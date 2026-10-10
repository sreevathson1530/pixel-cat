using Pixel.Core.Art;
using Pixel.Core.Motion;

namespace Pixel.Core.Behaviour;

/// <summary>
/// The cat's "pet brain": each tick it chooses an activity, drives motion and picks the animation to draw.
/// Pure logic — the app feeds it signals and renders <see cref="Animation"/>.
/// </summary>
public sealed class PetDirector(MotionController motion, Roamer roamer)
{
    private const double CrouchBeforePounce = 0.6;
    private const double PounceReach = 250;

    private RoamIntent? _intent;
    private double _intentUntil;
    private RoamAction? _lastRoam;
    private bool _intentStarted;
    private bool _sleepingFromIdle;
    private double? _pounceAt;
    private double _huntTargetX, _huntTargetY;
    private bool _dashing;

    public AnimationId Animation { get; private set; } = AnimationId.Idle;
    public double AnimationStart { get; private set; }
    public Activity Activity { get; private set; } = Activity.Roam;
    public bool Overheated => Activity == Activity.Overheat;

    /// <summary>Dropped somewhere by the user: no wandering until double-clicked.</summary>
    public bool Staying { get; set; }

    /// <summary>Cursor position in the same coordinates as the motion area (for hunting).</summary>
    public double CursorX { get; set; }
    public double CursorY { get; set; }

    /// <summary>Where something needs attention (e.g. an error on screen): the cat runs there before alerting.</summary>
    public double? AttentionX { get; set; }

    /// <summary>Vertical part of the attention point (used when free roaming).</summary>
    public double? AttentionY { get; set; }

    public void Tick(double now, double dt, PetSignals s, IReadOnlyList<Platform> platforms, double userIdleSeconds)
    {
        var activity = ResolveHunt(BehaviourSelector.Select(s), now);
        if (activity != Activity.Roam) _intent = null;
        Activity = activity;

        var anim = activity switch
        {
            Activity.Dangle => AnimationId.Dangle,
            Activity.PoofOut => AnimationId.PoofOut,
            Activity.PoofIn => AnimationId.PoofIn,
            Activity.Thinking => AnimationId.Thinking,
            Activity.Talk => AnimationId.Talk,
            Activity.Peek => AnimationId.Peek,
            Activity.AgentWorking => AnimationId.Working,
            Activity.AgentDone => AnimationId.Celebrate,
            Activity.AgentNeedsYou => AnimationId.Wave,
            Activity.Alert => AnimationId.Alert,
            Activity.Overheat => AnimationId.Overheat,
            Activity.Hunt => AnimationId.Crouch,
            Activity.Purr => AnimationId.Purr,
            Activity.Knead => AnimationId.Knead,
            Activity.Paper => AnimationId.Paper,
            _ when _dashing => AnimationId.Run,
            _ => Roam(now, platforms, userIdleSeconds),
        };
        if (_dashing && motion.Mode != MotionMode.Walking) _dashing = false;

        var goingToAttention = false;
        if (activity == Activity.Alert && motion.FreeRoam && AttentionX is { } fx && AttentionY is { } fy)
        {
            var dist = Math.Sqrt(Math.Pow(motion.Body.X - fx, 2) + Math.Pow(motion.Body.Y - fy, 2));
            if (dist > 30)
            {
                if (motion.Mode != MotionMode.Walking) motion.WalkToPoint(fx, fy, MotionController.RunSpeed);
                goingToAttention = true;
                anim = AnimationId.Run;
            }
            else
            {
                AttentionX = AttentionY = null;
            }
        }
        else if (activity == Activity.Alert && AttentionX is { } ax && motion.Body.Grounded)
        {
            if (Math.Abs(motion.Body.X - ax) > 30)
            {
                if (motion.Mode != MotionMode.Walking) motion.WalkTo(ax, MotionController.RunSpeed);
                goingToAttention = true;
                anim = AnimationId.Run;
            }
            else
            {
                AttentionX = null;
            }
        }

        if (activity is not (Activity.Roam or Activity.Dangle) && !goingToAttention) motion.Stop();
        if (activity == Activity.Hunt && _pounceAt is { } at && now >= at)
        {
            if (motion.FreeRoam)
            {
                motion.Pounce(_huntTargetX, _huntTargetY);
                _dashing = true;
            }
            else
            {
                var reach = Math.Clamp(_huntTargetX - motion.Body.X, -PounceReach, PounceReach);
                motion.Pounce(motion.Body.X + reach);
            }
            _pounceAt = null;
        }

        motion.Step(dt, platforms);

        var airborne = !motion.Body.Grounded && motion.Mode != MotionMode.Held;
        if (airborne && activity is not (Activity.Dangle or Activity.PoofOut or Activity.PoofIn))
            anim = motion.Body.Vy < 0 ? AnimationId.Jump : AnimationId.Fall;

        if (anim != Animation)
        {
            Animation = anim;
            AnimationStart = now;
        }
    }

    /// <summary>A hunt is a short sequence: crouch, then pounce — it finishes even if the mouse stops.</summary>
    private Activity ResolveHunt(Activity selected, double now)
    {
        if (selected == Activity.Hunt && _pounceAt is null && motion.Body.Grounded && Activity != Activity.Hunt)
        {
            _pounceAt = now + CrouchBeforePounce;
            _huntTargetX = CursorX;
            _huntTargetY = CursorY;
        }
        if (_pounceAt is null) return selected == Activity.Hunt && !motion.Body.Grounded ? Activity.Roam : selected;
        if (selected is Activity.Roam or Activity.Hunt or Activity.Purr or Activity.Knead or Activity.Paper)
            return Activity.Hunt;
        _pounceAt = null; // something more important interrupted the hunt
        return selected;
    }

    private AnimationId Roam(double now, IReadOnlyList<Platform> platforms, double userIdleSeconds)
    {
        if (!motion.Body.Grounded) return AnimationId.Fall;

        var wokeUp = _intent?.Action == RoamAction.Sleep && _sleepingFromIdle && userIdleSeconds < 2;
        var walkDone = _intent?.Action == RoamAction.Walk && _intentStarted && motion.Mode != MotionMode.Walking;
        var jumpDone = _intent?.Action == RoamAction.JumpUp && _intentStarted && motion.Mode != MotionMode.Airborne;
        var stayInterrupts = Staying && _intent?.Action is RoamAction.Walk or RoamAction.JumpUp;
        if (_intent is null || now >= _intentUntil || wokeUp || walkDone || jumpDone || stayInterrupts)
        {
            if (_intent is not null) _lastRoam = _intent.Action;
            motion.Stop();
            _intent = roamer.Next(new RoamContext(
                motion.Body.X, motion.Body.Y, motion.Area, platforms, userIdleSeconds, Staying, _lastRoam, motion.FreeRoam));
            _intentUntil = now + _intent.Seconds;
            _intentStarted = false;
            _sleepingFromIdle = _intent.Action == RoamAction.Sleep && userIdleSeconds >= Roamer.SleepAfterIdle;
        }

        switch (_intent.Action)
        {
            case RoamAction.Walk:
                if (!_intentStarted)
                {
                    if (motion.FreeRoam) motion.WalkToPoint(_intent.TargetX ?? motion.Body.X, _intent.TargetY ?? motion.Body.Y);
                    else motion.WalkTo(_intent.TargetX ?? motion.Body.X);
                    _intentStarted = true;
                }
                return AnimationId.Walk;
            case RoamAction.JumpUp:
                if (!_intentStarted && now >= _intentUntil - _intent.Seconds + 0.4)
                {
                    if (_intent.Target is { } target) motion.JumpTo(target, _intent.TargetX ?? motion.Body.X);
                    _intentStarted = true;
                }
                return AnimationId.Crouch;
            case RoamAction.Groom: return AnimationId.Groom;
            case RoamAction.Sleep: return AnimationId.Sleep;
            case RoamAction.Stretch: return AnimationId.Stretch;
            default: return AnimationId.Idle;
        }
    }
}
