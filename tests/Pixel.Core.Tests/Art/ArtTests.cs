using Pixel.Core.Art;

namespace Pixel.Core.Tests.Art;

public class PngWriterTests
{
    [Fact]
    public void Writes_png_signature_and_ihdr_dimensions()
    {
        using var ms = new MemoryStream();
        PngWriter.Write(ms, 3, 2, new uint[6]);
        var b = ms.ToArray();

        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, b[..8]);
        Assert.Equal("IHDR", System.Text.Encoding.ASCII.GetString(b, 12, 4));
        Assert.Equal(3, (b[16] << 24) | (b[17] << 16) | (b[18] << 8) | b[19]);
        Assert.Equal(2, (b[20] << 24) | (b[21] << 16) | (b[22] << 8) | b[23]);
    }
}

public class CatRendererTests
{
    private static readonly CatPose Sit = new(BodyShape.Sit);

    [Fact]
    public void Render_returns_full_canvas()
    {
        var px = CatRenderer.Render(Sit, Palette.Named("black"));
        Assert.Equal(CatRenderer.Width * CatRenderer.Height, px.Length);
    }

    [Fact]
    public void Canvas_corners_are_transparent_and_cat_is_opaque()
    {
        var px = CatRenderer.Render(Sit, Palette.Named("black"));
        Assert.Equal(0u, px[0]);
        Assert.Equal(0u, px[^1]);
        Assert.Contains(px, p => p >> 24 == 0xFF);
    }

    [Fact]
    public void Palette_swap_changes_colours_but_not_silhouette()
    {
        var black = CatRenderer.Render(Sit, Palette.Named("black"));
        var orange = CatRenderer.Render(Sit, Palette.Named("orange"));

        Assert.Equal(black.Select(p => p != 0), orange.Select(p => p != 0));
        Assert.Contains(Palette.Named("orange").Fur, orange);
        Assert.DoesNotContain(Palette.Named("orange").Fur, black);
    }

    [Fact]
    public void Facing_left_is_mirror_of_facing_right()
    {
        var pose = new CatPose(BodyShape.Stand);
        var right = CatRenderer.Render(pose, Palette.Named("black"));
        var left = CatRenderer.Render(pose, Palette.Named("black"), new RenderOptions(FacingLeft: true));

        for (var y = 0; y < CatRenderer.Height; y++)
        for (var x = 0; x < CatRenderer.Width; x++)
            Assert.Equal(right[y * CatRenderer.Width + x], left[y * CatRenderer.Width + (CatRenderer.Width - 1 - x)]);
    }

    [Fact]
    public void Pupil_offset_moves_pupils_and_is_clamped()
    {
        var p = Palette.Named("black");
        var lookLeft = CatRenderer.Render(Sit, p, new RenderOptions(PupilDx: -1));
        var lookRight = CatRenderer.Render(Sit, p, new RenderOptions(PupilDx: 1));
        var lookFarRight = CatRenderer.Render(Sit, p, new RenderOptions(PupilDx: 9));

        Assert.NotEqual(lookLeft, lookRight);
        Assert.Equal(lookRight, lookFarRight);
    }

    [Fact]
    public void Overheated_render_replaces_fur_colour()
    {
        var p = Palette.Named("black");
        var hot = CatRenderer.Render(Sit, p, new RenderOptions(Overheated: true));
        Assert.DoesNotContain(p.Fur, hot);
        Assert.Contains(p.Overheated().Fur, hot);
    }

    [Fact]
    public void Unknown_palette_name_falls_back_to_black()
    {
        Assert.Equal(Palette.Named("black"), Palette.Named("no-such-colour"));
    }
}

public class AnimationsTests
{
    [Fact]
    public void Every_animation_has_frames_with_positive_durations()
    {
        foreach (var id in Enum.GetValues<AnimationId>())
        {
            var anim = Animations.Get(id);
            Assert.Equal(id, anim.Id);
            Assert.NotEmpty(anim.Frames);
            Assert.All(anim.Frames, f => Assert.True(f.Ms > 0, $"{id} has a non-positive frame duration"));
        }
    }

    [Fact]
    public void Every_animation_frame_renders()
    {
        var p = Palette.Named("black");
        foreach (var id in Enum.GetValues<AnimationId>())
        foreach (var f in Animations.Get(id).Frames)
            Assert.Equal(CatRenderer.Width * CatRenderer.Height, CatRenderer.Render(f.Pose, p).Length);
    }
}

public class LaptopAnimationTests
{
    private const uint Silver = 0xFFA3ACBE;   // laptop body (FxLaptop)
    private const uint DarkEdge = 0xFF4A5164; // laptop edges (FxLaptopDark)

    private static List<(int X, int Y)> LaptopPixels(uint[] px) =>
        Enumerable.Range(0, px.Length).Where(i => px[i] is Silver or DarkEdge)
            .Select(i => (i % CatRenderer.Width, i / CatRenderer.Width)).ToList();

    private static uint[] TypingFrame(bool facingLeft) =>
        CatRenderer.Render(Animations.Get(AnimationId.Working).Frames[0].Pose, Palette.Named("black"),
            new RenderOptions(FacingLeft: facingLeft));

    [Fact]
    public void Typing_happens_side_on()
    {
        Assert.All(Animations.Get(AnimationId.Working).Frames, f => Assert.Equal(BodyShape.Sit, f.Pose.Body));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Laptop_sits_beside_the_cat_in_the_direction_it_faces(bool facingLeft)
    {
        var pts = LaptopPixels(TypingFrame(facingLeft));
        Assert.NotEmpty(pts);
        if (facingLeft) Assert.All(pts, p => Assert.True(p.X <= CatRenderer.Width - 1 - 27, $"laptop pixel at x={p.X} is behind a left-facing cat"));
        else Assert.All(pts, p => Assert.True(p.X >= 27, $"laptop pixel at x={p.X} is behind a right-facing cat"));
    }

    [Fact]
    public void Laptop_base_touches_the_cat_and_rests_on_the_floor()
    {
        var pts = LaptopPixels(TypingFrame(false));
        var baseRow = pts.Where(p => p.Y == 37).Select(p => p.X).ToList();
        Assert.NotEmpty(baseRow);
        Assert.InRange(baseRow.Min(), 29, 33);  // starts right at the cat's chest
        Assert.True(baseRow.Max() - baseRow.Min() >= 10, "a proper keyboard deck");
    }

    [Fact]
    public void Lid_stands_up_at_the_far_end()
    {
        var pts = LaptopPixels(TypingFrame(false));
        var top = pts.Min(p => p.Y);
        Assert.True(top <= 28, "open lid rises well above the deck");
        Assert.True(pts.Where(p => p.Y == top).All(p => p.X >= 40), "lid is at the far end of the laptop");
    }

    [Theory]
    [InlineData(AnimationId.Working)]
    [InlineData(AnimationId.Knead)]
    [InlineData(AnimationId.Overheat)]
    public void Typing_animations_type_on_an_open_laptop(AnimationId id) =>
        Assert.All(Animations.Get(id).Frames, f => Assert.Equal(LaptopStage.Open, f.Pose.Laptop));

    [Fact]
    public void Laptop_comes_out_of_the_pocket_and_opens()
    {
        var a = Animations.Get(AnimationId.LaptopOut);
        Assert.False(a.Loop);
        Assert.Equal(LaptopStage.PawInPocket, a.Frames[0].Pose.Laptop);
        Assert.Equal(LaptopStage.Open, a.Frames[^1].Pose.Laptop);
        Assert.All(a.Frames, f => Assert.Equal(BodyShape.Sit, f.Pose.Body));
    }

    [Fact]
    public void Laptop_goes_back_into_the_pocket()
    {
        var a = Animations.Get(AnimationId.LaptopAway);
        Assert.False(a.Loop);
        Assert.Equal(LaptopStage.Open, a.Frames[0].Pose.Laptop);
        Assert.Equal(LaptopStage.None, a.Frames[^1].Pose.Laptop);
    }

    [Fact]
    public void Paws_move_while_typing()
    {
        var frames = Animations.Get(AnimationId.Working).Frames;
        Assert.True(frames.Select(f => f.Pose.Paws).Distinct().Count() >= 2);
        var a = CatRenderer.Render(frames[0].Pose, Palette.Named("black"));
        var b = CatRenderer.Render(frames[1].Pose, Palette.Named("black"));
        Assert.NotEqual(a, b);
    }
}


public class WalkCycleTests
{
    private static uint[] Render(Frame f) => CatRenderer.Render(f.Pose, Palette.Named("black"));

    private static int TopRow(uint[] px) =>
        Enumerable.Range(0, px.Length).Where(i => px[i] >> 24 != 0).Min(i => i / CatRenderer.Width);

    /// <summary>Separate groups of opaque pixels on a row (feet touching the ground).</summary>
    private static int ContactsOnRow(uint[] px, int y)
    {
        var count = 0;
        var inside = false;
        for (var x = 0; x < CatRenderer.Width; x++)
        {
            var on = px[y * CatRenderer.Width + x] >> 24 != 0;
            if (on && !inside) count++;
            inside = on;
        }
        return count;
    }

    [Fact]
    public void Walk_is_a_smooth_eight_frame_cycle() =>
        Assert.Equal(8, Animations.Get(AnimationId.Walk).Frames.Count);

    [Fact]
    public void Body_stays_level_while_walking()
    {
        // No bobbing: the top of the head is at the same height in every frame (bobbing reads as hopping).
        var tops = Animations.Get(AnimationId.Walk).Frames.Select(f => TopRow(Render(f))).Distinct();
        Assert.Single(tops);
    }

    [Fact]
    public void At_least_two_paws_are_on_the_ground_in_every_frame()
    {
        foreach (var f in Animations.Get(AnimationId.Walk).Frames)
            Assert.True(ContactsOnRow(Render(f), 38) >= 2, $"frame with leg phase {f.Pose.LegPhase} looks airborne");
    }

    [Fact]
    public void Legs_actually_move_through_the_cycle()
    {
        var frames = Animations.Get(AnimationId.Walk).Frames.Select(Render).ToList();
        for (var i = 1; i < frames.Count; i++) Assert.NotEqual(frames[i - 1], frames[i]);
    }
}
