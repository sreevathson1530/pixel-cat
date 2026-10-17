using Pixel.Core.Focus;
using Pixel.Core.Sound;

namespace Pixel.Core.Tests.Polish;

public class FocusRuleTests
{
    private static readonly ScreenRect Monitor = new(0, 0, 1920, 1200);

    [Fact]
    public void A_window_covering_the_monitor_is_fullscreen() =>
        Assert.True(FocusRule.IsFullscreen(new ScreenRect(0, 0, 1920, 1200), Monitor, "Chrome_WidgetWin_1", maximized: false, hasCaption: false));

    [Fact]
    public void A_maximised_window_with_a_visible_taskbar_is_not() =>
        Assert.False(FocusRule.IsFullscreen(new ScreenRect(0, 0, 1920, 1152), Monitor, "Chrome_WidgetWin_1", maximized: true, hasCaption: true));

    [Theory]
    [InlineData("Progman")]
    [InlineData("WorkerW")]
    [InlineData("Shell_TrayWnd")]
    public void The_desktop_itself_is_not_a_fullscreen_app(string cls) =>
        Assert.False(FocusRule.IsFullscreen(new ScreenRect(0, 0, 1920, 1200), Monitor, cls, maximized: false, hasCaption: false));

    [Fact]
    public void A_window_bigger_than_the_monitor_counts() =>
        Assert.True(FocusRule.IsFullscreen(new ScreenRect(-8, -8, 1928, 1208), Monitor, "UnityWndClass", maximized: false, hasCaption: false));
}

public class FocusRuleAutoHideTaskbarTests
{
    private static readonly ScreenRect Monitor = new(0, 0, 1920, 1200);

    [Fact]
    public void A_maximised_window_with_an_auto_hidden_taskbar_is_not_fullscreen() =>
        // Covers the whole monitor, but it is just a maximised app (title bar, maximise state).
        Assert.False(FocusRule.IsFullscreen(new ScreenRect(0, 0, 1920, 1200), Monitor, "CASCADIA_HOSTING_WINDOW_CLASS", maximized: true, hasCaption: true));

    [Fact]
    public void A_borderless_window_covering_the_monitor_is_fullscreen() =>
        Assert.True(FocusRule.IsFullscreen(new ScreenRect(0, 0, 1920, 1200), Monitor, "MediaPlayerClass", maximized: false, hasCaption: false));
}

public class ChipSynthTests
{
    [Theory]
    [InlineData(SoundKind.Meow)]
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

    private static short[] Samples(byte[] wav) =>
        Enumerable.Range(0, (wav.Length - 44) / 2).Select(i => BitConverter.ToInt16(wav, 44 + i * 2)).ToArray();

    private static double Rms(short[] s, double from, double to)
    {
        var a = (int)(s.Length * from);
        var b = Math.Max(a + 1, (int)(s.Length * to));
        return Math.Sqrt(s[a..b].Average(v => (double)v * v));
    }

    [Fact]
    public void Meow_is_a_long_drawn_out_meowww()
    {
        var s = Samples(ChipSynth.Render(SoundKind.Meow, 0.8));
        Assert.InRange(s.Length / 22050.0, 0.75, 1.2);
    }

    [Fact]
    public void Meow_starts_softly_on_the_m_opens_up_then_fades_without_a_click()
    {
        var s = Samples(ChipSynth.Render(SoundKind.Meow, 0.8));
        var m = Rms(s, 0.02, 0.07);      // lips still closed
        var open = Rms(s, 0.2, 0.45);    // "ee-ah"
        var tail = Rms(s, 0.97, 1.0);
        Assert.True(open > m * 1.5, $"opens up: m={m:0} open={open:0}");
        Assert.True(tail < open * 0.15, $"fades out: tail={tail:0}");
        Assert.True(Math.Abs((int)s[0]) < 200 && Math.Abs((int)s[^1]) < 200, "no click at either end");
    }

    [Fact]
    public void Meow_never_clips()
    {
        var s = Samples(ChipSynth.Render(SoundKind.Meow, 1.0));
        Assert.True(s.Max(v => Math.Abs((int)v)) < short.MaxValue - 100);
    }
}

public class CatSoundsTests
{
    [Fact]
    public void Petting_makes_it_meow_not_purr() =>
        Assert.Equal(SoundKind.Meow, CatSounds.For(Pixel.Core.Behaviour.Activity.Purr));

    [Fact]
    public void There_is_no_purr_sound_any_more() =>
        Assert.DoesNotContain("Purr", Enum.GetNames<SoundKind>());

    [Theory]
    [InlineData(Pixel.Core.Behaviour.Activity.Alert, SoundKind.Meow)]
    [InlineData(Pixel.Core.Behaviour.Activity.AgentNeedsYou, SoundKind.Meow)]
    [InlineData(Pixel.Core.Behaviour.Activity.PoofOut, SoundKind.Poof)]
    [InlineData(Pixel.Core.Behaviour.Activity.PoofIn, SoundKind.Poof)]
    public void Other_moments_keep_their_sounds(Pixel.Core.Behaviour.Activity a, SoundKind expected) =>
        Assert.Equal(expected, CatSounds.For(a));

    [Fact]
    public void Roaming_is_quiet() => Assert.Null(CatSounds.For(Pixel.Core.Behaviour.Activity.Roam));
}
