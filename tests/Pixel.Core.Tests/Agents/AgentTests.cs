using Pixel.Core.Agents;
using Pixel.Core.Behaviour;

namespace Pixel.Core.Tests.Agents;

public class AgentStateTrackerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 9, 10, 0, 0, TimeSpan.Zero);
    private static AgentEvent E(string session, AgentEventKind kind, double atSeconds) =>
        new("claude-code", session, kind, T0.AddSeconds(atSeconds));

    [Fact]
    public void Nothing_happening_is_none() => Assert.Equal(AgentState.None, new AgentStateTracker().StateAt(T0));

    [Fact]
    public void Working_until_done_then_celebrates_briefly()
    {
        var t = new AgentStateTracker();
        t.On(E("a", AgentEventKind.Working, 0));
        Assert.Equal(AgentState.Working, t.StateAt(T0.AddSeconds(30)));
        t.On(E("a", AgentEventKind.Done, 60));
        Assert.Equal(AgentState.Done, t.StateAt(T0.AddSeconds(61)));
        Assert.Equal(AgentState.None, t.StateAt(T0.AddSeconds(66)));
    }

    [Fact]
    public void Still_working_while_any_session_works()
    {
        var t = new AgentStateTracker();
        t.On(E("a", AgentEventKind.Working, 0));
        t.On(E("b", AgentEventKind.Working, 1));
        t.On(E("a", AgentEventKind.Done, 10));
        Assert.Equal(AgentState.Working, t.StateAt(T0.AddSeconds(20)));
    }

    [Fact]
    public void Needs_you_wins_and_waits_until_the_next_event_or_two_minutes()
    {
        var t = new AgentStateTracker();
        t.On(E("a", AgentEventKind.Working, 0));
        t.On(E("b", AgentEventKind.NeedsYou, 5));
        Assert.Equal(AgentState.NeedsYou, t.StateAt(T0.AddSeconds(30)));
        Assert.Equal(AgentState.Working, t.StateAt(T0.AddSeconds(5 + 121)));

        t.On(E("c", AgentEventKind.NeedsYou, 200));
        t.On(E("c", AgentEventKind.Working, 210)); // user answered; the agent carries on
        Assert.Equal(AgentState.Working, t.StateAt(T0.AddSeconds(211)));
    }

    [Fact]
    public void Working_goes_stale_after_ten_quiet_minutes()
    {
        var t = new AgentStateTracker();
        t.On(E("a", AgentEventKind.Working, 0));
        Assert.Equal(AgentState.None, t.StateAt(T0.AddMinutes(11)));
    }
}

public class HookEventParserTests
{
    [Theory]
    [InlineData("/event/working", AgentEventKind.Working)]
    [InlineData("/event/done", AgentEventKind.Done)]
    [InlineData("/event/needs-you", AgentEventKind.NeedsYou)]
    public void Path_gives_the_kind(string path, AgentEventKind kind)
    {
        var e = HookEventParser.Parse(path, """{"session_id":"s1","hook_event_name":"Stop"}""", DateTimeOffset.Now);
        Assert.Equal(kind, e!.Kind);
        Assert.Equal("s1", e.Session);
    }

    [Theory]
    [InlineData("/event/explode")]
    [InlineData("/admin")]
    [InlineData("")]
    public void Unknown_paths_are_ignored(string path) =>
        Assert.Null(HookEventParser.Parse(path, "{}", DateTimeOffset.Now));

    [Fact]
    public void Missing_or_bad_body_still_counts_with_a_default_session()
    {
        var e = HookEventParser.Parse("/event/working", "not json", DateTimeOffset.Now);
        Assert.Equal("default", e!.Session);
    }
}

public class ProcessActivityHeuristicTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 9, 10, 0, 0, TimeSpan.Zero);

    private static List<AgentEventKind> Feed(ProcessActivityHeuristic h, IEnumerable<(double At, double Cpu)> samples) =>
        samples.Select(s => h.Sample(s.Cpu, T0.AddSeconds(s.At))).OfType<AgentEventKind>().ToList();

    private static IEnumerable<(double, double)> Span(double from, double to, double cpu) =>
        Enumerable.Range(0, (int)((to - from) / 2)).Select(i => (from + i * 2.0, cpu));

    [Fact]
    public void Sustained_busy_then_quiet_is_working_then_done()
    {
        var h = new ProcessActivityHeuristic();
        var events = Feed(h, Span(0, 60, 0.6).Concat(Span(60, 90, 0.01)));
        Assert.Equal([AgentEventKind.Working, AgentEventKind.Done], events);
    }

    [Fact]
    public void A_short_spike_is_ignored()
    {
        var h = new ProcessActivityHeuristic();
        Assert.Empty(Feed(h, Span(0, 6, 0.9).Concat(Span(6, 60, 0.0))));
    }

    [Fact]
    public void Brief_dips_while_working_do_not_end_the_work()
    {
        var h = new ProcessActivityHeuristic();
        var events = Feed(h, Span(0, 40, 0.6).Concat(Span(40, 44, 0.0)).Concat(Span(44, 80, 0.6)));
        Assert.Equal([AgentEventKind.Working], events);
    }
}

public class ClaudeHookConfigTests
{
    private static System.Text.Json.Nodes.JsonObject Parse(string json) =>
        System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();

    [Fact]
    public void Adds_the_three_hooks_and_keeps_everything_else()
    {
        var merged = Parse(ClaudeHookConfig.Merge("""{"model":"opus","statusLine":{"type":"command","command":"node x.js"}}""", 17861));
        Assert.Equal("opus", merged["model"]!.GetValue<string>());
        Assert.NotNull(merged["statusLine"]);
        var hooks = merged["hooks"]!.AsObject();
        foreach (var (evt, kind) in new[] { ("UserPromptSubmit", "working"), ("Stop", "done"), ("Notification", "needs-you") })
        {
            var command = hooks[evt]![0]!["hooks"]![0]!["command"]!.GetValue<string>();
            Assert.Contains($"http://127.0.0.1:17861/event/{kind}", command);
        }
    }

    [Fact]
    public void Running_twice_does_not_duplicate_and_updates_the_port()
    {
        var once = ClaudeHookConfig.Merge(null, 17861);
        var twice = Parse(ClaudeHookConfig.Merge(once, 17862));
        var entries = twice["hooks"]!["Stop"]!.AsArray();
        Assert.Single(entries);
        Assert.Contains("17862", entries[0]!["hooks"]![0]!["command"]!.GetValue<string>());
    }

    [Fact]
    public void Other_peoples_hooks_on_the_same_event_survive()
    {
        var existing = """{"hooks":{"Stop":[{"hooks":[{"type":"command","command":"echo bye"}]}]}}""";
        var stop = Parse(ClaudeHookConfig.Merge(existing, 17861))["hooks"]!["Stop"]!.AsArray();
        Assert.Equal(2, stop.Count);
        Assert.Equal("echo bye", stop[0]!["hooks"]![0]!["command"]!.GetValue<string>());
    }

    [Fact]
    public void Remove_takes_out_only_pixels_hooks()
    {
        var existing = ClaudeHookConfig.Merge("""{"hooks":{"Stop":[{"hooks":[{"type":"command","command":"echo bye"}]}]}}""", 17861);
        var removed = Parse(ClaudeHookConfig.Remove(existing));
        Assert.Single(removed["hooks"]!["Stop"]!.AsArray());
        Assert.Null(removed["hooks"]!["UserPromptSubmit"]);
    }

    [Fact]
    public void Invalid_existing_json_is_refused_rather_than_overwritten() =>
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => ClaudeHookConfig.Merge("{ not json", 17861));
}
