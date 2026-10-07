using Pixel.Core.Motion;

namespace Pixel.Core.Tests.Motion;

public class MotionControllerTests
{
    private static readonly Bounds Area = new(0, 0, 1280, 800);
    private static readonly IReadOnlyList<Platform> None = [];

    private static void Run(MotionController m, IReadOnlyList<Platform> platforms, double seconds)
    {
        for (var t = 0.0; t < seconds; t += 1 / 60.0) m.Step(1 / 60.0, platforms);
    }

    [Fact]
    public void Cat_placed_in_the_air_lands_on_the_ground()
    {
        var m = new MotionController(Area);
        m.Place(400, 100);
        Run(m, None, 2);
        Assert.True(m.Body.Grounded);
        Assert.Equal(800, m.Body.Y, 3);
    }

    [Fact]
    public void Removed_platform_drops_the_cat_to_the_ground()
    {
        var m = new MotionController(Area);
        var window = new Platform(300, 700, 400, Owner: 42);
        m.Place(500, 400);
        Run(m, [window], 0.2);
        Assert.Equal(400, m.Body.Y, 3);

        Run(m, None, 2);
        Assert.Equal(800, m.Body.Y, 3);
        Assert.True(m.Body.Grounded);
    }

    [Fact]
    public void Cat_rides_a_moved_window()
    {
        var m = new MotionController(Area);
        m.Place(500, 400);
        Run(m, [new Platform(300, 700, 400, Owner: 42)], 0.2);

        Run(m, [new Platform(350, 750, 380, Owner: 42)], 0.1);

        Assert.Equal(550, m.Body.X, 3);
        Assert.Equal(380, m.Body.Y, 3);
    }

    [Fact]
    public void Walking_never_leaves_the_work_area()
    {
        var m = new MotionController(Area);
        m.Place(100, 800);
        m.WalkTo(-1000);
        Run(m, None, 10);
        Assert.True(m.Body.X >= Area.Left + MotionController.HalfWidth);
        Assert.Equal(MotionMode.Idle, m.Mode);
    }

    [Fact]
    public void Walking_off_a_platform_edge_falls()
    {
        var m = new MotionController(Area);
        var shelf = new Platform(300, 500, 500, Owner: 7);
        m.Place(450, 500);
        Run(m, [shelf], 0.1);
        Assert.Equal(500, m.Body.Y, 3);

        m.WalkTo(700);
        Run(m, [shelf], 6);
        Assert.Equal(800, m.Body.Y, 3);
        Assert.True(m.Body.Grounded);
    }

    [Fact]
    public void Walk_request_while_airborne_is_ignored()
    {
        var m = new MotionController(Area);
        m.Place(450, 300);
        m.WalkTo(900);
        Assert.Equal(MotionMode.Airborne, m.Mode);
        Assert.Null(m.TargetX);
    }

    [Fact]
    public void Jump_lands_on_target_platform()
    {
        var m = new MotionController(Area);
        var shelf = new Platform(600, 900, 550, Owner: 9);
        m.Place(400, 800);
        Assert.True(m.JumpTo(shelf, 700));
        Run(m, [shelf], 3);
        Assert.True(m.Body.Grounded);
        Assert.Equal(550, m.Body.Y, 3);
        Assert.InRange(m.Body.X, 690, 710);
    }

    [Fact]
    public void Jump_too_high_is_refused()
    {
        var m = new MotionController(Area);
        m.Place(400, 800);
        Assert.False(m.JumpTo(new Platform(300, 600, 100, Owner: 3), 450));
    }

    [Fact]
    public void Held_cat_ignores_gravity_and_falls_after_release()
    {
        var m = new MotionController(Area);
        m.Place(400, 800);
        m.Hold(600, 300);
        Run(m, None, 1);
        Assert.Equal(MotionMode.Held, m.Mode);
        Assert.Equal(300, m.Body.Y, 3);

        m.Release();
        Run(m, None, 2);
        Assert.True(m.Body.Grounded);
        Assert.Equal(800, m.Body.Y, 3);
    }

    [Fact]
    public void Shrinking_work_area_pulls_cat_back_inside()
    {
        var m = new MotionController(Area);
        m.Place(1200, 800);
        m.SetArea(new Bounds(0, 0, 1000, 700));
        Run(m, None, 2);
        Assert.True(m.Body.X <= 1000 - MotionController.HalfWidth);
        Assert.Equal(700, m.Body.Y, 3);
    }
}
