using Pixel.Core.Art;
using Pixel.Core.Behaviour;
using Pixel.Core.Motion;

namespace Pixel.Core.Tests.Motion;

public class ThrowTrackerTests
{
    private const double Tick = 1 / 30.0;

    private static ThrowTracker Drag(double vx, double vy, int ticks = 10, double x = 500, double y = 400)
    {
        var t = new ThrowTracker();
        for (var i = 0; i < ticks; i++) t.Add(i * Tick, x + vx * i * Tick, y + vy * i * Tick);
        return t;
    }

    [Fact]
    public void A_fast_fling_is_a_throw_with_its_speed_and_direction()
    {
        var v = Drag(1200, -600).Release(9 * Tick);
        Assert.NotNull(v);
        Assert.InRange(v.Value.Vx, 1100, 1300);
        Assert.InRange(v.Value.Vy, -700, -500);
    }

    [Fact]
    public void A_slow_release_is_a_gentle_drop() => Assert.Null(Drag(150, 50).Release(9 * Tick));

    [Fact]
    public void Stopping_before_letting_go_is_a_drop_not_a_throw()
    {
        var t = Drag(2000, 0);
        for (var i = 10; i < 16; i++) t.Add(i * Tick, 500 + 2000 * 9 * Tick, 400); // held still for 0.2 s
        Assert.Null(t.Release(15 * Tick));
    }

    [Fact]
    public void A_wild_fling_is_capped()
    {
        var v = Drag(20_000, 0).Release(9 * Tick)!.Value;
        Assert.InRange(Math.Sqrt(v.Vx * v.Vx + v.Vy * v.Vy), ThrowTracker.MaxThrowSpeed - 1, ThrowTracker.MaxThrowSpeed + 1);
    }

    [Fact]
    public void A_click_without_movement_is_never_a_throw()
    {
        var t = new ThrowTracker();
        t.Add(0, 100, 100);
        Assert.Null(t.Release(0));
    }
}

public class ThrowFlightTests
{
    private const double Dt = 1 / 30.0;
    private static readonly Bounds Area = new(0, 0, 1280, 800);

    private static MotionController Thrown(double vx, double vy, double x = 640, double y = 500, bool freeRoam = true)
    {
        var m = new MotionController(Area) { FreeRoam = freeRoam, BodyHeight = 120 };
        m.Hold(x, y);
        m.Throw(vx, vy);
        return m;
    }

    private static List<(double X, double Y, double Vx, double Vy, int Bumps)> Fly(MotionController m, double seconds, IReadOnlyList<Platform>? platforms = null)
    {
        var trace = new List<(double, double, double, double, int)>();
        for (var t = 0.0; t < seconds; t += Dt)
        {
            m.Step(Dt, platforms ?? []);
            trace.Add((m.Body.X, m.Body.Y, m.Body.Vx, m.Body.Vy, m.BumpCount));
        }
        return trace;
    }

    [Fact]
    public void Thrown_at_the_right_wall_it_bounces_back_slower()
    {
        var m = Thrown(2400, -300);
        var trace = Fly(m, 1.0);
        var hit = trace.FindIndex(s => s.Bumps > 0);
        Assert.True(hit > 0, "hit the wall");
        Assert.Equal(Bump.Wall, m.BumpsSeen[0]);
        Assert.True(trace[hit].Vx < 0, "now flying left");
        Assert.InRange(-trace[hit].Vx, 2400 * MotionController.WallBounce * 0.9, 2400 * MotionController.WallBounce * 1.01);
    }

    [Fact]
    public void It_turns_round_when_it_bounces_off_a_wall()
    {
        var m = Thrown(2400, -300);
        Assert.Equal(Facing.Right, m.Body.Facing);
        while (m.BumpCount == 0) m.Step(Dt, []);
        Assert.Equal(Facing.Left, m.Body.Facing);
    }

    [Fact]
    public void It_never_leaves_the_screen()
    {
        var m = Thrown(-3000, -2500, x: 200, y: 300);
        foreach (var s in Fly(m, 6))
        {
            Assert.InRange(s.X, Area.Left + MotionController.HalfWidth, Area.Right - MotionController.HalfWidth);
            Assert.InRange(s.Y, Area.Top + m.BodyHeight, Area.Bottom);
        }
    }

    [Fact]
    public void Hitting_the_top_sends_it_back_down()
    {
        var m = Thrown(0, -3000, y: 300);
        var trace = Fly(m, 0.5);
        Assert.Contains(Bump.Ceiling, m.BumpsSeen);
        var hit = trace.FindIndex(s => s.Bumps > 0);
        Assert.True(trace[hit].Vy > 0, "falling after the bonk");
    }

    [Fact]
    public void Floor_bounces_shrink_and_it_settles_on_the_floor()
    {
        var m = Thrown(300, 0, y: 200);
        var trace = Fly(m, 6);

        var floorBumps = m.BumpsSeen.Count(b => b == Bump.Floor);
        Assert.InRange(floorBumps, 1, 4);
        Assert.False(m.Flying);
        Assert.True(m.Body.Grounded);
        Assert.Equal(Area.Bottom, m.Body.Y, 3);

        // Peak height after each floor bounce gets smaller.
        var apexes = new List<double>();
        for (var i = 1; i < trace.Count - 1; i++)
            if (trace[i].Vy < 0 && trace[i + 1].Vy >= 0) apexes.Add(trace[i].Y);
        for (var i = 1; i < apexes.Count; i++) Assert.True(apexes[i] > apexes[i - 1], "each hop is lower than the one before");
    }

    [Fact]
    public void A_throw_always_settles_within_a_few_seconds()
    {
        var m = Thrown(3000, -2000, x: 100, y: 700);
        Fly(m, MotionController.MaxFlightSeconds + 0.5);
        Assert.False(m.Flying);
        Assert.True(m.Body.Grounded);
    }

    [Fact]
    public void Catching_it_mid_air_ends_the_flight()
    {
        var m = Thrown(1500, -1500);
        Fly(m, 0.2);
        m.Hold(400, 400);
        Assert.False(m.Flying);
        Assert.Equal(MotionMode.Held, m.Mode);
    }

    [Fact]
    public void Walk_orders_are_ignored_mid_air()
    {
        var m = Thrown(800, -800);
        m.WalkToPoint(100, 700);
        m.Pounce(100, 700);
        Assert.True(m.Flying);
        Assert.Equal(MotionMode.Airborne, m.Mode);
    }

    [Fact]
    public void In_floor_mode_a_throw_can_land_on_a_window_top()
    {
        var m = Thrown(0, 0, x: 640, y: 300, freeRoam: false);
        var window = new Platform(500, 800, 450, Owner: 42);
        Fly(m, 4, [window]);
        Assert.False(m.Flying);
        Assert.Equal(450, m.Body.Y, 3);
        Assert.Equal(42, m.Body.StandingOn?.Owner);
    }

    [Fact]
    public void A_gentle_release_in_free_roam_still_stays_put()
    {
        var m = new MotionController(Area) { FreeRoam = true, BodyHeight = 120 };
        m.Hold(640, 300);
        m.Release();
        m.Step(Dt, []);
        Assert.Equal(300, m.Body.Y, 3);
        Assert.False(m.Flying);
    }
}

public class ThrowReactionTests
{
    private const double Dt = 1 / 30.0;
    private static readonly Bounds Area = new(0, 0, 1280, 800);

    [Fact]
    public void Hitting_a_wall_shows_a_bonk_then_landing_shows_a_daze_then_it_carries_on()
    {
        var m = new MotionController(Area) { FreeRoam = true, BodyHeight = 120 };
        m.Place(1100, 600);
        var d = new PetDirector(m, new Roamer(new Random(5))) { Staying = true };
        var now = 0.0;
        m.Hold(1100, 600);
        m.Throw(2800, -200);

        var seen = new List<AnimationId>();
        for (; now < 6; now += Dt)
        {
            d.Tick(now, Dt, new PetSignals(), [], 0);
            if (seen.Count == 0 || seen[^1] != d.Animation) seen.Add(d.Animation);
        }

        var bonk = seen.IndexOf(AnimationId.Bonk);
        var dazed = seen.IndexOf(AnimationId.Dazed);
        Assert.True(bonk >= 0, $"bonk shown ({string.Join(",", seen)})");
        Assert.True(dazed > bonk, $"dazed after landing ({string.Join(",", seen)})");
        Assert.NotEqual(AnimationId.Dazed, d.Animation); // shook it off
    }

    [Fact]
    public void Bonk_and_daze_frames_render_with_stars()
    {
        var p = Palette.Named("black");
        foreach (var id in new[] { AnimationId.Bonk, AnimationId.Dazed })
        {
            var a = Animations.Get(id);
            Assert.False(a.Loop);
            Assert.Contains(a.Frames, f => f.Pose.Effect.HasFlag(Effect.Stars));
            foreach (var f in a.Frames.Where(f => f.Pose.Effect.HasFlag(Effect.Stars)))
                Assert.Contains(0xFFFFD23Fu, CatRenderer.Render(f.Pose, p)); // yellow stars
        }
    }

    [Fact]
    public void Dazed_stars_circle_round()
    {
        var p = Palette.Named("black");
        var frames = Animations.Get(AnimationId.Dazed).Frames.Where(f => f.Pose.Effect.HasFlag(Effect.Stars))
            .Select(f => CatRenderer.Render(f.Pose, p)).ToList();
        Assert.True(frames.Count >= 3);
        Assert.NotEqual(frames[0], frames[1]);
    }
}
