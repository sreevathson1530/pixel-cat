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

public class FreeRoamMotionTests
{
    private static readonly Bounds Area = new(0, 0, 1280, 800);

    private static MotionController Free(double bodyHeight = 120) =>
        new(Area) { FreeRoam = true, BodyHeight = bodyHeight };

    private static void Run(MotionController m, double seconds)
    {
        for (var t = 0.0; t < seconds; t += 1 / 60.0) m.Step(1 / 60.0, []);
    }

    [Fact]
    public void Cat_can_rest_in_the_middle_of_the_screen()
    {
        var m = Free();
        m.Place(640, 400);
        Run(m, 2);
        Assert.Equal(400, m.Body.Y, 3);
        Assert.True(m.Body.Grounded);
    }

    [Fact]
    public void Walks_diagonally_to_any_point()
    {
        var m = Free();
        m.Place(200, 700);
        m.WalkToPoint(900, 250);
        Assert.Equal(Facing.Right, m.Body.Facing);
        Run(m, 20);
        Assert.Equal(900, m.Body.X, 1);
        Assert.Equal(250, m.Body.Y, 1);
        Assert.Equal(MotionMode.Idle, m.Mode);
    }

    [Fact]
    public void Faces_left_when_walking_left()
    {
        var m = Free();
        m.Place(900, 500);
        m.WalkToPoint(300, 200);
        Run(m, 0.2);
        Assert.Equal(Facing.Left, m.Body.Facing);
    }

    [Fact]
    public void Never_leaves_the_screen_even_at_the_top()
    {
        var m = Free(bodyHeight: 120);
        m.Place(640, 400);
        m.WalkToPoint(-500, -500);
        Run(m, 30);
        Assert.True(m.Body.X >= Area.Left + MotionController.HalfWidth);
        Assert.True(m.Body.Y >= Area.Top + 120, "whole cat stays visible");
    }

    [Fact]
    public void Dropped_cat_stays_where_it_was_dropped()
    {
        var m = Free();
        m.Place(640, 700);
        m.Hold(300, 350);
        m.Release();
        Run(m, 2);
        Assert.Equal(300, m.Body.X, 3);
        Assert.Equal(350, m.Body.Y, 3);
        Assert.Equal(MotionMode.Idle, m.Mode);
    }

    [Fact]
    public void Pounce_dashes_toward_a_point_within_reach()
    {
        var m = Free();
        m.Place(200, 600);
        m.Pounce(1200, 100);
        Run(m, 3);
        var moved = Math.Sqrt(Math.Pow(m.Body.X - 200, 2) + Math.Pow(m.Body.Y - 600, 2));
        Assert.InRange(moved, 150, MotionController.FreePounceReach + 1);
        Assert.True(m.Body.X > 200 && m.Body.Y < 600, "moved toward the target");
    }
}
