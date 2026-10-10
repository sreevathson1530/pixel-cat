using Pixel.Core.Art;
using Pixel.Core.Behaviour;
using Pixel.Core.Motion;

namespace Pixel.Core.Tests.Behaviour;

public class BehaviourSelectorTests
{
    private static readonly PetSignals Calm = new();

    public static TheoryData<PetSignals, Activity> Priorities => new()
    {
        { Calm, Activity.Roam },
        { Calm with { Scrolling = true }, Activity.Paper },
        { Calm with { Scrolling = true, Typing = true }, Activity.Knead },
        { Calm with { Typing = true, Purring = true }, Activity.Purr },
        { Calm with { Purring = true, Hunting = true }, Activity.Hunt },
        { Calm with { Hunting = true, Overheated = true }, Activity.Overheat },
        { Calm with { Overheated = true, Alert = true }, Activity.Alert },
        { Calm with { Alert = true, Agent = AgentState.Working }, Activity.AgentWorking },
        { Calm with { Agent = AgentState.Done }, Activity.AgentDone },
        { Calm with { Agent = AgentState.NeedsYou }, Activity.AgentNeedsYou },
        { Calm with { Agent = AgentState.Working, FocusMode = true }, Activity.Peek },
        { Calm with { FocusMode = true, Chat = ChatState.Thinking }, Activity.Thinking },
        { Calm with { Chat = ChatState.Talking }, Activity.Talk },
        { Calm with { Chat = ChatState.Talking, Poof = PoofState.Out }, Activity.PoofOut },
        { Calm with { Poof = PoofState.In }, Activity.PoofIn },
        { Calm with { Poof = PoofState.Out, Held = true }, Activity.Dangle },
    };

    [Theory]
    [MemberData(nameof(Priorities))]
    public void Highest_priority_signal_wins(PetSignals signals, Activity expected)
    {
        Assert.Equal(expected, BehaviourSelector.Select(signals));
    }

    [Fact]
    public void Hunt_needs_fast_mouse_near_the_cat()
    {
        Assert.True(HuntRule.ShouldHunt(mouseSpeed: 3000, distanceToCat: 200));
        Assert.False(HuntRule.ShouldHunt(mouseSpeed: 3000, distanceToCat: 400));
        Assert.False(HuntRule.ShouldHunt(mouseSpeed: 1500, distanceToCat: 100));
    }
}

public class RoamerTests
{
    private static readonly Bounds Area = new(0, 0, 1280, 800);

    private static RoamContext Ctx(bool staying = false, double idle = 0, RoamAction? last = null,
        IReadOnlyList<Platform>? platforms = null) =>
        new(X: 640, Y: 800, Area: Area, Platforms: platforms ?? [], UserIdleSeconds: idle, Staying: staying, Last: last);

    [Fact]
    public void Staying_cat_never_walks_or_jumps()
    {
        var r = new Roamer(new Random(1));
        var shelf = new Platform(500, 900, 600, Owner: 1);
        for (var i = 0; i < 500; i++)
        {
            var a = r.Next(Ctx(staying: true, platforms: [shelf])).Action;
            Assert.DoesNotContain(a, new[] { RoamAction.Walk, RoamAction.JumpUp });
        }
    }

    [Fact]
    public void Long_user_idle_puts_the_cat_to_sleep()
    {
        var r = new Roamer(new Random(2));
        Assert.Equal(RoamAction.Sleep, r.Next(Ctx(idle: 300)).Action);
    }

    [Fact]
    public void Waking_up_starts_with_a_stretch()
    {
        var r = new Roamer(new Random(3));
        Assert.Equal(RoamAction.Stretch, r.Next(Ctx(last: RoamAction.Sleep)).Action);
    }

    [Fact]
    public void Walk_targets_stay_inside_the_work_area()
    {
        var r = new Roamer(new Random(4));
        for (var i = 0; i < 500; i++)
        {
            var intent = r.Next(Ctx());
            if (intent.Action != RoamAction.Walk) continue;
            Assert.InRange(intent.TargetX!.Value, Area.Left + MotionController.HalfWidth, Area.Right - MotionController.HalfWidth);
        }
    }

    [Fact]
    public void Every_intent_has_a_positive_duration()
    {
        var r = new Roamer(new Random(5));
        for (var i = 0; i < 200; i++) Assert.True(r.Next(Ctx()).Seconds > 0);
    }
}

public class PetDirectorTests
{
    private static readonly Bounds Area = new(0, 0, 1280, 800);

    private static (PetDirector D, MotionController M) Make()
    {
        var m = new MotionController(Area);
        m.Place(640, 800);
        return (new PetDirector(m, new Roamer(new Random(7))), m);
    }

    private static void Tick(PetDirector d, ref double now, PetSignals s, double seconds = 1 / 30.0)
    {
        for (var end = now + seconds; now < end; now += 1 / 30.0)
            d.Tick(now, 1 / 30.0, s, [], userIdleSeconds: 0);
    }

    [Fact]
    public void Held_cat_dangles()
    {
        var (d, m) = Make();
        var now = 0.0;
        m.Hold(300, 300);
        Tick(d, ref now, new PetSignals { Held = true });
        Assert.Equal(AnimationId.Dangle, d.Animation);
    }

    [Fact]
    public void Airborne_cat_shows_fall_animation()
    {
        var (d, m) = Make();
        var now = 0.0;
        m.Place(640, 200);
        Tick(d, ref now, new PetSignals());
        Assert.Equal(AnimationId.Fall, d.Animation);
    }

    [Fact]
    public void Typing_makes_the_cat_stop_and_knead()
    {
        var (d, m) = Make();
        var now = 0.0;
        m.WalkTo(100);
        Tick(d, ref now, new PetSignals { Typing = true }, 0.5);
        Assert.Equal(AnimationId.Knead, d.Animation);
        Assert.Equal(MotionMode.Idle, m.Mode);
    }

    [Fact]
    public void Overheat_uses_the_overheat_animation_and_flag()
    {
        var (d, _) = Make();
        var now = 0.0;
        Tick(d, ref now, new PetSignals { Typing = true, Overheated = true });
        Assert.Equal(AnimationId.Overheat, d.Animation);
        Assert.True(d.Overheated);
    }

    [Fact]
    public void Staying_cat_does_not_move_while_roaming()
    {
        var (d, m) = Make();
        var now = 0.0;
        d.Staying = true;
        var x = m.Body.X;
        Tick(d, ref now, new PetSignals(), 120);
        Assert.Equal(x, m.Body.X, 3);
    }

    [Fact]
    public void Hunt_crouches_then_pounces_toward_the_cursor()
    {
        var (d, m) = Make();
        var now = 0.0;
        d.CursorX = 900;
        Tick(d, ref now, new PetSignals { Hunting = true });
        Assert.Equal(AnimationId.Crouch, d.Animation);

        Tick(d, ref now, new PetSignals(), 0.8);
        Assert.True(m.Body.X > 640, "cat leapt toward the cursor");
    }

    [Fact]
    public void Animation_start_resets_when_animation_changes()
    {
        var (d, _) = Make();
        var now = 0.0;
        Tick(d, ref now, new PetSignals { Typing = true }, 0.5);
        Tick(d, ref now, new PetSignals { Alert = true });
        Assert.Equal(AnimationId.Alert, d.Animation);
        Assert.InRange(now - d.AnimationStart, 0, 0.1);
    }
}

public class PetDirectorAttentionTests
{
    [Fact]
    public void Alert_with_attention_point_walks_there_first_then_alerts()
    {
        var m = new MotionController(new Bounds(0, 0, 1280, 800));
        m.Place(200, 800);
        var d = new PetDirector(m, new Roamer(new Random(1))) { AttentionX = 900 };
        var now = 0.0;
        var alert = new PetSignals { Alert = true };

        d.Tick(now, 1 / 30.0, alert, [], 0);
        Assert.Equal(AnimationId.Run, d.Animation);

        for (; now < 6; now += 1 / 30.0) d.Tick(now, 1 / 30.0, alert, [], 0);
        Assert.InRange(m.Body.X, 860, 940);
        Assert.Equal(AnimationId.Alert, d.Animation);
    }
}

public class FreeRoamBehaviourTests
{
    private static readonly Bounds Area = new(0, 0, 1280, 800);

    [Fact]
    public void Walks_go_everywhere_on_screen()
    {
        var r = new Roamer(new Random(11));
        var ctx = new RoamContext(640, 700, Area, [], 0, Staying: false, Last: null, FreeRoam: true);
        var targets = Enumerable.Range(0, 400).Select(_ => r.Next(ctx))
            .Where(i => i.Action == RoamAction.Walk).ToList();

        Assert.All(targets, t => Assert.NotNull(t.TargetY));
        Assert.Contains(targets, t => t.TargetY < 300);                       // upper part
        Assert.Contains(targets, t => t.TargetY is > 300 and < 550);          // middle
        Assert.Contains(targets, t => t.TargetX < 320);                       // left side
        Assert.Contains(targets, t => t.TargetX > 960);                       // right side
        Assert.All(targets, t => Assert.InRange(t.TargetY!.Value, Area.Top, Area.Bottom));
        Assert.DoesNotContain(r.Next(ctx with { Last = RoamAction.Sit }).Action, new[] { RoamAction.JumpUp });
    }

    [Fact]
    public void Hunt_dashes_toward_the_cursor_anywhere()
    {
        var m = new MotionController(Area) { FreeRoam = true, BodyHeight = 120 };
        m.Place(300, 700);
        var d = new PetDirector(m, new Roamer(new Random(3))) { CursorX = 700, CursorY = 300 };
        var now = 0.0;
        d.Tick(now, 1 / 30.0, new PetSignals { Hunting = true }, [], 0);
        Assert.Equal(AnimationId.Crouch, d.Animation);
        for (; now < 2; now += 1 / 30.0) d.Tick(now, 1 / 30.0, new PetSignals(), [], 0);
        Assert.True(m.Body.X > 300 && m.Body.Y < 700, $"dashed toward the cursor, at ({m.Body.X:0},{m.Body.Y:0})");
    }

    [Fact]
    public void Runs_to_an_error_anywhere_on_screen()
    {
        var m = new MotionController(Area) { FreeRoam = true, BodyHeight = 120 };
        m.Place(200, 700);
        var d = new PetDirector(m, new Roamer(new Random(4))) { AttentionX = 900, AttentionY = 260 };
        var now = 0.0;
        for (; now < 6; now += 1 / 30.0) d.Tick(now, 1 / 30.0, new PetSignals { Alert = true }, [], 0);
        Assert.InRange(m.Body.X, 860, 940);
        Assert.InRange(m.Body.Y, 220, 300);
        Assert.Equal(AnimationId.Alert, d.Animation);
    }
}
