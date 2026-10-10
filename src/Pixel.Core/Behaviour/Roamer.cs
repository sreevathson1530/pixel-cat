using Pixel.Core.Motion;

namespace Pixel.Core.Behaviour;

public enum RoamAction { Sit, Walk, Groom, Sleep, Stretch, JumpUp }

public sealed record RoamIntent(RoamAction Action, double Seconds, double? TargetX = null, Platform? Target = null, double? TargetY = null);

public sealed record RoamContext(
    double X,
    double Y,
    Bounds Area,
    IReadOnlyList<Platform> Platforms,
    double UserIdleSeconds,
    bool Staying,
    RoamAction? Last,
    bool FreeRoam = false);

/// <summary>Decides what the cat does when nothing needs its attention.</summary>
public sealed class Roamer(Random rng)
{
    public const double SleepAfterIdle = 120;

    /// <summary>Steepest up/down slope of a free-roaming walk (dy/dx). The walk is drawn side-on, so steep
    /// glides read as floating or hopping; the cat zig-zags to high or low spots over several walks instead.</summary>
    public const double MaxWalkSlope = 0.55;

    public RoamIntent Next(RoamContext c)
    {
        if (c.UserIdleSeconds >= SleepAfterIdle) return new(RoamAction.Sleep, Between(30, 60));
        if (c.Last == RoamAction.Sleep) return new(RoamAction.Stretch, 2.6);

        var roll = rng.NextDouble();
        if (c.Staying)
        {
            return roll switch
            {
                < 0.7 => new(RoamAction.Sit, Between(4, 10)),
                < 0.9 => new(RoamAction.Groom, 3),
                _ => new(RoamAction.Sleep, Between(10, 25)),
            };
        }

        if (roll < 0.35) return new(RoamAction.Sit, Between(3, 8));
        if (roll < 0.70) return Walk(c);
        if (roll < 0.80) return new(RoamAction.Groom, 3);
        if (roll < 0.95)
        {
            if (c.FreeRoam) return Walk(c); // free roaming reaches everywhere on foot — no window hopping
            var reachable = c.Platforms
                .Where(p => p.Y < c.Y - 40 && c.Y - p.Y <= MotionController.MaxJumpHeight && p.Right - p.Left >= 80)
                .ToList();
            if (reachable.Count == 0) return Walk(c);
            var target = reachable[rng.Next(reachable.Count)];
            var x = Between(target.Left + 30, target.Right - 30);
            return new(RoamAction.JumpUp, 4, x, target);
        }
        return new(RoamAction.Sleep, Between(10, 20));
    }

    private RoamIntent Walk(RoamContext c)
    {
        if (c.FreeRoam) return WalkAnywhere(c);
        var min = c.Area.Left + MotionController.HalfWidth;
        var max = c.Area.Right - MotionController.HalfWidth;
        double target;
        var tries = 0;
        do target = Between(min, max);
        while (Math.Abs(target - c.X) < 80 && ++tries < 10);
        return new(RoamAction.Walk, Math.Abs(target - c.X) / MotionController.WalkSpeed + 0.5, target);
    }

    /// <summary>
    /// A stroll to another spot anywhere on screen — sideways with a gentle up/down drift, so over a few walks the
    /// cat reaches the middle, corners, sides and top without ever gliding steeply.
    /// </summary>
    private RoamIntent WalkAnywhere(RoamContext c)
    {
        var a = c.Area;
        var minX = a.Left + MotionController.HalfWidth;
        var maxX = a.Right - MotionController.HalfWidth;
        var minY = a.Top + Math.Min(100, (a.Bottom - a.Top) / 3);

        var reach = Between(140, 520);
        var dir = rng.NextDouble() < 0.5 ? -1 : 1;
        if (c.X + dir * reach > maxX || c.X + dir * reach < minX) dir = -dir; // turn around at the screen edge
        var x = Math.Clamp(c.X + dir * reach, minX, maxX);
        var dx = Math.Abs(x - c.X);
        var y = Math.Clamp(c.Y + Between(-1, 1) * dx * MaxWalkSlope, minY, a.Bottom);
        var dist = Math.Sqrt(dx * dx + (y - c.Y) * (y - c.Y));
        return new(RoamAction.Walk, dist / MotionController.WalkSpeed + 0.5, x, TargetY: y);
    }

    private double Between(double a, double b) => a + rng.NextDouble() * (b - a);
}
