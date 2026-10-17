using Pixel.Core.Tools;

namespace Pixel.Core.Tests.Tools;

public class ToolRouterTests
{
    private static string[] Offered(string question) =>
        ToolRouter.For(question, ToolDefinitions.All).Select(t => t.Name).ToArray();

    [Theory]
    [InlineData("hello")]
    [InlineData("what's wrong on my screen and how do I fix it?")]
    [InlineData("Write a short Python function that counts how many times each word appears")]
    [InlineData("summarise this page")]
    [InlineData("who is the person on my wallpaper?")]
    [InlineData("how do I run this code?")]
    [InlineData("what does this error mean?")]
    public void Ordinary_questions_get_no_tools_so_the_model_just_answers(string q) => Assert.Empty(Offered(q));

    [Theory]
    [InlineData("remind me in 10 minutes to drink water", "set_reminder")]
    [InlineData("set a reminder at 17:30 to call mum", "set_reminder")]
    [InlineData("start a pomodoro", "start_pomodoro")]
    [InlineData("stop the pomodoro", "stop_pomodoro")]
    [InlineData("note that the API key rotates on Friday", "add_note")]
    [InlineData("remember that my locker code is 4412", "add_note")]
    [InlineData("copy that code for me", "copy_to_clipboard")]
    [InlineData("open notepad", "open_app")]
    [InlineData("open github.com", "open_url")]
    [InlineData("open C:\\Users\\me\\report.pdf", "open_file")]
    [InlineData("search the web for wpf layered windows", "web_search")]
    [InlineData("google how to cook rice", "web_search")]
    [InlineData("look up the weather in Chennai", "web_search")]
    public void Action_requests_get_just_the_matching_tool(string q, string tool) => Assert.Contains(tool, Offered(q));

    [Fact]
    public void Action_requests_get_only_a_few_tools()
    {
        Assert.Single(Offered("remind me in 10 minutes to drink water"));
        Assert.Single(Offered("open notepad"));
        Assert.True(Offered("start a pomodoro").Length <= 2);
    }

    [Fact]
    public void Offered_tools_keep_their_original_order_and_definitions()
    {
        var offered = ToolRouter.For("remind me to stretch and note that I did", ToolDefinitions.All);
        Assert.Equal(["set_reminder", "add_note"], offered.Select(t => t.Name));
        Assert.All(offered, t => Assert.Contains(t, ToolDefinitions.All));
    }
}
