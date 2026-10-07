using Pixel.Core.Brain;
using Pixel.Core.Tools;

namespace Pixel.Core.Tests.Tools;

public class ToolSafetyTests
{
    private static ToolRequest Parse(string name, string json) =>
        ToolArgs.Parse(new ToolInvocation("c1", name, json), _ => true);

    [Theory]
    [InlineData("https://google.com@evil.example/login")]
    [InlineData("http://127.0.0.1:52592/slots")]
    [InlineData("http://localhost/admin")]
    [InlineData("http://192.168.1.1/")]
    [InlineData("http://10.0.0.5/")]
    [InlineData("http://169.254.169.254/latest/meta-data")]
    [InlineData("http://[::1]/")]
    [InlineData("http://intranet/")]
    public void Links_that_hide_their_host_or_point_inside_this_network_are_refused(string url)
    {
        Assert.False(UrlSafety.TryCheck(url, out _, out _));
        Assert.IsType<ToolRequest.Invalid>(Parse("open_url", $$"""{"url":"{{url}}"}"""));
    }

    [Fact]
    public void Ordinary_web_links_are_allowed()
    {
        Assert.True(UrlSafety.TryCheck("https://docs.python.org/3/tutorial/", out var uri, out _));
        Assert.Equal("docs.python.org", uri!.IdnHost);
    }

    [Fact]
    public void Display_shows_the_real_host_first()
    {
        UrlSafety.TryCheck("https://xn--pple-43d.com/login", out var uri, out _);
        Assert.StartsWith("xn--pple-43d.com", UrlSafety.Display(uri!));
    }

    [Theory]
    [InlineData(@"C:\Users\me\Downloads\setup.exe")]
    [InlineData(@"C:\Users\me\run.bat")]
    [InlineData(@"C:\Users\me\script.ps1")]
    [InlineData(@"C:\Users\me\thing.lnk")]
    [InlineData(@"C:\Users\me\page.hta")]
    [InlineData(@"C:\Users\me\x.vbs")]
    [InlineData(@"C:\Users\me\setup.msi")]
    [InlineData(@"C:\Users\me\shortcut.url")]
    [InlineData(@"C:\Users\me\evil.exe.")]
    public void Programs_and_scripts_are_never_opened_as_files(string path) =>
        Assert.IsType<ToolRequest.Invalid>(Parse("open_file", $$"""{"path":{{System.Text.Json.JsonSerializer.Serialize(path)}}}"""));

    [Fact]
    public void Documents_can_be_opened() =>
        Assert.IsType<ToolRequest.OpenFile>(Parse("open_file", """{"path":"C:\\Users\\me\\notes.txt"}"""));

    [Theory]
    [InlineData("cmd")]
    [InlineData("powershell")]
    [InlineData("pwsh.exe")]
    [InlineData("mshta")]
    [InlineData("rundll32")]
    [InlineData("wscript")]
    [InlineData("ms-msdt:")]
    [InlineData("search-ms:query=x")]
    [InlineData("%COMSPEC%")]
    public void Shells_interpreters_and_protocol_handlers_are_refused(string name) =>
        Assert.IsType<ToolRequest.Invalid>(Parse("open_app", $$"""{"name":"{{name}}"}"""));

    [Theory]
    [InlineData("notepad")]
    [InlineData("Visual Studio Code")]
    [InlineData("chrome")]
    public void Normal_app_names_are_accepted(string name) =>
        Assert.IsType<ToolRequest.OpenApp>(Parse("open_app", $$"""{"name":"{{name}}"}"""));

    [Fact]
    public void Copy_card_shows_the_whole_text()
    {
        var text = new string('x', 500) + " rm -rf ~";
        var (_, detail) = Parse("copy_to_clipboard", $$"""{"text":"{{text}}"}""").Describe();
        Assert.EndsWith("rm -rf ~", detail);
    }
}
