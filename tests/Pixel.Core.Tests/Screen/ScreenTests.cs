using Pixel.Core.Screen;

namespace Pixel.Core.Tests.Screen;

public class ChangeDetectorTests
{
    private static byte[] Flat(byte v) => Enumerable.Repeat(v, ChangeDetector.ThumbWidth * ChangeDetector.ThumbHeight).ToArray();

    [Fact]
    public void First_frame_counts_as_changed() => Assert.True(ChangeDetector.HasChanged(null, Flat(10)));

    [Fact]
    public void Identical_frames_are_unchanged() => Assert.False(ChangeDetector.HasChanged(Flat(10), Flat(10)));

    [Fact]
    public void Tiny_noise_is_ignored()
    {
        var b = Flat(10);
        for (var i = 0; i < 40; i++) b[i * 50] = 15;
        Assert.False(ChangeDetector.HasChanged(Flat(10), b));
    }

    [Fact]
    public void A_real_change_is_detected()
    {
        var b = Flat(10);
        for (var i = 0; i < 30; i++) b[i] = 200;
        Assert.True(ChangeDetector.HasChanged(Flat(10), b));
    }

    [Fact]
    public void Thumbnail_averages_blocks_of_bgra_pixels()
    {
        const int w = ChangeDetector.ThumbWidth * 2, h = ChangeDetector.ThumbHeight * 2;
        var bgra = new byte[w * h * 4];
        for (var i = 0; i < w * h; i++) { bgra[i * 4] = 255; bgra[i * 4 + 1] = 255; bgra[i * 4 + 2] = 255; }
        var thumb = ChangeDetector.Thumbnail(bgra, w, h);
        Assert.Equal(ChangeDetector.ThumbWidth * ChangeDetector.ThumbHeight, thumb.Length);
        Assert.All(thumb, v => Assert.Equal(255, v));
    }
}

public class ErrorSpotterTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    private static ScreenSnapshot Fg(string text, string other = "") => new(T0, "Terminal", "WindowsTerminal", text, other);

    [Theory]
    [InlineData("Traceback (most recent call last):\n  File \"app.py\", line 3")]
    [InlineData("TypeError: Cannot read properties of undefined (reading 'name')")]
    [InlineData("System.NullReferenceException: Object reference not set")]
    [InlineData("npm ERR! code ENOENT")]
    [InlineData("Program.cs(12,5): error CS0103: The name 'x' does not exist")]
    [InlineData("error[E0382]: borrow of moved value: `v`")]
    [InlineData("fatal: not a git repository (or any of the parent directories): .git")]
    [InlineData("'pyhton' is not recognized as an internal or external command,")]
    [InlineData("bash: nodee: command not found")]
    [InlineData("FAILED tests/test_app.py::test_login - AssertionError")]
    [InlineData("Error: Cannot find module 'express'")]
    public void Spots_common_errors(string text)
    {
        Assert.NotNull(new ErrorSpotter().Scan(Fg(text), T0));
    }

    [Theory]
    [InlineData("This code is error-free and covers error handling gracefully.")]
    [InlineData("Build succeeded. 0 errors, 0 warnings")]
    [InlineData("No errors found.")]
    [InlineData("How to handle an Exception in Java (tutorial)")]
    public void Ignores_ordinary_text(string text)
    {
        Assert.Null(new ErrorSpotter().Scan(Fg(text), T0));
    }

    [Fact]
    public void Same_error_is_reported_once_per_two_minutes()
    {
        var s = new ErrorSpotter();
        var snap = Fg("npm ERR! code ENOENT");
        Assert.NotNull(s.Scan(snap, T0));
        Assert.Null(s.Scan(snap, T0.AddSeconds(90)));
        Assert.NotNull(s.Scan(snap, T0.AddMinutes(3)));
    }

    [Fact]
    public void Errors_outside_the_active_window_are_ignored()
    {
        Assert.Null(new ErrorSpotter().Scan(Fg("all good", other: "TypeError: boom"), T0));
    }

    [Fact]
    public void Finding_reports_the_matching_line()
    {
        var f = new ErrorSpotter().Scan(Fg("$ python app.py\nNameError: name 'foo' is not defined\n$"), T0);
        Assert.Equal("NameError: name 'foo' is not defined", f!.Line);
    }
}
