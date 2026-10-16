namespace Pixel.Core.Motion;

/// <summary>
/// Remembers where the dragged cat was over the last moments, so letting go can tell a throw from a drop: a fast
/// flick at release is a throw with that speed and direction; a slow move, or holding still first, is a drop.
/// </summary>
public sealed class ThrowTracker
{
    /// <summary>How much of the drag (seconds before release) the throw speed is measured over.</summary>
    public const double Window = 0.12;

    /// <summary>Slower than this (DIP/s) at release is a gentle drop.</summary>
    public const double MinThrowSpeed = 350;

    /// <summary>Faster flings are capped so the cat never flies wildly.</summary>
    public const double MaxThrowSpeed = 3000;

    private readonly List<(double T, double X, double Y)> _samples = [];

    /// <summary>Speed measured at the last release (DIP/s, before capping), for diagnostics.</summary>
    public double LastSpeed { get; private set; }

    public void Reset() => _samples.Clear();

    /// <summary>Records the cat's position while it's held (every tick, moving or not).</summary>
    public void Add(double t, double x, double y)
    {
        _samples.Add((t, x, y));
        var keepFrom = _samples.FindIndex(s => t - s.T <= Window * 2);
        if (keepFrom > 0) _samples.RemoveRange(0, keepFrom);
    }

    /// <summary>The throw velocity when released at <paramref name="t"/>, or null for a gentle drop.</summary>
    public (double Vx, double Vy)? Release(double t)
    {
        LastSpeed = 0;
        var recent = _samples.Where(s => t - s.T <= Window).ToList();
        if (recent.Count < 2) return null;
        var (t0, x0, y0) = recent[0];
        var (t1, x1, y1) = recent[^1];
        var dt = t1 - t0;
        if (dt < 0.02) return null;

        double vx = (x1 - x0) / dt, vy = (y1 - y0) / dt;
        var speed = LastSpeed = Math.Sqrt(vx * vx + vy * vy);
        if (speed < MinThrowSpeed) return null;
        if (speed > MaxThrowSpeed)
        {
            vx *= MaxThrowSpeed / speed;
            vy *= MaxThrowSpeed / speed;
        }
        return (vx, vy);
    }
}
