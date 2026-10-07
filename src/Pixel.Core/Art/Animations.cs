namespace Pixel.Core.Art;

public enum AnimationId
{
    Idle, Walk, Run, Jump, Fall, Sleep, Groom, Stretch, Purr, Crouch, Knead, Overheat, Paper,
    Working, Celebrate, Wave, Alert, Confused, Peek, Dangle, PoofOut, PoofIn, Talk, Thinking,
}

public sealed record Frame(CatPose Pose, int Ms);

public sealed record Animation(AnimationId Id, IReadOnlyList<Frame> Frames, bool Loop)
{
    public int TotalMs { get; } = Frames.Sum(f => f.Ms);

    /// <summary>Frame shown <paramref name="elapsedMs"/> after the animation started.</summary>
    public Frame At(double elapsedMs)
    {
        var t = elapsedMs < 0 ? 0 : elapsedMs;
        if (Loop) t %= TotalMs;
        else if (t >= TotalMs) return Frames[^1];
        foreach (var f in Frames)
        {
            if (t < f.Ms) return f;
            t -= f.Ms;
        }
        return Frames[^1];
    }

    public bool IsFinished(double elapsedMs) => !Loop && elapsedMs >= TotalMs;
}

/// <summary>The cat's full animation catalogue.</summary>
public static class Animations
{
    private static readonly CatPose Sit = new(BodyShape.Sit);
    private static readonly Dictionary<AnimationId, Animation> All = Build();

    public static Animation Get(AnimationId id) => All[id];

    private static Dictionary<AnimationId, Animation> Build()
    {
        var d = new Dictionary<AnimationId, Animation>();
        void Add(AnimationId id, bool loop, params Frame[] frames) => d[id] = new Animation(id, frames, loop);

        Add(AnimationId.Idle, true,
            new(Sit, 2200),
            new(Sit with { Eyes = EyeState.Closed }, 130),
            new(Sit with { Tail = 1 }, 1600),
            new(Sit with { Tail = 2 }, 900),
            new(Sit with { Tail = 1 }, 900),
            new(Sit with { Eyes = EyeState.Closed }, 130),
            new(Sit, 1400));

        var stand = new CatPose(BodyShape.Stand);
        Add(AnimationId.Walk, true,
            new(stand with { LegPhase = 0, Tail = 0 }, 130),
            new(stand with { LegPhase = 1, Tail = 1 }, 130),
            new(stand with { LegPhase = 2, Tail = 0 }, 130),
            new(stand with { LegPhase = 3, Tail = 2 }, 130));

        Add(AnimationId.Run, true,
            new(stand with { LegPhase = 4, Tail = 2, Eyes = EyeState.Wide }, 90),
            new(stand with { LegPhase = 5, Tail = 1, Eyes = EyeState.Wide }, 90));

        Add(AnimationId.Jump, true, new Frame(new CatPose(BodyShape.Jump, Eyes: EyeState.Wide), 100));
        Add(AnimationId.Fall, true, new Frame(new CatPose(BodyShape.Fall, Eyes: EyeState.Wide, Mouth: MouthState.Open), 100));

        var loaf = new CatPose(BodyShape.Loaf, Eyes: EyeState.Closed, Effect: Effect.Zz);
        Add(AnimationId.Sleep, true,
            new(loaf with { LegPhase = 0, EffectFrame = 0 }, 650),
            new(loaf with { LegPhase = 1, EffectFrame = 1 }, 650),
            new(loaf with { LegPhase = 0, EffectFrame = 2 }, 650),
            new(loaf with { LegPhase = 1, EffectFrame = 3 }, 650));

        var groom = Sit with { Paws = PawState.Groom, Eyes = EyeState.Happy };
        Add(AnimationId.Groom, true,
            new(groom, 280),
            new(groom with { HeadDx = 1, Mouth = MouthState.Open }, 280),
            new(groom, 280),
            new(groom with { HeadDx = 1 }, 280));

        var stretch = new CatPose(BodyShape.Stretch, Eyes: EyeState.Happy);
        Add(AnimationId.Stretch, false,
            new(stretch, 900),
            new(stretch with { Eyes = EyeState.Closed, Mouth = MouthState.Open }, 900),
            new(stretch, 500),
            new(Sit, 300));

        var purr = Sit with { Eyes = EyeState.Happy, Effect = Effect.Hearts };
        Add(AnimationId.Purr, true,
            new(purr with { EffectFrame = 0 }, 180),
            new(purr with { EffectFrame = 1, BodyDx = 1 }, 180),
            new(purr with { EffectFrame = 2 }, 180),
            new(purr with { EffectFrame = 3, BodyDx = 1 }, 180));

        var crouch = new CatPose(BodyShape.Crouch, Eyes: EyeState.Wide);
        Add(AnimationId.Crouch, true,
            new(crouch with { LegPhase = 0 }, 140),
            new(crouch with { LegPhase = 1, Tail = 1 }, 140),
            new(crouch with { LegPhase = 0 }, 140),
            new(crouch with { LegPhase = 3, Tail = 1 }, 140));

        var knead = Sit with { Eyes = EyeState.Half, Effect = Effect.Keyboard };
        Add(AnimationId.Knead, true,
            new(knead with { Paws = PawState.LeftUp }, 170),
            new(knead with { Paws = PawState.RightUp }, 170));

        var hot = Sit with { Eyes = EyeState.Wide, Mouth = MouthState.Open, Effect = Effect.Keyboard | Effect.Steam };
        Add(AnimationId.Overheat, true,
            new(hot with { Paws = PawState.LeftUp, EffectFrame = 0 }, 90),
            new(hot with { Paws = PawState.RightUp, EffectFrame = 1, BodyDx = 1 }, 90),
            new(hot with { Paws = PawState.LeftUp, EffectFrame = 2 }, 90),
            new(hot with { Paws = PawState.RightUp, EffectFrame = 3, BodyDx = 1 }, 90));

        var paper = Sit with { Effect = Effect.Paper, Eyes = EyeState.Wide };
        Add(AnimationId.Paper, true,
            new(paper with { EffectFrame = 0, Paws = PawState.LeftUp }, 140),
            new(paper with { EffectFrame = 1 }, 140),
            new(paper with { EffectFrame = 2, Paws = PawState.LeftUp }, 140),
            new(paper with { EffectFrame = 3 }, 140));

        var work = Sit with { Eyes = EyeState.Half, Effect = Effect.Laptop };
        Add(AnimationId.Working, true,
            new(work with { Paws = PawState.LeftUp, EffectFrame = 0 }, 150),
            new(work with { Paws = PawState.RightUp, EffectFrame = 1 }, 150),
            new(work with { Paws = PawState.LeftUp, EffectFrame = 2 }, 150),
            new(work with { Paws = PawState.RightUp, EffectFrame = 3 }, 150));

        var joy = Sit with { Eyes = EyeState.Happy, Mouth = MouthState.Open, Effect = Effect.Sparkles };
        Add(AnimationId.Celebrate, true,
            new(joy with { BodyDy = -3, EffectFrame = 0 }, 140),
            new(joy with { BodyDy = -5, EffectFrame = 1 }, 140),
            new(joy with { BodyDy = -3, EffectFrame = 2 }, 140),
            new(joy with { BodyDy = 0, EffectFrame = 3 }, 220));

        Add(AnimationId.Wave, true,
            new(Sit with { Paws = PawState.WaveUp, Mouth = MouthState.Open }, 240),
            new(Sit with { Paws = PawState.WaveDown }, 240));

        var alert = Sit with { Eyes = EyeState.Wide, Effect = Effect.Exclaim };
        Add(AnimationId.Alert, true,
            new(alert, 380),
            new(alert with { BodyDy = -1 }, 380));

        Add(AnimationId.Confused, true,
            new(Sit with { HeadDx = 1, Effect = Effect.Question }, 700),
            new(Sit with { HeadDx = 1, Eyes = EyeState.Closed, Effect = Effect.Question }, 120),
            new(Sit with { HeadDx = 1, Effect = Effect.Question }, 700));

        var peek = new CatPose(BodyShape.Peek);
        Add(AnimationId.Peek, true,
            new(peek, 900),
            new(peek with { HeadDx = -1 }, 700),
            new(peek with { Eyes = EyeState.Closed }, 130),
            new(peek with { HeadDx = 1 }, 700));

        var dangle = new CatPose(BodyShape.Dangle, Eyes: EyeState.Wide);
        Add(AnimationId.Dangle, true,
            new(dangle with { Tail = 0 }, 220),
            new(dangle with { Tail = 1 }, 220),
            new(dangle with { Tail = 0, Mouth = MouthState.Open }, 220),
            new(dangle with { Tail = 2 }, 220));

        var none = new CatPose(BodyShape.None, Effect: Effect.Poof);
        Add(AnimationId.PoofOut, false,
            new(none with { EffectFrame = 0 }, 110),
            new(none with { EffectFrame = 1 }, 110),
            new(none with { EffectFrame = 2 }, 110),
            new(none with { EffectFrame = 3 }, 110),
            new(new CatPose(BodyShape.None), 60));

        Add(AnimationId.PoofIn, false,
            new(none with { EffectFrame = 3 }, 100),
            new(none with { EffectFrame = 2 }, 100),
            new(none with { EffectFrame = 1 }, 100),
            new(Sit with { Effect = Effect.Poof, EffectFrame = 1 }, 120),
            new(Sit with { Effect = Effect.Sparkles, Eyes = EyeState.Happy }, 400));

        Add(AnimationId.Talk, true,
            new(Sit with { Mouth = MouthState.Open }, 140),
            new(Sit, 160));

        var think = Sit with { Effect = Effect.Dots, HeadDx = 1 };
        Add(AnimationId.Thinking, true,
            new(think with { EffectFrame = 0 }, 380),
            new(think with { EffectFrame = 1 }, 380),
            new(think with { EffectFrame = 2 }, 380));

        return d;
    }
}
