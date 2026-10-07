using System.Net;
using Pixel.Core.Tools;

namespace Pixel.Core.Tests.Tools;

public class ToolSafetyRound2Tests
{
    [Theory]
    [InlineData(@"C:\Users\me\report.docm")]
    [InlineData(@"C:\Users\me\data.iqy")]
    [InlineData(@"C:\Users\me\disk.iso")]
    [InlineData(@"C:\Users\me\notes.one")]
    [InlineData(@"C:\Users\me\script.py")]
    [InlineData(@"C:\Users\me\tool.ps1xml")]
    [InlineData(@"C:\Users\me\archive.zip")]
    public void Only_known_safe_document_types_open(string path) =>
        Assert.NotNull(LaunchSafety.CheckFile(path));

    [Theory]
    [InlineData(@"C:\Users\me\notes.txt")]
    [InlineData(@"C:\Users\me\paper.pdf")]
    [InlineData(@"C:\Users\me\photo.JPG")]
    [InlineData(@"C:\Users\me\budget.xlsx")]
    [InlineData(@"C:\Users\me\Projects")]
    public void Documents_media_and_folders_are_fine(string path) =>
        Assert.Null(LaunchSafety.CheckFile(path));

    [Theory]
    [InlineData("http://localtest.me/")]
    [InlineData("http://127.0.0.1.nip.io/")]
    public void Names_that_resolve_to_this_pc_are_refused(string url)
    {
        IPAddress[] Resolve(string host) => [IPAddress.Loopback];
        Assert.False(UrlSafety.TryCheck(url, out _, out _, Resolve));
    }

    [Fact]
    public void Public_names_pass_the_resolver_check()
    {
        IPAddress[] Resolve(string host) => [IPAddress.Parse("151.101.1.69")];
        Assert.True(UrlSafety.TryCheck("https://pypi.org/", out _, out _, Resolve));
    }

    [Fact]
    public void Unresolvable_names_are_left_to_the_browser()
    {
        IPAddress[] Resolve(string host) => [];
        Assert.True(UrlSafety.TryCheck("https://example.org/", out _, out _, Resolve));
    }

    [Theory]
    [InlineData("http://2130706433/")]
    [InlineData("http://0x7f.1/")]
    [InlineData("http://017700000001/")]
    [InlineData("http://[::ffff:127.0.0.1]/")]
    [InlineData("http://0.0.0.0/")]
    [InlineData("http://localhost./")]
    public void Odd_spellings_of_local_addresses_are_refused(string url) =>
        Assert.False(UrlSafety.TryCheck(url, out _, out _));

    [Theory]
    [InlineData("cmd.exe")]
    [InlineData("C:\\Windows\\System32\\WindowsPowerShell\\v1.0\\powershell.exe")]
    [InlineData("MSHTA.EXE")]
    public void Shortcut_targets_that_are_shells_are_blocked(string target) =>
        Assert.True(LaunchSafety.IsBlockedTarget(target));

    [Fact]
    public void Ordinary_shortcut_targets_are_allowed() =>
        Assert.False(LaunchSafety.IsBlockedTarget(@"C:\Program Files\Google\Chrome\Application\chrome.exe"));
}
