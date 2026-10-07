namespace Pixel.Core.Art;

/// <summary>
/// Draws the cat into a 48×40 canvas, facing right, feet on the bottom rows.
/// Order: tail → far limbs → body → near limbs → head → rim shading → face → outline
/// → attached effects → mirror → floating effects (text must never be mirrored).
/// </summary>
public static class CatRenderer
{
    public const int Width = 48;
    public const int Height = 40;

    internal const int HeadW = 15;

    /// <summary>Front-facing head with ears. Eyes, nose and mouth are drawn procedurally.</summary>
    internal static readonly string[] Head =
    [
        ".f...........f.",
        ".ff.........ff.",
        ".fpf.......fpf.",
        ".fppfffffffppf.",
        "fffffffffffffff",
        "fffffffffffffff",
        "fffffffffffffff",
        "fffffffffffffff",
        "fffffffffffffff",
        "fffffffffffffff",
        ".fffffffffffff.",
        "..sssssssssss..",
    ];

    public static uint[] Render(CatPose pose, Palette palette, RenderOptions opt = default)
    {
        var c = new PixelCanvas(Width, Height);
        var pal = opt.Overheated ? palette.Overheated() : palette;
        var pdx = Math.Clamp(opt.PupilDx, -1, 1) * (opt.FacingLeft ? -1 : 1);
        var pdy = Math.Clamp(opt.PupilDy, -1, 1);

        var head = DrawBody(c, pose);
        c.RimShade();
        if (head is (int hx, int hy))
        {
            DrawFace(c, hx, hy, pose, pdx, pdy);
            DrawPawOverFace(c, hx, hy, pose);
        }
        c.Outline();
        Effects.DrawAttached(c, pose);

        if (opt.FacingLeft) c.FlipX();
        (int X, int Y)? headOnScreen = head is (int x, int y)
            ? (opt.FacingLeft ? Width - x - HeadW : x, y)
            : null;
        Effects.DrawFloating(c, pose, headOnScreen);

        return c.ToArgb(pal);
    }

    // ---------------------------------------------------------------- bodies

    private static (int X, int Y)? DrawBody(PixelCanvas c, CatPose p) => p.Body switch
    {
        BodyShape.Stand => Stand(c, p),
        BodyShape.Sit => Sit(c, p),
        BodyShape.Loaf => Loaf(c, p),
        BodyShape.Crouch => Crouch(c, p),
        BodyShape.Stretch => Stretch(c, p),
        BodyShape.Jump => Jump(c, p),
        BodyShape.Fall => Fall(c, p),
        BodyShape.Dangle => Dangle(c, p),
        BodyShape.Peek => Peek(c, p),
        _ => null,
    };

    private static (int, int) Stand(PixelCanvas c, CatPose p)
    {
        int ox = p.BodyDx, oy = p.BodyDy;
        // back-near, back-far, front-near, front-far x offsets, near/far lift
        var (bn, bf, fn, ff, liftN, liftF) = p.LegPhase switch
        {
            0 => (-1, 1, 1, -1, 0, 0),
            1 => (0, 0, 0, 0, 1, 0),
            2 => (1, -1, -1, 1, 0, 0),
            3 => (0, 0, 0, 0, 0, 1),
            4 => (-3, -2, 3, 2, 1, 1), // gallop: stretched
            5 => (2, 1, -2, -1, 0, 0), // gallop: gathered
            _ => (0, 0, 0, 0, 0, 0),
        };
        var bob = p.LegPhase is 1 or 3 or 5 ? -1 : 0;

        c.Path(Ix.Fur, ox, oy + bob, TailStand(p.Tail));
        c.Rect(15 + bf + ox, 32 + oy, 16 + bf + ox, 38 - liftF + oy, Ix.Shade);
        c.Rect(30 + ff + ox, 32 + oy, 31 + ff + ox, 38 - liftF + oy, Ix.Shade);
        c.Ellipse(22 + ox, 30 + bob + oy, 10.5, 4.6, Ix.Fur);
        c.Rect(12 + bn + ox, 32 + oy, 14 + bn + ox, 38 - liftN + oy, Ix.Fur);
        c.Rect(27 + fn + ox, 32 + oy, 29 + fn + ox, 38 - liftN + oy, Ix.Fur);

        var hx = 27 + ox + p.HeadDx;
        var hy = 17 + bob + oy + p.HeadDy;
        c.Blit(Head, hx, hy);
        return (hx, hy);
    }

    private static (int X, int Y)[] TailStand(int v) => v switch
    {
        1 => [(12, 28), (9, 25), (9, 21), (11, 18), (13, 18)],
        2 => [(12, 28), (8, 27), (6, 23), (6, 19), (8, 17)],
        _ => [(12, 28), (9, 26), (8, 22), (9, 19), (11, 18)],
    };

    private static (int, int) Sit(PixelCanvas c, CatPose p)
    {
        int ox = p.BodyDx, oy = p.BodyDy;
        (int X, int Y)[] tail = p.Tail switch
        {
            1 => [(17, 37), (12, 37), (8, 35), (7, 31), (9, 29)],
            2 => [(17, 37), (12, 37), (8, 36), (6, 34), (5, 31)],
            _ => [(17, 37), (12, 37), (8, 36), (7, 32), (8, 30)],
        };
        c.Path(Ix.Fur, ox, oy, tail);

        var farLift = p.Paws == PawState.RightUp ? 2 : 0;
        c.Rect(30 + ox, 30 + oy, 31 + ox, 38 - farLift + oy, Ix.Shade);

        c.Ellipse(20 + ox, 33 + oy, 7, 5, Ix.Fur);       // haunch
        c.Ellipse(25 + ox, 29 + oy, 6, 7.5, Ix.Fur);     // chest
        c.Ellipse(16 + ox, 37.5 + oy, 3, 1.2, Ix.Fur);   // back paw

        switch (p.Paws)
        {
            case PawState.WaveUp or PawState.WaveDown:
                var px = p.Paws == PawState.WaveUp ? 34 : 32;
                c.Thick(27 + ox, 29 + oy, px + ox, 22 + oy, Ix.Fur);
                c.Ellipse(px + 1 + ox, 21 + oy, 1.6, 1.6, Ix.Fur);
                break;
            case PawState.Groom:
                break; // drawn over the face later
            default:
                var nearLift = p.Paws == PawState.LeftUp ? 2 : 0;
                c.Rect(26 + ox, 30 + oy, 28 + ox, 38 - nearLift + oy, Ix.Fur);
                break;
        }

        var hx = 19 + ox + p.HeadDx;
        var hy = 11 + oy + p.HeadDy;
        c.Blit(Head, hx, hy);
        return (hx, hy);
    }

    private static (int, int) Loaf(PixelCanvas c, CatPose p)
    {
        int ox = p.BodyDx, oy = p.BodyDy;
        c.Ellipse(23 + ox, 33.5 + oy, 12, 5 + 0.5 * (p.LegPhase & 1), Ix.Fur);
        c.Path(Ix.Fur, ox, oy, [(11, 34), (12, 37), (20, 37), (29, 37)]);
        c.Ellipse(37 + ox, 37.5 + oy, 2.5, 1.2, Ix.Fur);

        var hx = 26 + ox + p.HeadDx;
        var hy = 23 + oy + p.HeadDy;
        c.Blit(Head, hx, hy);
        return (hx, hy);
    }

    private static (int, int) Crouch(PixelCanvas c, CatPose p)
    {
        int ox = p.BodyDx, oy = p.BodyDy;
        var wiggle = p.LegPhase switch { 1 => 1, 3 => -1, _ => 0 };
        c.Path(Ix.Fur, ox + wiggle, oy, [(12, 33), (8, 32), (4, 30 + p.Tail % 2)]);
        c.Rect(17 + ox + wiggle, 34 + oy, 18 + ox + wiggle, 38 + oy, Ix.Shade);
        c.Ellipse(17 + ox + wiggle, 31.5 + oy, 6.5, 5, Ix.Fur);
        c.Ellipse(26 + ox, 34.5 + oy, 8, 3.6, Ix.Fur);
        c.Rect(12 + ox + wiggle, 34 + oy, 14 + ox + wiggle, 38 + oy, Ix.Fur);
        c.Rect(31 + ox, 37 + oy, 37 + ox, 38 + oy, Ix.Fur);

        var hx = 28 + ox + p.HeadDx;
        var hy = 24 + oy + p.HeadDy;
        c.Blit(Head, hx, hy);
        return (hx, hy);
    }

    private static (int, int) Stretch(PixelCanvas c, CatPose p)
    {
        int ox = p.BodyDx, oy = p.BodyDy;
        c.Path(Ix.Fur, ox, oy, [(11, 25), (9, 20), (8, 15), (10, 12)]);
        c.Rect(18 + ox, 30 + oy, 19 + ox, 38 + oy, Ix.Shade);
        c.Rect(31 + ox, 36 + oy, 42 + ox, 37 + oy, Ix.Shade);
        c.Ellipse(16 + ox, 27.5 + oy, 6, 5, Ix.Fur);
        c.Ellipse(27 + ox, 34 + oy, 8, 3.5, Ix.Fur);
        c.Rect(13 + ox, 30 + oy, 15 + ox, 38 + oy, Ix.Fur);
        c.Rect(30 + ox, 37 + oy, 41 + ox, 38 + oy, Ix.Fur);

        var hx = 31 + ox + p.HeadDx;
        var hy = 25 + oy + p.HeadDy;
        c.Blit(Head, hx, hy);
        return (hx, hy);
    }

    private static (int, int) Jump(PixelCanvas c, CatPose p)
    {
        int ox = p.BodyDx, oy = p.BodyDy;
        c.Path(Ix.Fur, ox, oy, [(12, 24), (7, 22), (3, 20)]);
        c.Thick(16 + ox, 29 + oy, 10 + ox, 34 + oy, Ix.Shade);
        c.Thick(29 + ox, 27 + oy, 35 + ox, 29 + oy, Ix.Shade);
        c.Ellipse(21 + ox, 26 + oy, 10, 4.5, Ix.Fur);
        c.Thick(14 + ox, 29 + oy, 8 + ox, 33 + oy, Ix.Fur);
        c.Thick(28 + ox, 28 + oy, 34 + ox, 31 + oy, Ix.Fur);

        var hx = 27 + ox + p.HeadDx;
        var hy = 13 + oy + p.HeadDy;
        c.Blit(Head, hx, hy);
        return (hx, hy);
    }

    private static (int, int) Fall(PixelCanvas c, CatPose p)
    {
        int ox = p.BodyDx, oy = p.BodyDy;
        c.Path(Ix.Fur, ox, oy, [(12, 24), (9, 20), (8, 15)]);
        c.Thick(17 + ox, 29 + oy, 16 + ox, 35 + oy, Ix.Shade);
        c.Thick(25 + ox, 29 + oy, 26 + ox, 35 + oy, Ix.Shade);
        c.Ellipse(21 + ox, 26 + oy, 9, 4.5, Ix.Fur);
        c.Thick(14 + ox, 29 + oy, 11 + ox, 35 + oy, Ix.Fur);
        c.Thick(28 + ox, 29 + oy, 31 + ox, 35 + oy, Ix.Fur);

        var hx = 25 + ox + p.HeadDx;
        var hy = 14 + oy + p.HeadDy;
        c.Blit(Head, hx, hy);
        return (hx, hy);
    }

    private static (int, int) Dangle(PixelCanvas c, CatPose p)
    {
        int ox = p.BodyDx, oy = p.BodyDy;
        var sway = p.Tail switch { 1 => 2, 2 => -2, _ => 0 };
        c.Path(Ix.Fur, ox, oy, [(24, 29), (24 + sway / 2, 34), (24 + sway, 38)]);
        c.Rect(27 + ox, 24 + oy, 28 + ox, 32 + oy, Ix.Shade);
        c.Ellipse(24 + ox, 22 + oy, 6, 8, Ix.Fur);
        c.Rect(20 + ox, 24 + oy, 22 + ox, 32 + oy, Ix.Fur);
        c.Rect(19 + ox, 28 + oy, 21 + ox, 35 + oy, Ix.Fur);
        c.Rect(27 + ox, 28 + oy, 29 + ox, 35 + oy, Ix.Fur);

        var hx = 17 + ox + p.HeadDx;
        var hy = 3 + oy + p.HeadDy;
        c.Blit(Head, hx, hy);
        return (hx, hy);
    }

    private static (int, int) Peek(PixelCanvas c, CatPose p)
    {
        int ox = p.BodyDx, oy = p.BodyDy;
        var hx = 16 + ox + p.HeadDx;
        var hy = 27 + oy + p.HeadDy;
        c.Blit(Head, hx, hy);
        c.Ellipse(14 + ox, 37.5 + oy, 2.5, 1.3, Ix.Fur);
        c.Ellipse(33 + ox, 37.5 + oy, 2.5, 1.3, Ix.Fur);
        return (hx, hy);
    }

    // ---------------------------------------------------------------- face

    private static void DrawFace(PixelCanvas c, int hx, int hy, CatPose p, int pdx, int pdy)
    {
        Eye(c, hx + 3, hy + 6, p.Eyes, pdx, pdy);
        Eye(c, hx + 9, hy + 6, p.Eyes, pdx, pdy);
        c.Set(hx + 7, hy + 9, Ix.Nose);
        c.Set(hx + 6, hy + 10, Ix.Line);
        c.Set(hx + 8, hy + 10, Ix.Line);
        if (p.Mouth == MouthState.Open)
        {
            c.Set(hx + 7, hy + 10, Ix.Nose);
            c.Set(hx + 7, hy + 11, Ix.Line);
        }
    }

    private static void Eye(PixelCanvas c, int ex, int ey, EyeState s, int pdx, int pdy)
    {
        switch (s)
        {
            case EyeState.Open:
            {
                c.Rect(ex, ey, ex + 2, ey + 2, Ix.Iris);
                // Vertical slit pupil: full height when looking level, shifted when looking up/down.
                var px = ex + 1 + pdx;
                var top = pdy > 0 ? ey + 1 : ey;
                var bottom = pdy < 0 ? ey + 1 : ey + 2;
                c.Rect(px, top, px, bottom, Ix.Pupil);
                c.Set(pdx < 0 ? ex + 2 : ex, ey, Ix.Shine);
                break;
            }
            case EyeState.Wide:
            {
                c.Rect(ex, ey, ex + 2, ey + 2, Ix.Iris);
                c.Set(ex + 1 + pdx, ey + 1 + pdy, Ix.Pupil);
                c.Set(pdx < 0 ? ex + 2 : ex, pdy < 0 ? ey + 2 : ey, Ix.Shine);
                break;
            }
            case EyeState.Half:
                c.Rect(ex, ey, ex + 2, ey, Ix.Line);
                c.Rect(ex, ey + 1, ex + 2, ey + 2, Ix.Iris);
                c.Set(ex + 1 + pdx, ey + 1, Ix.Pupil);
                c.Set(ex + 1 + pdx, ey + 2, Ix.Pupil);
                break;
            case EyeState.Closed:
                c.Set(ex, ey + 1, Ix.Line);
                c.Set(ex + 1, ey + 2, Ix.Line);
                c.Set(ex + 2, ey + 1, Ix.Line);
                break;
            case EyeState.Happy:
                c.Set(ex, ey + 2, Ix.Line);
                c.Set(ex + 1, ey + 1, Ix.Line);
                c.Set(ex + 2, ey + 2, Ix.Line);
                break;
        }
    }

    /// <summary>Grooming paw is raised in front of the mouth, so it is drawn after the face.</summary>
    private static void DrawPawOverFace(PixelCanvas c, int hx, int hy, CatPose p)
    {
        if (p.Body != BodyShape.Sit || p.Paws != PawState.Groom) return;
        c.Thick(27 + p.BodyDx, 31 + p.BodyDy, hx + 10, hy + 12, Ix.Fur);
        c.Ellipse(hx + 10.5, hy + 10.5, 1.6, 1.6, Ix.Highlight);
        c.Set(hx + 10, hy + 11, Ix.Nose);
    }
}
