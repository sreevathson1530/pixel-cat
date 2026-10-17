using Pixel.Core.Brain;
using Pixel.Core.Tools;

namespace Pixel.Core.Tests.Tools;

public class ReminderTextTests
{
    private static ToolRequest Parse(string json, string asked) =>
        ToolArgs.Parse(new ToolInvocation("c1", "set_reminder", json), _ => true, asked);

    [Fact]
    public void Missing_minutes_are_taken_from_what_you_asked()
    {
        var r = Assert.IsType<ToolRequest.SetReminder>(Parse("""{"message":"remind me in 10 minutes"}""", "remind me in 10 minutes to drink water"));
        Assert.Equal(TimeSpan.FromMinutes(10), r.In);
        Assert.Equal("drink water", r.Message);
    }

    [Theory]
    [InlineData("remind me in 2 hours to stand up", 120, "stand up")]
    [InlineData("remind me in 1 hour and 30 minutes to call mum", 90, "call mum")]
    [InlineData("in 45 mins remind me about the meeting", 45, "the meeting")]
    [InlineData("set a reminder in 5 min that the tea is ready", 5, "the tea is ready")]
    public void Durations_in_your_words(string asked, int minutes, string what)
    {
        var r = Assert.IsType<ToolRequest.SetReminder>(Parse("{}", asked));
        Assert.Equal(TimeSpan.FromMinutes(minutes), r.In);
        Assert.Equal(what, r.Message);
    }

    [Theory]
    [InlineData("remind me at 17:30 to call mum", 17, 30)]
    [InlineData("remind me at 5pm to leave", 17, 0)]
    [InlineData("remind me at 9:15 am to join the call", 9, 15)]
    public void Clock_times_in_your_words(string asked, int hour, int minute)
    {
        var r = Assert.IsType<ToolRequest.SetReminder>(Parse("""{"message":"x"}""", asked));
        Assert.Equal(new TimeOnly(hour, minute), r.At);
    }

    [Fact]
    public void What_the_model_gave_wins_when_it_is_complete()
    {
        var r = Assert.IsType<ToolRequest.SetReminder>(Parse("""{"minutes":3,"message":"Drink water!"}""", "remind me in 10 minutes to drink water"));
        Assert.Equal(TimeSpan.FromMinutes(3), r.In);
        Assert.Equal("Drink water!", r.Message);
    }

    [Fact]
    public void Without_any_time_it_still_asks_for_one() =>
        Assert.IsType<ToolRequest.Invalid>(Parse("""{"message":"water"}""", "remind me to drink water"));

    [Fact]
    public void Older_callers_without_your_words_still_work() =>
        Assert.IsType<ToolRequest.SetReminder>(ToolArgs.Parse(new ToolInvocation("c1", "set_reminder", """{"minutes":5,"message":"m"}"""), _ => true));
}
