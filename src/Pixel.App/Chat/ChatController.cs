using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using Pixel.App.Shell;
using Pixel.App.Tools;
using Pixel.Core.Art;
using Pixel.Core.Behaviour;
using Pixel.Core.Brain;
using Pixel.Core.Screen;
using Pixel.Core.Storage;
using Pixel.Core.Tools;

namespace Pixel.App.Chat;

/// <summary>
/// Owns the chat panel and the current conversation: shows/hides the panel beside the cat, runs turns
/// against the brain and mirrors progress onto the cat (thinking / talking).
/// </summary>
internal sealed class ChatController
{
    private readonly AppHost _host;
    private IBrain _brain = new EchoBrain();
    private readonly Func<Task<ScreenSnapshot?>> _snapshot;
    private readonly List<ChatMessage> _conversation = [];
    private ChatWindow? _window;
    private Task? _init;
    private CancellationTokenSource? _turn;
    private Task<ScreenSnapshot?> _screen = Task.FromResult<ScreenSnapshot?>(null);
    private readonly Dictionary<string, TaskCompletionSource<bool>> _pendingConfirms = [];
    private int _confirmSeq;

    private const int MaxToolRounds = 3;
    private static readonly TimeSpan StallLimit = TimeSpan.FromMinutes(4);
    private const string StallMessage =
        "Mrrp… my brain went quiet for 4 minutes, so I stopped. Your PC is probably short on free memory — "
        + "close a few apps, or pick the smaller brain in Settings, then ask again.";
    private bool _opening;
    private StallWatchdog? _watchdog;
    private readonly ToolExecutor _tools;
    private readonly HistoryStore? _history;
    private long? _historyId;

    public ChatController(AppHost host, Func<Task<ScreenSnapshot?>> snapshot, ToolExecutor tools, HistoryStore? history)
    {
        _host = host;
        _snapshot = snapshot;
        _tools = tools;
        _history = history;
        host.ChatToggled += () => _ = ToggleAsync();
        host.Vanishing += () =>
        {
            Hide();
            EndConversation();
        };
        host.Revived += EndConversation;
    }

    public bool IsOpen => _window?.IsVisible == true;

    public void UseBrain(IBrain brain) => _brain = brain;

    /// <summary>Shows a Yes/No card in the chat and waits for the answer (false if the chat closes).</summary>
    public async Task<bool> ConfirmAsync(string title, string detail)
    {
        var (id, answer) = ShowAction(title, detail, needsConfirm: true);
        var ok = await WaitForUserAsync(answer);
        _window?.Post(new { type = "actionResult", id, ok, text = ok ? "Okay!" : "Skipped." });
        return ok;
    }

    private async Task<bool> WaitForUserAsync(Task<bool> answer)
    {
        if (answer.IsCompleted) return await answer;
        _watchdog?.Pause();
        try
        {
            return await answer;
        }
        finally
        {
            _watchdog?.Pet();
        }
    }

    private void ReleasePendingConfirms()
    {
        foreach (var p in _pendingConfirms.Values) p.TrySetResult(false);
        _pendingConfirms.Clear();
    }

    /// <summary>Adds an action card; with <paramref name="needsConfirm"/> the task completes on Yes/No.</summary>
    private (string Id, Task<bool> Answer) ShowAction(string title, string detail, bool needsConfirm)
    {
        var id = $"action-{++_confirmSeq}";
        if (_window is null) return (id, Task.FromResult(false));
        _window.Post(new { type = "action", id, title, detail, needsConfirm });
        if (!needsConfirm) return (id, Task.FromResult(true));
        var tcs = new TaskCompletionSource<bool>();
        _pendingConfirms[id] = tcs;
        return (id, tcs.Task);
    }

    /// <summary>Validates, confirms (when needed) and runs one tool call; returns the result text for the model.</summary>
    private async Task<string> RunToolAsync(ToolInvocation call)
    {
        var request = ToolArgs.Parse(call, p => File.Exists(p) || Directory.Exists(p));
        var action = _tools.Prepare(call.Name, request); // refused actions skip confirmation and report why
        var (id, answer) = ShowAction(action.Title, action.Detail, action.NeedsConfirm);
        if (!await WaitForUserAsync(answer))
        {
            _window?.Post(new { type = "actionResult", id, ok = false, text = "Skipped." });
            return "The user declined this action.";
        }
        var (ok, text) = action.Run();
        _window?.Post(new { type = "actionResult", id, ok, text });
        Log.Info($"tool {call.Name}: {(ok ? "ok" : "failed")}");
        return text;
    }

    /// <summary>Raised when a conversation ends (vanish, revive, "new chat") with its messages.</summary>
    public event Action<IReadOnlyList<ChatMessage>>? ConversationEnded;

    public async Task ToggleAsync()
    {
        if (IsOpen)
        {
            Hide();
            return;
        }
        if (_opening) return; // a second press while the panel is still starting up
        _opening = true;
        try
        {
            await OpenAsync();
        }
        finally
        {
            _opening = false;
        }
    }

    private async Task OpenAsync()
    {
        _screen = SafeSnapshot(); // the panel is excluded from capture, so reading can overlap showing it
        _host.Cat.HoldStill = true;
        await EnsureWindowAsync();
        PlaceBesideCat();
        _window!.Show();
        _window.SetIdle(false);
        _window.FocusInput();
        _window.Post(new { type = "status", text = "reading your screen…", busy = true });
        var screen = await _screen;
        _window.Post(new { type = "context", text = DescribeContext(screen) });
        _window.Post(new { type = "status", text = "here for you", busy = false });
    }

    /// <summary>Opens the panel (if needed) and asks <paramref name="text"/> as if typed.</summary>
    public async Task AskAsync(string text)
    {
        if (!IsOpen) await ToggleAsync();
        _window!.Post(new { type = "ask", text });
    }

    private async Task<ScreenSnapshot?> SafeSnapshot()
    {
        try
        {
            return await _snapshot();
        }
        catch (Exception e)
        {
            Log.Error("screen snapshot", e);
            return null;
        }
    }

    public void Hide()
    {
        _host.Cat.HoldStill = false;
        if (_window is null || !_window.IsVisible) return;
        _window.Hide();
        _window.SetIdle(true);
    }

    /// <summary>Writes a message to local history as it happens (never the screen text).</summary>
    private void Remember(ChatRole role, string content)
    {
        if (_history is null) return;
        try
        {
            _historyId ??= _history.Start(DateTimeOffset.Now);
            _history.Append(_historyId.Value, role, content, DateTimeOffset.Now);
        }
        catch (Exception e)
        {
            Log.Error("history write failed", e);
        }
    }

    private void EndConversation()
    {
        if (_historyId is { } hid)
        {
            try
            {
                _history?.End(hid, DateTimeOffset.Now);
            }
            catch (Exception e)
            {
                Log.Error("history end failed", e);
            }
            _historyId = null;
        }
        _turn?.Cancel();
        ReleasePendingConfirms();
        if (_conversation.Count > 0) ConversationEnded?.Invoke(_conversation.ToArray());
        _conversation.Clear();
        _window?.Post(new { type = "reset" });
        SetCatChat(ChatState.None);
    }

    private async Task EnsureWindowAsync()
    {
        if (_window is null)
        {
            _window = new ChatWindow();
            _window.Message += OnMessage;
            _window.Show(); // WebView2 needs a live HWND to initialise
            _window.Hide();
            _init = _window.InitializeAsync();
        }
        await _init!;
        await _window.Ready;
    }

    private void PlaceBesideCat()
    {
        var cat = _host.Cat.Bounds;
        var area = SystemParameters.WorkArea;
        var w = _window!.Width;
        var h = _window.Height;
        var left = cat.Right + 4 + w <= area.Right ? cat.Right + 4 : cat.Left - w - 4;
        var top = cat.Bottom - h;
        _window.Left = Math.Clamp(left, area.Left + 4, area.Right - w - 4);
        _window.Top = Math.Clamp(top, area.Top + 4, area.Bottom - h - 4);
    }

    private static string DescribeContext(ScreenSnapshot? s) => s is null
        ? "Screen not available"
        : string.IsNullOrWhiteSpace(s.WindowTitle) ? "Reading your screen" : $"Seeing: {s.WindowTitle}";

    private void OnMessage(JsonElement m)
    {
        switch (m.GetProperty("type").GetString())
        {
            case "ready":
                _window!.Post(new { type = "avatar", dataUrl = AvatarDataUrl(_host.Cat.Palette) });
                break;
            case "send":
                var text = m.GetProperty("text").GetString() ?? "";
                var useScreen = !m.TryGetProperty("useScreen", out var u) || u.GetBoolean();
                _ = RunTurnAsync(text, useScreen);
                break;
            case "copy":
                TrySetClipboard(m.GetProperty("text").GetString() ?? "");
                break;
            case "openLink":
                _ = OpenLinkAsync(m.GetProperty("href").GetString());
                break;
            case "close":
                Hide();
                break;
            case "newChat":
                EndConversation();
                _screen = SafeSnapshot();
                break;
            case "drag":
                _window!.BeginDrag();
                break;
            case "stop":
                _turn?.Cancel();
                break;
            case "confirm":
                var cid = m.GetProperty("id").GetString() ?? "";
                if (_pendingConfirms.Remove(cid, out var pending)) pending.TrySetResult(m.GetProperty("ok").GetBoolean());
                break;
        }
    }

    private async Task RunTurnAsync(string text, bool useScreen)
    {
        var screen = useScreen ? await _screen : null;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        double? firstToken = null;
        _turn?.Cancel();
        var cts = _turn = new CancellationTokenSource();
        using var watchdog = new StallWatchdog(cts, StallLimit);
        _watchdog = watchdog;
        // Stop (or a newer question) must also release any card still waiting for a click.
        // On the UI thread (Stop, new question, vanish) release right away, before a newer turn adds its own cards.
        using var release = cts.Token.Register(() =>
        {
            if (_host.Dispatcher.CheckAccess()) ReleasePendingConfirms();
            else _host.Dispatcher.BeginInvoke(ReleasePendingConfirms);
        });
        _conversation.Add(new ChatMessage(ChatRole.User, text));
        Remember(ChatRole.User, text);
        SetCatChat(ChatState.Thinking);
        var answer = new System.Text.StringBuilder();
        try
        {
            // The model may call tools; run them and let it continue (at most a few rounds).
            for (var round = 0; round < MaxToolRounds; round++)
            {
                var calls = new List<ToolInvocation>();
                var roundText = new System.Text.StringBuilder();
                await foreach (var ev in _brain.AskAsync(_conversation, screen, cts.Token))
                {
                    switch (ev)
                    {
                        case StatusEvent s:
                            watchdog.Pet();
                            _window!.Post(new { type = "status", text = s.Text, busy = s.Busy });
                            break;
                        case ProgressEvent p:
                            watchdog.Pet();
                            _window!.Post(new { type = "status", text = $"reading… {p.Fraction:P0}", busy = true });
                            break;
                        case TokenEvent t:
                            watchdog.Pet();
                            if (answer.Length == 0)
                            {
                                SetCatChat(ChatState.Talking);
                                firstToken = watch.Elapsed.TotalSeconds;
                            }
                            answer.Append(t.Text);
                            roundText.Append(t.Text);
                            _window!.Post(new { type = "token", text = t.Text });
                            break;
                        case ToolCallEvent c:
                            calls.Add(c.Call);
                            break;
                        case ErrorEvent e:
                            _window!.Post(new { type = "error", text = e.Message });
                            return;
                    }
                }

                if (calls.Count == 0)
                {
                    if (roundText.Length > 0) _conversation.Add(new ChatMessage(ChatRole.Assistant, roundText.ToString()));
                    Remember(ChatRole.Assistant, answer.ToString());
                    break;
                }

                _conversation.Add(new ChatMessage(ChatRole.Assistant, roundText.ToString(), ToolCalls: calls));
                foreach (var call in calls)
                    _conversation.Add(new ChatMessage(ChatRole.Tool, await RunToolAsync(call), ToolCallId: call.Id));
                SetCatChat(ChatState.Thinking);
            }
            _window!.Post(new { type = "done" });
        }
        catch (OperationCanceledException) when (watchdog.Fired)
        {
            Log.Info("turn stalled; cancelled by watchdog");
            _window?.Post(new { type = "error", text = StallMessage });
        }
        catch (OperationCanceledException)
        {
            _window?.Post(new { type = "done" });
        }
        catch (Exception e)
        {
            Log.Error("chat turn failed", e);
            _window?.Post(new { type = "error", text = "Mrrp… something broke on my side. Try again?" });
        }
        finally
        {
            Log.Info($"turn: screen={(screen is null ? "off" : "on")} first token {firstToken:0.0}s, total {watch.Elapsed.TotalSeconds:0.0}s, {answer.Length} chars");
            if (ReferenceEquals(_watchdog, watchdog)) _watchdog = null;
            if (ReferenceEquals(_turn, cts)) SetCatChat(ChatState.None);
            _window?.Post(new { type = "status", text = "here for you", busy = false });
        }
    }

    private void SetCatChat(ChatState state) => _host.Cat.Overlay = _host.Cat.Overlay with { Chat = state };

    private static void TrySetClipboard(string text)
    {
        try { Clipboard.SetText(text); }
        catch (Exception e) { Log.Error("clipboard", e); }
    }

    /// <summary>Links in answers can be crafted from screen text, so every click is checked and confirmed.</summary>
    private async Task OpenLinkAsync(string? href)
    {
        if (!UrlSafety.TryCheck(href, out var uri, out var error, h => System.Net.Dns.GetHostAddresses(h)))
        {
            var (id, _) = ShowAction("Link blocked", href ?? "", needsConfirm: false);
            _window?.Post(new { type = "actionResult", id, ok = false, text = error });
            return;
        }
        if (await ConfirmAsync("Open this link?", UrlSafety.Display(uri!)))
            Process.Start(new ProcessStartInfo(uri!.AbsoluteUri) { UseShellExecute = true })?.Dispose();
    }

    private static string AvatarDataUrl(Palette palette)
    {
        var px = CatRenderer.Render(new CatPose(BodyShape.Sit), palette);
        var head = new uint[16 * 16];
        for (var y = 0; y < 16; y++)
        for (var x = 0; x < 16; x++)
            head[y * 16 + x] = px[(9 + y) * CatRenderer.Width + 18 + x];
        using var ms = new MemoryStream();
        PngWriter.Write(ms, 16, 16, head);
        return "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
    }
}
