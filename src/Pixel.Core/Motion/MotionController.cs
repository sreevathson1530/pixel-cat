namespace Pixel.Core.Motion;

/// <summary>A horizontal surface the cat can stand on: a window's top edge, or the ground (Owner 0).</summary>
public readonly record struct Platform(double Left, double Right, double Y, long Owner = 0)
{
    public bool Covers(double x) => x >= Left && x <= Right;
}

public readonly record struct Bounds(double Left, double Top, double Right, double Bottom);

public enum Facing { Right, Left }

public enum MotionMode { Idle, Walking, Airborne, Held }

/// <summary>What a thrown cat bounced off.</summary>
public enum Bump { Wall, Ceiling, Floor }

/// <summary>Cat position: X is the horizontal centre, Y is the bottom of the feet (DIPs, y grows down).</summary>
public sealed class CatBody
{
    public double X { get; internal set; }
    public double Y { get; internal set; }
    public double Vx { get; internal set; }
    public double Vy { get; internal set; }
    public Facing Facing { get; internal set; }
    public bool Grounded { get; internal set; }
    public Platform? StandingOn { get; internal set; }
}

/// <summary>
/// Moves the cat inside the work area. Two styles: <see cref="FreeRoam"/> (walk anywhere on screen, no gravity —
/// the default) or gravity mode (floor and window tops: walking, jumping, falling, riding windows).
/// </summary>
public sealed class MotionController(Bounds area)
{
    public const double HalfWidth = 24;
    public const double FreePounceReach = 260;
    public const double Gravity = 2600;
    public const double WalkSpeed = 66; // matches the 8-step walk cycle so paws don't slide
    public const double RunSpeed = 260;
    public const double MaxJumpHeight = 450;
    private const double SnapTolerance = 14;

    /// <summary>Speed kept bouncing off a side of the screen, the top, or the floor (which also slows it sideways).</summary>
    public const double WallBounce = 0.7, CeilingBounce = 0.5, FloorBounce = 0.45, FloorFriction = 0.7;

    /// <summary>Landing slower than this (DIP/s) just lands; faster makes a little hop.</summary>
    public const double MinBounceSpeed = 300;

    /// <summary>A throw always ends within this long, whatever happens.</summary>
    public const double MaxFlightSeconds = 6;

    private Bounds _area = area;
    private double _speed = WalkSpeed;
    private double? _leapTargetX;
    private bool _flying;
    private double _flightTime;
    private readonly List<Bump> _bumps = [];

    public CatBody Body { get; } = new();
    public MotionMode Mode { get; private set; } = MotionMode.Idle;
    public double? TargetX { get; private set; }
    public double? TargetY { get; private set; }

    /// <summary>Walk anywhere on screen (middle, sides, top) instead of only the floor and window tops.</summary>
    public bool FreeRoam { get; set; }

    /// <summary>Height of the cat above its feet, so free roaming never pushes it off the top of the screen.</summary>
    public double BodyHeight { get; set; }
    public Bounds Area => _area;

    /// <summary>Thrown and still in the air (bouncing around), in either movement style.</summary>
    public bool Flying => _flying;

    /// <summary>Every bounce so far (never resets), so watchers can tell a new one happened.</summary>
    public int BumpCount { get; private set; }
    public Bump? LastBump { get; private set; }

    /// <summary>The bounces of the current (or last) throw, in order.</summary>
    public IReadOnlyList<Bump> BumpsSeen => _bumps;
    private Platform Ground => new(_area.Left, _area.Right, _area.Bottom);

    public void SetArea(Bounds area)
    {
        _area = area;
        Body.X = ClampX(Body.X);
        if (FreeRoam)
        {
            Body.Y = ClampY(Body.Y);
            return;
        }
        if (Body.Y > area.Bottom) Body.Y = area.Bottom;
        if (Body.StandingOn is { Owner: 0 }) Unground(); // ground moved; re-land next step
    }

    public void Place(double x, double y)
    {
        _flying = false;
        if (FreeRoam)
        {
            Body.X = ClampX(x);
            Body.Y = ClampY(y);
            RestHere();
            return;
        }
        Body.X = ClampX(x);
        Body.Y = Math.Min(y, _area.Bottom);
        Body.Vx = Body.Vy = 0;
        Mode = MotionMode.Idle;
        TargetX = null;
        if (Math.Abs(Body.Y - _area.Bottom) < 0.5) Land(Ground);
        else
        {
            Unground();
            Mode = MotionMode.Airborne;
        }
    }

    public void WalkTo(double x, double speed = WalkSpeed)
    {
        if (FreeRoam)
        {
            WalkToPoint(x, Body.Y, speed);
            return;
        }
        if (Mode is MotionMode.Held or MotionMode.Airborne || _flying) return;
        TargetX = ClampX(x);
        _speed = speed;
        Mode = MotionMode.Walking;
    }

    /// <summary>Free roaming: walk straight to any point. In gravity mode only the X matters.</summary>
    public void WalkToPoint(double x, double y, double speed = WalkSpeed)
    {
        if (!FreeRoam)
        {
            WalkTo(x, speed);
            return;
        }
        if (Mode == MotionMode.Held || _flying) return;
        TargetX = ClampX(x);
        TargetY = ClampY(y);
        _speed = speed;
        Mode = MotionMode.Walking;
        if (Math.Abs(TargetX.Value - Body.X) > 1) Body.Facing = TargetX < Body.X ? Facing.Left : Facing.Right;
    }

    public void Stop()
    {
        if (Mode != MotionMode.Walking) return;
        Mode = MotionMode.Idle;
        TargetX = null;
        TargetY = null;
    }

    /// <summary>Pounce at a point: a quick dash (free roaming, limited reach) or a hop along the floor (gravity).</summary>
    public bool Pounce(double x, double y)
    {
        if (!FreeRoam) return Pounce(x);
        var dx = x - Body.X;
        var dy = y - Body.Y;
        var dist = Math.Sqrt(dx * dx + dy * dy);
        if (dist > FreePounceReach)
        {
            dx *= FreePounceReach / dist;
            dy *= FreePounceReach / dist;
        }
        WalkToPoint(Body.X + dx, Body.Y + dy, RunSpeed * 1.6);
        return true;
    }

    /// <summary>Ballistic jump that lands at <paramref name="x"/> on <paramref name="target"/>. False if out of reach.</summary>
    public bool JumpTo(Platform target, double x) => Leap(ClampX(x), target.Y);

    /// <summary>Short pounce along the current surface.</summary>
    public bool Pounce(double x) => Leap(ClampX(x), Body.Y, apex: 70);

    private bool Leap(double tx, double ty, double apex = 60)
    {
        if (Mode == MotionMode.Held || !Body.Grounded) return false;
        if (Body.Y - ty > MaxJumpHeight) return false;

        var apexY = Math.Min(Body.Y, ty) - apex;
        var vy0 = -Math.Sqrt(2 * Gravity * (Body.Y - apexY));
        var tUp = -vy0 / Gravity;
        var tDown = Math.Sqrt(2 * (ty - apexY) / Gravity);
        Body.Vx = (tx - Body.X) / (tUp + tDown);
        Body.Vy = vy0;
        Body.Facing = tx >= Body.X ? Facing.Right : Facing.Left;
        Mode = MotionMode.Airborne;
        TargetX = null;
        _leapTargetX = tx;
        Unground();
        return true;
    }

    public void Hold(double x, double y)
    {
        _leapTargetX = null;
        _flying = false; // caught mid-air
        Mode = MotionMode.Held;
        TargetX = null;
        Body.X = x;
        Body.Y = y;
        Body.Vx = Body.Vy = 0;
        Unground();
    }

    public void Release()
    {
        if (Mode != MotionMode.Held) return;
        Body.X = ClampX(Body.X);
        if (FreeRoam)
        {
            Body.Y = ClampY(Body.Y);
            RestHere(); // free roaming: dropped cats stay exactly where they were let go
            return;
        }
        Mode = MotionMode.Airborne;
    }

    /// <summary>
    /// Let go with a fling: the cat flies with gravity, bounces off the sides and top of the screen, makes a few
    /// shrinking hops on landing, then settles. Works in both movement styles.
    /// </summary>
    public void Throw(double vx, double vy)
    {
        if (Mode != MotionMode.Held) return;
        Body.X = ClampX(Body.X);
        Body.Y = Math.Clamp(Body.Y, Ceiling, _area.Bottom);
        Body.Vx = vx;
        Body.Vy = vy;
        if (Math.Abs(vx) > 1) Body.Facing = vx < 0 ? Facing.Left : Facing.Right;
        _leapTargetX = null;
        TargetX = TargetY = null;
        Unground();
        Mode = MotionMode.Airborne;
        _flying = true;
        _flightTime = 0;
        _bumps.Clear();
    }

    public void Step(double dt, IReadOnlyList<Platform> platforms)
    {
        if (Mode == MotionMode.Held) return;
        if (_flying)
        {
            Fly(dt, platforms);
            return;
        }
        if (FreeRoam)
        {
            if (!Body.Grounded) RestHere();
            if (Mode == MotionMode.Walking) WalkFree(dt);
            return;
        }

        if (Body.Grounded)
        {
            FollowSupport(platforms);
            if (!Body.Grounded) { Mode = MotionMode.Airborne; return; }
            if (Mode == MotionMode.Walking) Walk(dt);
            return;
        }

        // Airborne (falling, jumping or just released).
        Mode = MotionMode.Airborne;
        var prevY = Body.Y;
        Body.Vy += Gravity * dt;
        Body.X = ClampX(Body.X + Body.Vx * dt);
        Body.Y += Body.Vy * dt;
        Body.Y = Math.Max(Body.Y, _area.Top + 40);

        if (Body.Vy < 0) return;
        Platform? landing = null;
        foreach (var p in platforms.Append(Ground))
        {
            if (!p.Covers(Body.X) || p.Y < prevY - 0.001 || p.Y > Body.Y) continue;
            if (landing is null || p.Y < landing.Value.Y) landing = p;
        }
        if (landing is { } l) Land(l);
    }

    /// <summary>Highest the feet can be so the whole cat stays on screen.</summary>
    private double Ceiling => Math.Min(_area.Top + Math.Max(BodyHeight, 40), _area.Bottom);

    private void Fly(double dt, IReadOnlyList<Platform> platforms)
    {
        _flightTime += dt;
        var prevY = Body.Y;
        Body.Vy += Gravity * dt;
        var x = Body.X + Body.Vx * dt;
        var y = Body.Y + Body.Vy * dt;

        double left = _area.Left + HalfWidth, right = _area.Right - HalfWidth;
        if ((x <= left && Body.Vx < 0) || (x >= right && Body.Vx > 0))
        {
            Body.Vx = -Body.Vx * WallBounce;
            Bumped(Bump.Wall);
        }
        Body.X = Math.Clamp(x, left, right);
        if (Math.Abs(Body.Vx) > 30) Body.Facing = Body.Vx < 0 ? Facing.Left : Facing.Right;

        if (y < Ceiling)
        {
            y = Ceiling;
            if (Body.Vy < 0)
            {
                Body.Vy = -Body.Vy * CeilingBounce;
                Bumped(Bump.Ceiling);
            }
        }
        Body.Y = y;

        if (Body.Vy > 0)
        {
            // Free roaming lands on the bottom of the screen; floor mode also on window tops.
            Platform? landing = null;
            foreach (var p in FreeRoam ? [Ground] : platforms.Append(Ground))
            {
                if (!p.Covers(Body.X) || p.Y < prevY - 0.001 || p.Y > Body.Y) continue;
                if (landing is null || p.Y < landing.Value.Y) landing = p;
            }
            if (landing is { } l)
            {
                Body.Y = l.Y;
                if (Body.Vy > MinBounceSpeed)
                {
                    Body.Vy = -Body.Vy * FloorBounce;
                    Body.Vx *= FloorFriction;
                    Bumped(Bump.Floor);
                }
                else
                {
                    EndFlight(l);
                    return;
                }
            }
        }
        if (_flightTime >= MaxFlightSeconds) EndFlight(null);
    }

    private void Bumped(Bump b)
    {
        _bumps.Add(b);
        LastBump = b;
        BumpCount++;
    }

    /// <summary>Lands on <paramref name="surface"/>; with none (time's up) free roaming rests where it is, floor mode falls normally.</summary>
    private void EndFlight(Platform? surface)
    {
        _flying = false;
        if (FreeRoam)
        {
            if (surface is { } s) Body.Y = s.Y;
            Body.Y = ClampY(Body.Y);
            RestHere();
            return;
        }
        if (surface is { } p) Land(p);
        else Body.Vx = 0; // keep falling straight down with ordinary gravity
    }

    private void WalkFree(double dt)
    {
        if (TargetX is not { } tx || TargetY is not { } ty)
        {
            Mode = MotionMode.Idle;
            return;
        }
        var dx = tx - Body.X;
        var dy = ty - Body.Y;
        var dist = Math.Sqrt(dx * dx + dy * dy);
        var step = _speed * dt;
        if (dist <= step)
        {
            Body.X = tx;
            Body.Y = ty;
            Mode = MotionMode.Idle;
            TargetX = TargetY = null;
            return;
        }
        Body.X = ClampX(Body.X + dx / dist * step);
        Body.Y = ClampY(Body.Y + dy / dist * step);
        if (Math.Abs(dx) > 1) Body.Facing = dx < 0 ? Facing.Left : Facing.Right;
    }

    /// <summary>Free roaming has no gravity: wherever the cat is, it is standing.</summary>
    private void RestHere()
    {
        _flying = false;
        Body.Vx = Body.Vy = 0;
        Body.Grounded = true;
        Body.StandingOn = null;
        _leapTargetX = null;
        Mode = MotionMode.Idle;
        TargetX = TargetY = null;
    }

    private void Walk(double dt)
    {
        if (TargetX is not { } target) { Mode = MotionMode.Idle; return; }
        var dir = Math.Sign(target - Body.X);
        Body.Facing = dir < 0 ? Facing.Left : Facing.Right;
        var step = _speed * dt;
        if (Math.Abs(target - Body.X) <= step)
        {
            Body.X = target;
            Mode = MotionMode.Idle;
            TargetX = null;
        }
        else
        {
            Body.X = ClampX(Body.X + dir * step);
        }

        // Walked off the edge of a window?
        if (Body.StandingOn is { } on && !on.Covers(Body.X))
        {
            Body.Vx = dir * _speed;
            Body.Vy = 0;
            Unground();
            Mode = MotionMode.Airborne;
        }
    }

    /// <summary>Keeps the cat on its window as the window moves; drops it when the window disappears.</summary>
    private void FollowSupport(IReadOnlyList<Platform> platforms)
    {
        if (Body.StandingOn is not { } on) { Unground(); return; }
        if (on.Owner == 0)
        {
            Body.Y = _area.Bottom;
            Body.StandingOn = Ground;
            return;
        }

        foreach (var p in platforms)
        {
            if (p.Owner != on.Owner) continue;
            Body.X = ClampX(Body.X + (p.Left - on.Left));
            if (!p.Covers(Body.X) || Math.Abs(p.Y - on.Y) > 200) break;
            Body.Y = p.Y;
            Body.StandingOn = p;
            return;
        }

        // Same-owner platform gone: try another surface right under the feet, else fall.
        foreach (var p in platforms.Append(Ground))
        {
            if (p.Covers(Body.X) && Math.Abs(p.Y - Body.Y) <= SnapTolerance)
            {
                Land(p);
                return;
            }
        }
        Unground();
    }

    private void Land(Platform p)
    {
        _flying = false;
        // Integration drifts a few pixels; finish a leap exactly where it was aimed.
        if (_leapTargetX is { } tx && Math.Abs(tx - Body.X) < 20 && p.Covers(tx)) Body.X = tx;
        _leapTargetX = null;
        Body.Y = p.Y;
        Body.Vx = Body.Vy = 0;
        Body.Grounded = true;
        Body.StandingOn = p;
        Mode = MotionMode.Idle;
    }

    private void Unground()
    {
        Body.Grounded = false;
        Body.StandingOn = null;
    }

    private double ClampX(double x) => Math.Clamp(x, _area.Left + HalfWidth, _area.Right - HalfWidth);

    private double ClampY(double y) => Math.Clamp(y, Math.Min(_area.Top + BodyHeight, _area.Bottom), _area.Bottom);
}
