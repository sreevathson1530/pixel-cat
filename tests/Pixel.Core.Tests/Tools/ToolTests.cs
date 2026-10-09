using Pixel.Core.Brain;
using Pixel.Core.Tools;

namespace Pixel.Core.Tests.Tools;

public class ToolArgsTests
{
    private static ToolRequest Parse(string name, string json, Func<string, bool>? exists = null) =>
        ToolArgs.Parse(new ToolInvocation("c1", name, json), exists ?? (_ => true));

    [Theory]
    [InlineData("set_reminder", true)]
    [InlineData("start_pomodoro", false)]
    [InlineData("stop_pomodoro", false)]
    [InlineData("add_note", false)]
    [InlineData("copy_to_clipboard", true)]
    [InlineData("open_url", true)]
    [InlineData("open_app", true)]
    [InlineData("open_file", true)]
    [InlineData("web_search", true)]
    public void Only_harmless_tools_skip_confirmation(string name, bool confirm) =>
        Assert.Equal(confirm, ToolPolicy.RequiresConfirmation(name));

    [Fact]
    public void Every_defined_tool_has_a_policy_and_valid_schema()
    {
        foreach (var t in ToolDefinitions.All)
        {
            _ = ToolPolicy.RequiresConfirmation(t.Name);
            using var schema = System.Text.Json.JsonDocument.Parse(t.ParametersJson);
            Assert.Equal("object", schema.RootElement.GetProperty("type").GetString());
        }
    }

    [Fact]
    public void Reminder_in_minutes_parses()
    {
        var r = Assert.IsType<ToolRequest.SetReminder>(Parse("set_reminder", """{"minutes": 5, "message": "drink water"}"""));
        Assert.Equal(TimeSpan.FromMinutes(5), r.In);
        Assert.Equal("drink water", r.Message);
    }

    [Fact]
    public void Reminder_at_clock_time_parses()
    {
        var r = Assert.IsType<ToolRequest.SetReminder>(Parse("set_reminder", """{"at": "17:30", "message": "call mom"}"""));
        Assert.Equal(new TimeOnly(17, 30), r.At);
    }

    [Fact]
    public void Reminder_without_a_time_is_invalid() =>
        Assert.IsType<ToolRequest.Invalid>(Parse("set_reminder", """{"message": "x"}"""));

    [Theory]
    [InlineData("""{"url": "javascript:alert(1)"}""")]
    [InlineData("""{"url": "file:///C:/Windows/System32/cmd.exe"}""")]
    [InlineData("""{"url": "not a url"}""")]
    public void Open_url_only_allows_web_links(string json) =>
        Assert.IsType<ToolRequest.Invalid>(Parse("open_url", json));

    [Fact]
    public void Open_url_accepts_https() =>
        Assert.Equal("https://learn.microsoft.com/", Assert.IsType<ToolRequest.OpenUrl>(Parse("open_url", """{"url":"https://learn.microsoft.com/"}""")).Url.AbsoluteUri);

    [Fact]
    public void Web_search_builds_an_encoded_url()
    {
        var r = Assert.IsType<ToolRequest.WebSearch>(Parse("web_search", """{"query": "c# list & dict"}"""));
        Assert.Equal("https://www.google.com/search?q=c%23%20list%20%26%20dict", r.Url.AbsoluteUri);
    }

    [Fact]
    public void Open_file_must_exist() =>
        Assert.IsType<ToolRequest.Invalid>(Parse("open_file", """{"path": "C:\nope.txt"}""", _ => false));

    [Fact]
    public void Bad_json_gives_a_friendly_error() =>
        Assert.Contains("understand", Assert.IsType<ToolRequest.Invalid>(Parse("add_note", "{oops")).Error);

    [Fact]
    public void Unknown_tool_is_invalid() =>
        Assert.IsType<ToolRequest.Invalid>(Parse("format_disk", "{}"));

    [Fact]
    public void Pomodoro_defaults_to_25_and_5() =>
        Assert.Equal(new ToolRequest.StartPomodoro(25, 5), Parse("start_pomodoro", "{}"));

    [Fact]
    public void Requests_describe_themselves_for_the_action_card()
    {
        var (title, detail) = Parse("copy_to_clipboard", """{"text": "print('hi')"}""").Describe();
        Assert.Equal("Copy to clipboard", title);
        Assert.Contains("print('hi')", detail);
    }
}
