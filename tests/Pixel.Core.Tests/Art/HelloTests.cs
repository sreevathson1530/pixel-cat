using Pixel.Core.Art;
using Pixel.Core.Behaviour;
using Pixel.Core.Motion;

namespace Pixel.Core.Tests.Art;

public class HelloTests
{
    private const uint White = 0xFFFFFFFF;

    [Fact]
    public void Hello_waves_one_paw_a_few_times_then_stops()
    {
        var a = Animations.Get(AnimationId.Hello);
        Assert.False(a.Loop);
        Assert.True(a.Frames.Count(f => f.Pose.Paws == PawState.WaveUp) >= 2, "waves more than once");
        Assert.Contains(a.Frames, f => f.Pose.Paws == PawState.WaveDown);
        Assert.InRange(a.TotalMs, 1000, 2500);
        Assert.Equal(PawState.Down, a.Frames[^1].Pose.Paws); // paw back down at the end
    }

    [Fact]
    public void Hello_says_hi_in_a_speech_bubble()
    {
        var a = Animations.Get(AnimationId.Hello);
        Assert.Contains(a.Frames, f => f.Pose.Effect.HasFlag(Effect.Hi));
        var px = CatRenderer.Render(a.Frames[0].Pose, Palette.Named("black"));
        Assert.Contains(White, px);
    }

    [Fact]
    public void The_hi_bubble_reads_the_same_whichever_way_the_cat_faces()
    {
        // Text is drawn after mirroring, so the bubble's own pixels are never flipped.
        var pose = Animations.Get(AnimationId.Hello).Frames[0].Pose;
        var p = Palette.Named("black");
        static string BubbleRows(uint[] px)
        {
            var rows = new List<string>();
            for (var y = 0; y < CatRenderer.Height; y++)
            {
                var row = new char[CatRenderer.Width];
                for (var x = 0; x < CatRenderer.Width; x++) row[x] = px[y * CatRenderer.Width + x] == White ? 'w' : '.';
                var s = new string(row).Trim('.');
                if (s.Length > 0) rows.Add(s);
            }
            return string.Join("|", rows);
        }
        var right = BubbleRows(CatRenderer.Render(pose, p));
        var left = BubbleRows(CatRenderer.Render(pose, p, new RenderOptions(FacingLeft: true)));
        Assert.Equal(right, left);
    }

    [Fact]
    public void Coming_back_greets_you_with_a_wave()
    {
        var m = new MotionController(new Bounds(0, 0, 1280, 800)) { FreeRoam = true, BodyHeight = 120 };
        m.Place(600, 700);
        var d = new PetDirector(m, new Roamer(new Random(2)));
        d.Tick(0, 1 / 30.0, new PetSignals { Greeting = true }, [], 0);
        Assert.Equal(Activity.Greet, d.Activity);
        Assert.Equal(AnimationId.Hello, d.Animation);
        Assert.Equal(MotionMode.Idle, m.Mode); // stands still to wave
    }

    [Fact]
    public void Scrolling_has_no_paper_any_more()
    {
        Assert.DoesNotContain("Paper", Enum.GetNames<AnimationId>());
        Assert.DoesNotContain("Paper", Enum.GetNames<Activity>());
        Assert.DoesNotContain("Paper", Enum.GetNames<Effect>());
    }
}
