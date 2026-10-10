using Pixel.Core.Input;

namespace Pixel.Core.Tests.Input;

public class ActivityMeterTests
{
    private static void TypeSteadily(ActivityMeter m, double from, double to, double keysPerSecond)
    {
        for (var t = from; t < to; t += 1.0 / keysPerSecond)
        {
            m.OnKeyDown(t);
            m.Update(t);
        }
    }

    [Fact]
    public void Ten_keys_per_second_for_three_seconds_overheats()
    {
        var m = new ActivityMeter();
        TypeSteadily(m, 0, 3.2, 10);
        Assert.True(m.IsOverheated);
    }

    [Fact]
    public void Fast_typing_for_two_seconds_is_not_yet_overheated()
    {
        var m = new ActivityMeter();
        TypeSteadily(m, 0, 2.0, 10);
        Assert.False(m.IsOverheated);
        Assert.True(m.IsTyping);
    }

    [Fact]
    public void Overheat_cools_after_four_seconds_of_slower_typing()
    {
        var m = new ActivityMeter();
        TypeSteadily(m, 0, 3.5, 10);
        TypeSteadily(m, 3.5, 7.0, 3);
        Assert.True(m.IsOverheated, "still hot before 4 s of cooling");
        TypeSteadily(m, 7.0, 9.0, 3);
        Assert.False(m.IsOverheated);
    }

    [Fact]
    public void Slower_than_one_key_per_second_is_not_typing()
    {
        var m = new ActivityMeter();
        m.OnKeyDown(0);
        m.Update(1.05);
        Assert.False(m.IsTyping);
    }

    [Fact]
    public void Scrolling_lasts_briefly_after_last_wheel_event()
    {
        var m = new ActivityMeter();
        m.OnScroll(1.0);
        m.Update(1.3);
        Assert.True(m.IsScrolling);
        m.Update(1.8);
        Assert.False(m.IsScrolling);
    }

    [Fact]
    public void Mouse_speed_reflects_recent_motion()
    {
        var m = new ActivityMeter();
        for (var i = 0; i <= 10; i++) m.OnMouse(i * 30, 0, i * 0.01); // 300 px in 0.1 s
        Assert.InRange(m.MouseSpeed, 2500, 3500);
        m.OnMouse(300, 0, 0.5);
        m.Update(0.5);
        Assert.True(m.MouseSpeed < 100);
    }
}

public class PettingDetectorTests
{
    [Fact]
    public void Three_strokes_within_window_start_purring()
    {
        var p = new PettingDetector();
        double[] xs = [0, 10, 0, 10, 0];
        for (var i = 0; i < xs.Length; i++) p.OnPointer(xs[i], i * 0.25);
        Assert.True(p.IsPurring(1.0));
    }

    [Fact]
    public void Two_strokes_are_not_enough()
    {
        var p = new PettingDetector();
        double[] xs = [0, 10, 0, 10];
        for (var i = 0; i < xs.Length; i++) p.OnPointer(xs[i], i * 0.25);
        Assert.False(p.IsPurring(0.75));
    }

    [Fact]
    public void Purring_stops_two_seconds_after_last_stroke()
    {
        var p = new PettingDetector();
        double[] xs = [0, 10, 0, 10, 0];
        for (var i = 0; i < xs.Length; i++) p.OnPointer(xs[i], i * 0.25);
        Assert.True(p.IsPurring(2.5));
        Assert.False(p.IsPurring(3.2));
    }

    [Fact]
    public void Tiny_jitter_is_not_a_stroke()
    {
        var p = new PettingDetector();
        double[] xs = [0, 1, 0, 1, 0, 1];
        for (var i = 0; i < xs.Length; i++) p.OnPointer(xs[i], i * 0.1);
        Assert.False(p.IsPurring(0.6));
    }
}

public class HotkeyDetectorTests
{
    private static KeyDecision Press(HotkeyDetector d, int vk) => d.OnKey(vk, down: true);
    private static KeyDecision Release(HotkeyDetector d, int vk) => d.OnKey(vk, down: false);

    [Fact]
    public void Ctrl_right_shift_delete_toggles_vanish_once_and_swallows()
    {
        var d = new HotkeyDetector();
        Press(d, VK.LControl);
        Press(d, VK.RShift);

        var down = Press(d, VK.Delete);
        var repeat = Press(d, VK.Delete);
        var up = Release(d, VK.Delete);

        Assert.Equal(HotkeyAction.ToggleVanish, down.Action);
        Assert.True(down.Swallow);
        Assert.Null(repeat.Action);
        Assert.True(repeat.Swallow);
        Assert.True(up.Swallow);
    }

    [Fact]
    public void Ctrl_left_shift_delete_passes_through()
    {
        var d = new HotkeyDetector();
        Press(d, VK.RControl);
        Press(d, VK.LShift);

        var down = Press(d, VK.Delete);

        Assert.Null(down.Action);
        Assert.False(down.Swallow);
    }

    [Fact]
    public void Ctrl_win_space_toggles_chat_and_masks_the_win_key()
    {
        var d = new HotkeyDetector();
        Press(d, VK.LWin);
        Press(d, VK.LControl);

        var down = Press(d, VK.Space);
        var up = Release(d, VK.Space);

        Assert.Equal(HotkeyAction.ToggleChat, down.Action);
        Assert.True(down.Swallow);
        Assert.True(down.InjectMask);
        Assert.True(up.Swallow);
    }

    [Fact]
    public void Plain_space_and_delete_pass_through()
    {
        var d = new HotkeyDetector();
        Assert.Equal(default, Press(d, VK.Space));
        Assert.Equal(default, Press(d, VK.Delete));
    }

    [Fact]
    public void Released_modifiers_no_longer_count()
    {
        var d = new HotkeyDetector();
        Press(d, VK.LControl);
        Press(d, VK.RShift);
        Release(d, VK.RShift);

        Assert.Null(Press(d, VK.Delete).Action);
    }

    [Fact]
    public void Resync_clears_modifiers_whose_key_up_was_missed()
    {
        var d = new HotkeyDetector();
        Press(d, VK.LControl);
        Press(d, VK.LWin); // key-up later lost to an elevated window
        d.ResyncModifiers(_ => false);

        Assert.Null(Press(d, VK.Space).Action);
    }
}

public class HotkeyDebounceTests
{
    private static KeyDecision Chord(HotkeyDetector d)
    {
        d.OnKey(VK.LControl, true);
        d.OnKey(VK.RShift, true);
        var down = d.OnKey(VK.Delete, true);
        d.OnKey(VK.Delete, false);
        d.OnKey(VK.RShift, false);
        d.OnKey(VK.LControl, false);
        return down;
    }

    [Fact]
    public void Vanish_presses_closer_than_a_second_are_swallowed_but_ignored()
    {
        var now = 0.0;
        var d = new HotkeyDetector(() => now);
        Assert.Equal(HotkeyAction.ToggleVanish, Chord(d).Action);

        now = 0.3;
        var flurry = Chord(d);
        Assert.Null(flurry.Action);
        Assert.True(flurry.Swallow, "still kept away from other apps");

        now = 1.5;
        Assert.Equal(HotkeyAction.ToggleVanish, Chord(d).Action);
    }
}
