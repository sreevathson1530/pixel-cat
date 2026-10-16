namespace Pixel.Core.Art;

public enum AnimationId
{
    Idle, Walk, Run, Jump, Fall, Sleep, Groom, Stretch, Purr, Crouch, Knead, Overheat, Paper,
    Working, Celebrate, Wave, Alert, Confused, Peek, Dangle, PoofOut, PoofIn, Talk, Thinking,
    LaptopOut, LaptopIdle, LaptopAway, Bonk, Dazed,
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
        // Eight-step walk, 90 ms a step: a stride takes ~0.72 s and covers ~24 DIP at 3× — matched by
        // MotionController.WalkSpeed so the paws don't slide. Tail sways slowly, a step behind the legs.
        int[] tailSway = [0, 0, 1, 1, 0, 0, 2, 2];
        Add(AnimationId.Walk, true,
            Enumerable.Range(0, 8).Select(i => new Frame(stand with { LegPhase = i, Tail = tailSway[i] }, 90)).ToArray());

        Add(AnimationId.Run, true,
            new(stand with { LegPhase = 8, Tail = 2, Eyes = EyeState.Wide }, 90),
            new(stand with { LegPhase = 9, Tail = 1, Eyes = EyeState.Wide }, 90));

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

        // Typing on the laptop from the pocket (you typing, or an AI tool working).
        var front = Sit; // side-on: the laptop sits on the floor beside the cat, in the direction it faces
        var typing = front with { Laptop = LaptopStage.Open, Eyes = EyeState.Half };
        Add(AnimationId.Knead, true,
            new(typing with { Paws = PawState.LeftUp, EffectFrame = 0 }, 160),
            new(typing with { Paws = PawState.RightUp, EffectFrame = 1 }, 160),
            new(typing with { Paws = PawState.LeftUp, EffectFrame = 2, Tail = 1 }, 160),
            new(typing with { Paws = PawState.RightUp, EffectFrame = 3, Tail = 1 }, 160));

        Add(AnimationId.LaptopOut, false,
            new(front with { Laptop = LaptopStage.PawInPocket, Eyes = EyeState.Open }, 240),
            new(front with { Laptop = LaptopStage.Emerging, Eyes = EyeState.Wide }, 220),
            new(front with { Laptop = LaptopStage.HeldUp, Eyes = EyeState.Happy, Mouth = MouthState.Open }, 260),
            new(front with { Laptop = LaptopStage.Closed }, 160),
            new(front with { Laptop = LaptopStage.Half }, 140),
            new(typing, 140));

        Add(AnimationId.LaptopIdle, true,
            new(typing with { Eyes = EyeState.Open }, 1600),
            new(typing with { Eyes = EyeState.Closed }, 120),
            new(typing with { Eyes = EyeState.Open, Tail = 2 }, 900));

        Add(AnimationId.LaptopAway, false,
            new(typing with { Eyes = EyeState.Open }, 120),
            new(front with { Laptop = LaptopStage.Half }, 120),
            new(front with { Laptop = LaptopStage.Closed }, 140),
            new(front with { Laptop = LaptopStage.HeldUp, Eyes = EyeState.Happy }, 200),
            new(front with { Laptop = LaptopStage.Emerging }, 180),
            new(front with { Laptop = LaptopStage.PawInPocket, Eyes = EyeState.Happy }, 180),
            new(front, 140));

        var hot = typing with { Eyes = EyeState.Wide, Mouth = MouthState.Open, Effect = Effect.Steam };
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

        var work = typing;
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

        // Thrown into a side of the screen: eyes squeezed shut, mouth open, stars.
        var bonk = new CatPose(BodyShape.Fall, Eyes: EyeState.Closed, Mouth: MouthState.Open, Effect: Effect.Stars);
        Add(AnimationId.Bonk, false,
            new(bonk with { EffectFrame = 0 }, 150),
            new(bonk with { EffectFrame = 1, HeadDx = -1 }, 150));

        // Landed after a throw: sits dizzy with stars circling its head, then shakes it off.
        var dizzy = Sit with { Eyes = EyeState.Half, Effect = Effect.Stars };
        Add(AnimationId.Dazed, false,
            new(dizzy with { EffectFrame = 0, HeadDx = 1 }, 170),
            new(dizzy with { EffectFrame = 1 }, 170),
            new(dizzy with { EffectFrame = 2, HeadDx = -1 }, 170),
            new(dizzy with { EffectFrame = 3 }, 170),
            new(Sit with { Eyes = EyeState.Closed, BodyDx = 1 }, 80),
            new(Sit with { Eyes = EyeState.Closed, BodyDx = -1 }, 80),
            new(Sit with { Eyes = EyeState.Closed, BodyDx = 1 }, 80),
            new(Sit with { Eyes = EyeState.Happy }, 120));

        var think = Sit with { Effect = Effect.Dots, HeadDx = 1 };
        Add(AnimationId.Thinking, true,
            new(think with { EffectFrame = 0 }, 380),
            new(think with { EffectFrame = 1 }, 380),
            new(think with { EffectFrame = 2 }, 380));

        return d;
    }
}
