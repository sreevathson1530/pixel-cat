namespace Pixel.Core.Art;

public enum BodyShape { Stand, Sit, Loaf, Stretch, Jump, Fall, Dangle, Crouch, Peek, None }

public enum EyeState { Open, Closed, Happy, Half, Wide }

public enum MouthState { Smile, Open }

public enum PawState { Down, LeftUp, RightUp, WaveUp, WaveDown, Groom }

/// <summary>Extra things drawn around the cat. Flags so they can combine (e.g. Keyboard | Steam).</summary>
[Flags]
public enum Effect
{
    None = 0,
    Exclaim = 1 << 0,
    Question = 1 << 1,
    Zz = 1 << 2,
    Hearts = 1 << 3,
    Sparkles = 1 << 4,
    Steam = 1 << 5,
    Laptop = 1 << 6,
    Keyboard = 1 << 7,
    Paper = 1 << 8,
    Poof = 1 << 9,
    Dots = 1 << 10,
}

/// <summary>Everything needed to draw one frame of the cat.</summary>
/// <param name="LegPhase">Walk/run cycle step, 0..3.</param>
/// <param name="Tail">Tail sway variant, 0..2.</param>
/// <param name="EffectFrame">Animation step of <paramref name="Effect"/>.</param>
/// <param name="BodyDx">Whole-cat horizontal nudge in pixels (wiggles, purr jitter).</param>
public sealed record CatPose(
    BodyShape Body,
    int LegPhase = 0,
    int Tail = 0,
    EyeState Eyes = EyeState.Open,
    MouthState Mouth = MouthState.Smile,
    PawState Paws = PawState.Down,
    int HeadDx = 0,
    int HeadDy = 0,
    Effect Effect = Effect.None,
    int EffectFrame = 0,
    int BodyDx = 0,
    int BodyDy = 0);

/// <summary>Per-frame render options that come from the live world rather than the animation.</summary>
public readonly record struct RenderOptions(
    bool FacingLeft = false,
    int PupilDx = 0,
    int PupilDy = 0,
    bool Overheated = false);
