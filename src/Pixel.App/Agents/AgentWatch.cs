using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using Pixel.App.Shell;
using Pixel.Core.Agents;

namespace Pixel.App.Agents;

/// <summary>
/// Feeds the cat's "AI tool is working / done / needs you" moods: Claude Code via hooks posting to a loopback
/// listener, and other tools (Cursor, VS Code, Codex…) via a CPU-activity heuristic.
/// </summary>
internal sealed class AgentWatchService : IDisposable
{
    public const int PreferredPort = 17861;
    private readonly AppHost _host;
    private readonly AgentStateTracker _tracker = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly ProcessWatcher _processes;
    private readonly CancellationTokenSource _stop = new();
    private TcpListener? _listener;

    public AgentWatchService(AppHost host)
    {
        _host = host;
        _processes = new ProcessWatcher(() => _host.Settings.AgentProcesses, e => _host.Dispatcher.BeginInvoke(() => _tracker.On(e)));
        StartListener();
        _timer.Tick += (_, _) =>
        {
            _processes.Sample();
            var state = _tracker.StateAt(DateTimeOffset.Now);
            if (_host.Cat.Overlay.Agent == state) return;
            Log.Info($"AI tool state: {state}");
            _host.Cat.Overlay = _host.Cat.Overlay with { Agent = state };
        };
        _timer.Start();
    }

    public int? Port { get; private set; }

    private void StartListener()
    {
        for (var port = PreferredPort; port < PreferredPort + 10; port++)
        {
            try
            {
                var l = new TcpListener(IPAddress.Loopback, port);
                l.Start();
                _listener = l;
                Port = port;
                _ = AcceptLoopAsync(l, _stop.Token);
                Log.Info($"agent listener on 127.0.0.1:{port}");
                return;
            }
            catch (SocketException)
            {
                // Port taken — try the next one.
            }
        }
        Log.Error("agent listener: no free port");
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(ct);
            }
            catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return;
            }
            _ = HandleAsync(client);
        }
    }

    /// <summary>Minimal HTTP: one small POST per connection, answered 204. Anything odd is dropped.</summary>
    private async Task HandleAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                var stream = client.GetStream();
                var buffer = new byte[64 * 1024];
                var read = 0;
                int headerEnd;
                while ((headerEnd = IndexOf(buffer, read, "\r\n\r\n"u8)) < 0)
                {
                    if (read == buffer.Length) return;
                    var n = await stream.ReadAsync(buffer.AsMemory(read), cts.Token);
                    if (n == 0) return;
                    read += n;
                }

                var head = Encoding.ASCII.GetString(buffer, 0, headerEnd);
                var lines = head.Split("\r\n");
                var request = lines[0].Split(' ');
                if (request.Length < 2 || request[0] != "POST") return;
                var length = lines.Select(l => l.Split(':', 2))
                    .Where(p => p.Length == 2 && p[0].Trim().Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                    .Select(p => int.TryParse(p[1].Trim(), out var v) ? v : 0).FirstOrDefault();
                length = Math.Clamp(length, 0, buffer.Length - headerEnd - 4);
                while (read < headerEnd + 4 + length)
                {
                    var n = await stream.ReadAsync(buffer.AsMemory(read), cts.Token);
                    if (n == 0) break;
                    read += n;
                }
                var body = Encoding.UTF8.GetString(buffer, headerEnd + 4, Math.Max(0, Math.Min(length, read - headerEnd - 4)));

                if (HookEventParser.Parse(request[1], body, DateTimeOffset.Now) is { } e)
                    await _host.Dispatcher.InvokeAsync(() => _tracker.On(e));
                await stream.WriteAsync("HTTP/1.1 204 No Content\r\nConnection: close\r\n\r\n"u8.ToArray(), cts.Token);
            }
            catch (Exception e) when (e is IOException or SocketException or OperationCanceledException)
            {
                // A hook that hangs up early is harmless.
            }
        }
    }

    private static int IndexOf(byte[] haystack, int length, ReadOnlySpan<byte> needle) =>
        haystack.AsSpan(0, length).IndexOf(needle);

    /// <summary>Adds Pixel's hooks to ~/.claude/settings.json after showing exactly what changes.</summary>
    public void ConnectClaudeCode()
    {
        if (Port is not { } port)
        {
            MessageBox.Show("Pixel couldn't open its local listener, so Claude Code can't reach it.", "Pixel");
            return;
        }
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json");
        var existing = File.Exists(path) ? File.ReadAllText(path) : null;
        if (ClaudeHookConfig.IsInstalled(existing) && existing!.Contains($"127.0.0.1:{port}/", StringComparison.Ordinal))
        {
            MessageBox.Show("Claude Code is already connected to Pixel. 🐾", "Pixel");
            return;
        }

        string merged;
        try
        {
            merged = ClaudeHookConfig.Merge(existing, port);
        }
        catch (System.Text.Json.JsonException)
        {
            MessageBox.Show($"{path} isn't valid JSON, so Pixel won't touch it.", "Pixel");
            return;
        }

        var preview =
            $"Pixel will add three hooks to\n{path}\n\n" +
            $"• UserPromptSubmit → {ClaudeHookConfig.Command(port, "working")}\n" +
            $"• Stop → {ClaudeHookConfig.Command(port, "done")}\n" +
            $"• Notification → {ClaudeHookConfig.Command(port, "needs-you")}\n\n" +
            "They only tell Pixel when Claude Code starts, finishes or needs you — no prompt text is sent, and nothing leaves this PC.
" +
            "Your other settings are kept, and a backup is saved as settings.json.pixel-backup.\n\nConnect now?";
        if (MessageBox.Show(preview, "Connect Claude Code", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (existing is not null) File.WriteAllText(path + ".pixel-backup", existing);
        File.WriteAllText(path, merged);
        _host.UpdateSettings(_host.Settings with { ClaudeHooksInstalled = true });
        Log.Info("Claude Code hooks installed");
        MessageBox.Show("Connected! Pixel will react the next time Claude Code works on something.", "Pixel");
    }

    public void Dispose()
    {
        _timer.Stop();
        _stop.Cancel();
        _listener?.Stop();
    }
}

/// <summary>Samples CPU use of watched AI tools (by process name) and turns it into agent events.</summary>
internal sealed class ProcessWatcher(Func<string> names, Action<AgentEvent> onEvent)
{
    private static readonly TimeSpan Every = TimeSpan.FromSeconds(2);
    private readonly Dictionary<string, (TimeSpan Cpu, DateTimeOffset At, ProcessActivityHeuristic H)> _state = [];
    private DateTimeOffset _last = DateTimeOffset.MinValue;

    public void Sample()
    {
        var now = DateTimeOffset.Now;
        if (now - _last < Every) return;
        _last = now;
        foreach (var name in names().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var cpu = TotalCpu(name);
            if (cpu is null)
            {
                if (_state.Remove(name, out var gone) && gone.H.Working)
                    onEvent(new AgentEvent(name, "proc", AgentEventKind.Done, now)); // tool closed mid-work
                continue;
            }
            if (_state.TryGetValue(name, out var prev))
            {
                var cores = (cpu.Value - prev.Cpu).TotalSeconds / Math.Max(0.1, (now - prev.At).TotalSeconds);
                if (prev.H.Sample(cores, now) is { } kind) onEvent(new AgentEvent(name, "proc", kind, now));
                _state[name] = (cpu.Value, now, prev.H);
            }
            else
            {
                _state[name] = (cpu.Value, now, new ProcessActivityHeuristic());
            }
        }
    }

    private static TimeSpan? TotalCpu(string name)
    {
        var processes = Process.GetProcessesByName(name);
        if (processes.Length == 0) return null;
        var total = TimeSpan.Zero;
        foreach (var p in processes)
        {
            try
            {
                total += p.TotalProcessorTime;
            }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Exited or not ours to inspect.
            }
            finally
            {
                p.Dispose();
            }
        }
        return total;
    }
}
