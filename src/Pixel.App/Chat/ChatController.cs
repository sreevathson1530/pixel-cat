using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using Pixel.App.Shell;
using Pixel.Core.Art;
using Pixel.Core.Behaviour;
using Pixel.Core.Brain;
using Pixel.Core.Screen;

namespace Pixel.App.Chat;

/// <summary>
/// Owns the chat panel and the current conversation: shows/hides the panel beside the cat, runs turns
/// against the brain and mirrors progress onto the cat (thinking / talking).
/// </summary>
internal sealed class ChatController
{
    private readonly AppHost _host;
    private readonly IBrain _brain;
    private readonly Func<ScreenSnapshot?> _snapshot;
    private readonly List<ChatMessage> _conversation = [];
    private ChatWindow? _window;
    private Task? _init;
    private CancellationTokenSource? _turn;
    private ScreenSnapshot? _screen;

    public ChatController(AppHost host, IBrain brain, Func<ScreenSnapshot?> snapshot)
    {
        _host = host;
        _brain = brain;
        _snapshot = snapshot;
        host.ChatToggled += () => _ = ToggleAsync();
        host.Vanishing += () =>
        {
            Hide();
            EndConversation();
        };
        host.Revived += EndConversation;
    }

    public bool IsOpen => _window?.IsVisible == true;

    /// <summary>Raised when a conversation ends (vanish, revive, "new chat") with its messages.</summary>
    public event Action<IReadOnlyList<ChatMessage>>? ConversationEnded;

    public async Task ToggleAsync()
    {
        if (IsOpen)
        {
            Hide();
            return;
        }
        _screen = _snapshot(); // read the screen before the panel covers it
        _host.Cat.HoldStill = true;
        await EnsureWindowAsync();
        PlaceBesideCat();
        _window!.Show();
        _window.SetIdle(false);
        _window.FocusInput();
        _window.Post(new { type = "context", text = DescribeContext(_screen) });
    }

    public void Hide()
    {
        _host.Cat.HoldStill = false;
        if (_window is null || !_window.IsVisible) return;
        _window.Hide();
        _window.SetIdle(true);
    }

    private void EndConversation()
    {
        _turn?.Cancel();
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
                _ = RunTurnAsync(text, useScreen ? _screen : null);
                break;
            case "copy":
                TrySetClipboard(m.GetProperty("text").GetString() ?? "");
                break;
            case "openLink":
                OpenLink(m.GetProperty("href").GetString());
                break;
            case "close":
                Hide();
                break;
            case "newChat":
                EndConversation();
                _screen = _snapshot();
                _window!.Post(new { type = "context", text = DescribeContext(_screen) });
                break;
            case "drag":
                _window!.BeginDrag();
                break;
        }
    }

    private async Task RunTurnAsync(string text, ScreenSnapshot? screen)
    {
        _turn?.Cancel();
        var cts = _turn = new CancellationTokenSource();
        _conversation.Add(new ChatMessage(ChatRole.User, text));
        SetCatChat(ChatState.Thinking);
        var answer = new System.Text.StringBuilder();
        try
        {
            await foreach (var ev in _brain.AskAsync(_conversation, screen, cts.Token))
            {
                switch (ev)
                {
                    case StatusEvent s:
                        _window!.Post(new { type = "status", text = s.Text, busy = s.Busy });
                        break;
                    case TokenEvent t:
                        if (answer.Length == 0) SetCatChat(ChatState.Talking);
                        answer.Append(t.Text);
                        _window!.Post(new { type = "token", text = t.Text });
                        break;
                    case ErrorEvent e:
                        _window!.Post(new { type = "error", text = e.Message });
                        return;
                }
            }
            _window!.Post(new { type = "done" });
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
            if (answer.Length > 0) _conversation.Add(new ChatMessage(ChatRole.Assistant, answer.ToString()));
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

    private static void OpenLink(string? href)
    {
        if (!Uri.TryCreate(href, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return;
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
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
