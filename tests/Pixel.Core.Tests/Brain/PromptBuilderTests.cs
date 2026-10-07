using Pixel.Core.Brain;
using Pixel.Core.Screen;

namespace Pixel.Core.Tests.Brain;

public class PromptBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 21, 30, 0, TimeSpan.FromHours(5.5));
    private static ChatMessage U(string t) => new(ChatRole.User, t);
    private static ChatMessage A(string t) => new(ChatRole.Assistant, t);

    private static ScreenSnapshot Screen(string fg, string other = "", string title = "main.py - VS Code") =>
        new(Now, title, "Code", fg, other);

    [Fact]
    public void Starts_with_a_stable_persona_system_message()
    {
        var a = PromptBuilder.Build([U("hi")], null, Now);
        var b = PromptBuilder.Build([U("hi")], null, Now.AddHours(3));
        Assert.Equal(ChatRole.System, a[0].Role);
        Assert.Contains("Pixel", a[0].Content);
        Assert.Equal(a[0].Content, b[0].Content); // identical prefix → llama-server reuses its cache
    }

    [Fact]
    public void Current_time_rides_on_the_latest_question()
    {
        var msgs = PromptBuilder.Build([U("hi")], null, Now);
        Assert.Contains("7 October 2026", msgs[^1].Content);
        Assert.EndsWith("hi", msgs[^1].Content);
    }

    [Fact]
    public void Screen_text_goes_into_the_latest_user_message_active_window_first()
    {
        var msgs = PromptBuilder.Build([U("what is this?")], Screen("def foo(): pass", "Spotify - Lofi"), Now);
        var last = msgs[^1];
        Assert.Equal(ChatRole.User, last.Role);
        Assert.Contains("main.py - VS Code", last.Content);
        Assert.True(last.Content.IndexOf("def foo()", StringComparison.Ordinal) < last.Content.IndexOf("Spotify", StringComparison.Ordinal));
        Assert.EndsWith("what is this?", last.Content);
    }

    [Fact]
    public void Without_screen_there_is_no_screen_block()
    {
        var msgs = PromptBuilder.Build([U("hello")], null, Now);
        Assert.DoesNotContain("<screen>", msgs[^1].Content);
    }

    [Fact]
    public void Earlier_turns_never_carry_screen_text()
    {
        var msgs = PromptBuilder.Build([U("first"), A("answer"), U("second")], Screen("SECRET-ON-SCREEN"), Now);
        Assert.Equal("first", msgs[1].Content);
        Assert.Contains("SECRET-ON-SCREEN", msgs[^1].Content);
    }

    [Fact]
    public void Screen_text_is_capped()
    {
        var msgs = PromptBuilder.Build([U("q")], Screen(new string('a', 30_000), new string('b', 30_000)), Now);
        Assert.InRange(msgs[^1].Content.Length, 1, PromptBuilder.MaxScreenChars + 600);
    }

    [Fact]
    public void Oldest_turns_are_dropped_to_fit_the_budget()
    {
        var convo = new List<ChatMessage>();
        for (var i = 0; i < 40; i++)
        {
            convo.Add(U($"question {i} " + new string('x', 1500)));
            convo.Add(A($"answer {i} " + new string('y', 1500)));
        }
        convo.Add(U("latest question"));

        var msgs = PromptBuilder.Build(convo, null, Now);

        Assert.True(msgs.Sum(m => m.Content.Length) <= PromptBuilder.MaxPromptChars);
        Assert.EndsWith("latest question", msgs[^1].Content);
        Assert.DoesNotContain(msgs, m => m.Content.StartsWith("question 0 "));
        Assert.Equal(ChatRole.User, msgs[1].Role); // history still starts on a user turn
    }

    [Fact]
    public void A_huge_question_is_truncated_not_rejected()
    {
        var msgs = PromptBuilder.Build([U(new string('z', 100_000))], Screen("code"), Now);
        Assert.True(msgs.Sum(m => m.Content.Length) <= PromptBuilder.MaxPromptChars);
        Assert.Contains("[truncated]", msgs[^1].Content);
    }

    [Fact]
    public void Tool_call_and_its_result_stay_together_when_trimming()
    {
        var call = new ToolInvocation("c1", "set_reminder", """{"minutes":5}""");
        var convo = new List<ChatMessage>
        {
            U("old " + new string('x', PromptBuilder.MaxPromptChars)),
            A("ok"),
            U("remind me in 5"),
            new(ChatRole.Assistant, "", ToolCalls: [call]),
            new(ChatRole.Tool, "Reminder set", ToolCallId: "c1"),
            A("Done!"),
            U("thanks"),
        };
        var msgs = PromptBuilder.Build(convo, null, Now);
        var toolIdx = msgs.FindIndex(m => m.Role == ChatRole.Tool);
        Assert.True(toolIdx > 0);
        Assert.Equal("c1", msgs[toolIdx - 1].ToolCalls![0].Id);
    }
}
