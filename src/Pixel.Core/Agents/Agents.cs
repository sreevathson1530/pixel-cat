using System.Text.Json;
using Pixel.Core.Behaviour;

namespace Pixel.Core.Agents;

public enum AgentEventKind { Working, Done, NeedsYou }

/// <param name="Source">Which tool reported it (claude-code, cursor, code, codex…).</param>
/// <param name="Session">Per-tool session id, so parallel sessions are tracked separately.</param>
public sealed record AgentEvent(string Source, string Session, AgentEventKind Kind, DateTimeOffset At);

/// <summary>
/// Folds AI-coding-tool events into one mood for the cat: someone needs you &gt; just finished (celebrate)
/// &gt; working &gt; nothing. Stale states expire so a crashed tool never leaves the cat stuck.
/// </summary>
public sealed class AgentStateTracker
{
    public static readonly TimeSpan CelebrateFor = TimeSpan.FromSeconds(4);
    public static readonly TimeSpan NeedsYouFor = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan StaleWorking = TimeSpan.FromMinutes(10);

    private readonly Dictionary<string, DateTimeOffset> _working = [];
    private readonly Dictionary<string, DateTimeOffset> _needsYou = [];
    private DateTimeOffset _celebrateUntil = DateTimeOffset.MinValue;

    public void On(AgentEvent e)
    {
        var key = $"{e.Source}/{e.Session}";
        switch (e.Kind)
        {
            case AgentEventKind.Working:
                _working[key] = e.At;
                _needsYou.Remove(key);
                break;
            case AgentEventKind.Done:
                _working.Remove(key);
                _needsYou.Remove(key);
                _celebrateUntil = e.At + CelebrateFor;
                break;
            case AgentEventKind.NeedsYou:
                _needsYou[key] = e.At;
                break;
        }
    }

    public AgentState StateAt(DateTimeOffset now)
    {
        Prune(_needsYou, now, NeedsYouFor);
        Prune(_working, now, StaleWorking);
        if (_needsYou.Count > 0) return AgentState.NeedsYou;
        if (now < _celebrateUntil) return AgentState.Done;
        return _working.Count > 0 ? AgentState.Working : AgentState.None;
    }

    private static void Prune(Dictionary<string, DateTimeOffset> d, DateTimeOffset now, TimeSpan max)
    {
        foreach (var k in d.Where(p => now - p.Value >= max).Select(p => p.Key).ToList()) d.Remove(k);
    }
}

/// <summary>Parses Claude Code hook calls: POST /event/{working|done|needs-you} with the hook's JSON on the body.</summary>
public static class HookEventParser
{
    public static AgentEvent? Parse(string path, string body, DateTimeOffset now)
    {
        AgentEventKind? kind = path.TrimEnd('/') switch
        {
            "/event/working" => AgentEventKind.Working,
            "/event/done" => AgentEventKind.Done,
            "/event/needs-you" => AgentEventKind.NeedsYou,
            _ => null,
        };
        if (kind is not { } k) return null;

        var session = "default";
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("session_id", out var s) && s.ValueKind == JsonValueKind.String &&
                s.GetString() is { Length: > 0 and <= 200 } id)
                session = id;
        }
        catch (JsonException)
        {
            // Body is optional; the path already says what happened.
        }
        return new AgentEvent("claude-code", session, k, now);
    }
}

/// <summary>
/// Best-effort "is this AI tool busy?" for tools without hooks (Cursor, VS Code + Copilot, Codex): sustained CPU
/// means working, a quiet spell after a real stretch of work means done. Short spikes and brief dips are ignored.
/// </summary>
public sealed class ProcessActivityHeuristic
{
    private const double BusyCores = 0.25;
    private const double QuietCores = 0.05;
    private static readonly TimeSpan BusyBeforeWorking = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan QuietBeforeDone = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan MinWorkForCelebration = TimeSpan.FromSeconds(20);

    private DateTimeOffset? _busySince, _quietSince, _workStart;

    public bool Working => _workStart is not null;

    /// <param name="cpuCores">CPU used since the last sample, in cores (1.0 = one core fully busy).</param>
    public AgentEventKind? Sample(double cpuCores, DateTimeOffset at)
    {
        var busy = cpuCores >= BusyCores;
        if (_workStart is { } started)
        {
            if (cpuCores > QuietCores)
            {
                _quietSince = null;
                return null;
            }
            _quietSince ??= at;
            if (at - _quietSince < QuietBeforeDone) return null;
            _workStart = null;
            _quietSince = null;
            _busySince = null;
            return at - started >= MinWorkForCelebration ? AgentEventKind.Done : null;
        }

        if (!busy)
        {
            _busySince = null;
            return null;
        }
        _busySince ??= at;
        if (at - _busySince < BusyBeforeWorking) return null;
        _workStart = _busySince;
        return AgentEventKind.Working;
    }
}
