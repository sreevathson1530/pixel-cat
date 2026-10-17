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

    /// <summary>How long the laptop stays out after typing pauses, so slow typing doesn't make it pop in and out.</summary>
    public const double LaptopGraceSeconds = 2.5;

    private bool _laptopOut;
    private double _laptopIntroUntil, _laptopGraceUntil, _laptopAwayUntil = double.NegativeInfinity;

    private RoamIntent? _intent;
    private double _intentUntil;
    private RoamAction? _lastRoam;
    private bool _intentStarted;
    private bool _sleepingFromIdle;
    private double? _pounceAt;
    private double _huntTargetX, _huntTargetY;
    private bool _dashing;

    /// <summary>How long the "bonk" shows after a thrown cat hits a side or the top of the screen.</summary>
    private const double BonkSeconds = 0.3;
    private int _seenBumps;
    private bool _wasFlying;
    private double _bonkUntil = double.NegativeInfinity, _dazedUntil = double.NegativeInfinity;

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
        var laptopActivity = activity is Activity.Knead or Activity.AgentWorking or Activity.Overheat;
        var laptopPhase = LaptopPhase(activity, laptopActivity, now);
        if (activity != Activity.Roam || laptopPhase is not null) _intent = null;
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
            Activity.Greet => AnimationId.Hello,
            _ when laptopPhase is { } phase => phase,
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

        if (laptopActivity && now < _laptopIntroUntil) anim = AnimationId.LaptopOut;
        if ((activity is not (Activity.Roam or Activity.Dangle) || laptopPhase is not null) && !goingToAttention) motion.Stop();
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
        ReactToThrow(now);

        var airborne = !motion.Body.Grounded && motion.Mode != MotionMode.Held;
        if (airborne && activity is not (Activity.Dangle or Activity.PoofOut or Activity.PoofIn))
            anim = now < _bonkUntil ? AnimationId.Bonk : motion.Body.Vy < 0 ? AnimationId.Jump : AnimationId.Fall;
        else if (now < _dazedUntil && activity is Activity.Roam or Activity.Hunt or Activity.Purr)
        {
            anim = AnimationId.Dazed; // dizzy for a moment after a throw; nothing else until it shakes it off
            motion.Stop();
            _pounceAt = null;
        }

        if (anim != Animation)
        {
            Animation = anim;
            AnimationStart = now;
        }
    }

    /// <summary>A bonk for each new bounce off a side or the top; a daze once a throw comes to rest.</summary>
    private void ReactToThrow(double now)
    {
        if (motion.BumpCount != _seenBumps)
        {
            if (motion.LastBump is Bump.Wall or Bump.Ceiling) _bonkUntil = now + BonkSeconds;
            _seenBumps = motion.BumpCount;
        }
        if (_wasFlying && !motion.Flying && motion.Mode != MotionMode.Held)
            _dazedUntil = now + Animations.Get(AnimationId.Dazed).TotalMs / 1000.0;
        _wasFlying = motion.Flying;
    }

    /// <summary>
    /// The laptop routine around typing / AI work: it comes out of the pocket when work starts, stays out through
    /// short pauses, and goes back in before the cat wanders off. Returns the animation that overrides roaming.
    /// </summary>
    private AnimationId? LaptopPhase(Activity activity, bool laptopActivity, double now)
    {
        if (laptopActivity)
        {
            if (!_laptopOut && now >= _laptopAwayUntil)
                _laptopIntroUntil = now + Animations.Get(AnimationId.LaptopOut).TotalMs / 1000.0;
            _laptopOut = true;
            _laptopGraceUntil = now + LaptopGraceSeconds;
            _laptopAwayUntil = double.NegativeInfinity;
            return null;
        }
        if (_laptopOut)
        {
            if (activity != Activity.Roam)
            {
                _laptopOut = false; // something else took over (chat, alert, drag…) — just drop the routine
                return null;
            }
            if (now < _laptopGraceUntil) return AnimationId.LaptopIdle;
            _laptopOut = false;
            _laptopAwayUntil = now + Animations.Get(AnimationId.LaptopAway).TotalMs / 1000.0;
        }
        return activity == Activity.Roam && now < _laptopAwayUntil ? AnimationId.LaptopAway : null;
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
        if (selected is Activity.Roam or Activity.Hunt or Activity.Purr or Activity.Knead)
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
