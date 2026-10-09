using Pixel.Core.Focus;
using Pixel.Core.Sound;

namespace Pixel.Core.Tests.Polish;

public class FocusRuleTests
{
    private static readonly ScreenRect Monitor = new(0, 0, 1920, 1200);

    [Fact]
    public void A_window_covering_the_monitor_is_fullscreen() =>
        Assert.True(FocusRule.IsFullscreen(new ScreenRect(0, 0, 1920, 1200), Monitor, "Chrome_WidgetWin_1"));

    [Fact]
    public void A_maximised_window_with_a_visible_taskbar_is_not() =>
        Assert.False(FocusRule.IsFullscreen(new ScreenRect(0, 0, 1920, 1152), Monitor, "Chrome_WidgetWin_1"));

    [Theory]
    [InlineData("Progman")]
    [InlineData("WorkerW")]
    [InlineData("Shell_TrayWnd")]
    public void The_desktop_itself_is_not_a_fullscreen_app(string cls) =>
        Assert.False(FocusRule.IsFullscreen(new ScreenRect(0, 0, 1920, 1200), Monitor, cls));

    [Fact]
    public void A_window_bigger_than_the_monitor_counts() =>
        Assert.True(FocusRule.IsFullscreen(new ScreenRect(-8, -8, 1928, 1208), Monitor, "UnityWndClass"));
}

public class ChipSynthTests
{
    [Theory]
    [InlineData(SoundKind.Meow)]
    [InlineData(SoundKind.Purr)]
    [InlineData(SoundKind.Poof)]
    public void Produces_a_valid_wav(SoundKind kind)
    {
        var wav = ChipSynth.Render(kind, volume: 0.6);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(wav, 8, 4));
        Assert.Equal("data", System.Text.Encoding.ASCII.GetString(wav, 36, 4));
        Assert.Equal(wav.Length - 8, BitConverter.ToInt32(wav, 4));
        Assert.Equal(wav.Length - 44, BitConverter.ToInt32(wav, 40));
        Assert.True(wav.Length > 44 + 1000);
    }

    [Fact]
    public void Zero_volume_is_silence()
    {
        var wav = ChipSynth.Render(SoundKind.Meow, volume: 0);
        Assert.All(wav.Skip(44), b => Assert.Equal(0, b));
    }

    [Fact]
    public void Louder_volume_means_bigger_samples()
    {
        static int Peak(byte[] w) => Enumerable.Range(0, (w.Length - 44) / 2).Max(i => Math.Abs((int)BitConverter.ToInt16(w, 44 + i * 2)));
        Assert.True(Peak(ChipSynth.Render(SoundKind.Meow, 0.9)) > Peak(ChipSynth.Render(SoundKind.Meow, 0.3)));
    }
}
