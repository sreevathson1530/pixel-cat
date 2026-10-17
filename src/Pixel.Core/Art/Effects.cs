namespace Pixel.Core.Art;

/// <summary>
/// Props and particles around the cat. "Attached" props (the laptop) live in cat space and are
/// mirrored with it; "floating" ones (bubbles, zZ, hearts…) are drawn after mirroring so text never flips.
/// Every effect gets an ink outline so it stays readable on any wallpaper.
/// </summary>
internal static class Effects
{
    private const int W = CatRenderer.Width, H = CatRenderer.Height;

    private static readonly string[] ExclaimGlyph =
        ["..ii...", "..ii...", "..ii...", "..ii...", "..ii...", ".......", "..ii...", "..ii..."];

    private static readonly string[] QuestionGlyph =
        [".iiii..", "ii..ii.", "....ii.", "...ii..", "..ii...", ".......", "..ii...", "..ii..."];

    // "hi!" in a 1px font with a white margin: at this size thicker strokes, or letters touching the border, read as noise.
    private static readonly string[] HiGlyph =
        [".........", ".i...i.i.", ".i.....i.", ".iii.i.i.", ".i.i.i...", ".i.i.i.i.", "........."];

    public static void DrawAttached(PixelCanvas c, CatPose p)
    {
        var f = p.EffectFrame;
        var front = new PixelCanvas(W, H);
        var behind = new PixelCanvas(W, H);

        if (p.Body == BodyShape.Sit && p.Laptop != LaptopStage.None) Laptop(front, p, f);

        InkOutline(behind);
        InkOutline(front);
        for (var i = 0; i < c.Px.Length; i++)
        {
            if (front.Px[i] != Ix.Clear) c.Px[i] = front.Px[i];
            else if (behind.Px[i] != Ix.Clear && c.Px[i] == Ix.Clear) c.Px[i] = behind.Px[i];
        }
    }

    /// <summary>
    /// The laptop routine, side-on: reach into the chest pocket, pull the laptop out, set it on the floor right in
    /// front of the cat (its base touching the chest), lift the lid at the far end so the screen faces the cat,
    /// then type with both front paws. Arms and paws are drawn here so they sit on top of the laptop.
    /// </summary>
    private static void Laptop(PixelCanvas l, CatPose p, int f)
    {
        int ox = p.BodyDx, oy = p.BodyDy;
        void Rect(int x0, int y0, int x1, int y1, byte v) => l.Rect(x0 + ox, y0 + oy, x1 + ox, y1 + oy, v);
        void Set(int x, int y, byte v) => l.Set(x + ox, y + oy, v);
        void Arm(int x0, int y0, int x1, int y1, byte v) => l.Thick(x0 + ox, y0 + oy, x1 + ox, y1 + oy, v);
        void Paw(int x, int y, byte fur)
        {
            Rect(x, y, x + 2, y + 1, fur);
            if (fur == Ix.Fur) Rect(x, y, x + 2, y, Ix.Highlight);
        }
        void Deck()
        {
            Rect(30, 37, 44, 37, Ix.FxLaptop);
            Rect(30, 38, 44, 38, Ix.FxLaptopDark);
            for (var x = 32; x <= 42; x += 2) Set(x, 37, Ix.FxKey); // keys
        }

        switch (p.Laptop)
        {
            case LaptopStage.PawInPocket:
                Arm(27, 32, 27, 29, Ix.Fur); // paw dipping into the chest pocket
                Paw(26, 28, Ix.Fur);
                Set(32, 26, Ix.FxYellow);
                Set(23, 25, Ix.FxYellow);
                break;

            case LaptopStage.Emerging:
                Rect(28, 27, 36, 29, Ix.FxLaptop); // closed laptop sliding out of the pocket
                Rect(28, 29, 36, 29, Ix.FxLaptopDark);
                Arm(27, 31, 33, 27, Ix.Fur);
                Paw(33, 25, Ix.Fur);
                Set(38, 25, Ix.FxYellow);
                break;

            case LaptopStage.HeldUp:
                Rect(31, 27, 44, 29, Ix.FxLaptop); // held out in front, ready to set down
                Rect(31, 29, 44, 29, Ix.FxLaptopDark);
                Arm(27, 31, 31, 28, Ix.Fur);
                Paw(30, 26, Ix.Fur);
                break;

            case LaptopStage.Closed:
                Rect(30, 36, 44, 36, Ix.FxLaptopDark); // closed lid lying on the base
                Rect(30, 37, 44, 37, Ix.FxLaptop);
                Rect(30, 38, 44, 38, Ix.FxLaptopDark);
                Arm(27, 31, 31, 34, Ix.Fur);
                Paw(31, 34, Ix.Fur);
                break;

            case LaptopStage.Half:
                Deck();
                Arm(43, 36, 37, 30, Ix.FxLaptop); // lid swinging up from the hinge at the far end
                Arm(27, 31, 31, 34, Ix.Fur);
                Paw(31, 35, Ix.Fur);
                break;

            case LaptopStage.Open:
            {
                Deck();
                // Lid stands up at the far end, leaning back a little; the screen side faces the cat and glows.
                for (var y = 26; y <= 36; y++)
                {
                    var x = 44 + (36 - y) * 2 / 10;
                    Set(x, y, Ix.FxLaptop);
                    Set(x + 1, y, Ix.FxLaptopDark);
                    if (y is > 26 and < 36) Set(x - 1, y, (y + f) % 3 == 0 ? Ix.FxCode : Ix.FxScreen);
                }

                // Typing: near paw (fur) and far paw (shade) on the keys, one hopping up at a time.
                var nearUp = p.Paws == PawState.LeftUp;
                var farUp = p.Paws == PawState.RightUp;
                Arm(29, 30, 35, farUp ? 33 : 35, Ix.Shade);
                Paw(35, farUp ? 33 : 35, Ix.Shade);
                Arm(27, 31, 31, nearUp ? 33 : 35, Ix.Fur);
                Paw(31, nearUp ? 33 : 35, Ix.Fur);
                break;
            }
        }
    }

    public static void DrawFloating(PixelCanvas c, CatPose p, (int X, int Y)? head)
    {
        var f = ((p.EffectFrame % 4) + 4) % 4;
        var fx = new PixelCanvas(W, H);

        if (p.Effect.HasFlag(Effect.Poof)) Poof(fx, f);

        if (head is (int hx, int hy))
        {
            if (p.Effect.HasFlag(Effect.Exclaim)) Bubble(c, hx + 12, hy - 9, ExclaimGlyph);
            if (p.Effect.HasFlag(Effect.Question)) Bubble(c, hx + 12, hy - 9, QuestionGlyph);
            if (p.Effect.HasFlag(Effect.Hi)) Bubble(c, hx - 12, hy - 10, HiGlyph, tailRight: true); // up-left, clear of the waving paw
            if (p.Effect.HasFlag(Effect.Dots)) Dots(c, hx + 11, hy - 3, f);

            if (p.Effect.HasFlag(Effect.Zz))
            {
                Glyph(fx, hx + 13 + f / 2, hy + 1 - f * 2, Ix.FxZ, ["zzz", ".z.", "zzz"]);
                if (f >= 2) Glyph(fx, hx + 15, hy - 4 - (f - 2) * 2, Ix.FxZ, ["zzzz", "..z.", ".z..", "zzzz"]);
            }

            if (p.Effect.HasFlag(Effect.Hearts))
            {
                Glyph(fx, hx + 12, hy - 1 - f * 2, Ix.FxHeart, [".z.z.", "zzzzz", ".zzz.", "..z.."]);
                Glyph(fx, hx - 3, hy + 3 - ((f + 2) % 4) * 2, Ix.FxHeart, ["z.z", "zzz", ".z."]);
            }

            if (p.Effect.HasFlag(Effect.Stars))
            {
                // Three little stars circling just above the head; the ring turns a quarter each frame.
                var cx = hx + CatRenderer.HeadW / 2.0;
                var cy = hy - 1.5;
                for (var i = 0; i < 3; i++)
                {
                    var a = (f / 4.0 + i / 3.0) * 2 * Math.PI;
                    var sx = (int)Math.Round(cx + Math.Cos(a) * 9) - 1;
                    var sy = (int)Math.Round(cy + Math.Sin(a) * 2.5) - 1;
                    Glyph(fx, sx, sy, Ix.FxYellow, [".z.", "zzz", ".z."]);
                }
            }

            if (p.Effect.HasFlag(Effect.Steam))
            {
                for (var i = 0; i < 3; i++)
                {
                    var px = hx + 2 + i * 5;
                    var py = hy - 2 - ((f + i) % 3) * 3;
                    Glyph(fx, px, py, Ix.FxSteam, [".z.", "zzz", ".z."]);
                }
            }
        }

        if (p.Effect.HasFlag(Effect.Sparkles))
        {
            (int X, int Y)[] spots = [(5, 8), (41, 5), (3, 24), (43, 22), (24, 1)];
            for (var i = 0; i < spots.Length; i++)
            {
                var (sx, sy) = spots[i];
                switch ((f + i) % 3)
                {
                    case 1:
                        Glyph(fx, sx - 1, sy - 1, Ix.FxYellow, [".z.", "zzz", ".z."]);
                        break;
                    case 2:
                        Glyph(fx, sx - 2, sy - 2, Ix.FxYellow, ["..z..", "..z..", "zzzzz", "..z..", "..z.."]);
                        fx.Set(sx, sy, Ix.FxWhite);
                        break;
                }
            }
        }

        InkOutline(fx);
        for (var i = 0; i < c.Px.Length; i++)
            if (fx.Px[i] != Ix.Clear) c.Px[i] = fx.Px[i];
    }

    private static void Poof(PixelCanvas fx, int f)
    {
        const double cx = 24, cy = 28;
        if (f == 0)
        {
            fx.Ellipse(cx, cy, 6, 5, Ix.FxCloud);
            fx.Ellipse(cx - 4, cy + 2, 3, 2.5, Ix.FxCloud);
            fx.Ellipse(cx + 4, cy + 2, 3, 2.5, Ix.FxCloud);
            return;
        }
        var r = 5 + f * 4;
        var pr = Math.Max(1.2, 3.6 - f * 0.8);
        for (var k = 0; k < 8; k++)
        {
            var a = k * Math.PI / 4;
            fx.Ellipse(cx + Math.Cos(a) * r, cy + Math.Sin(a) * r * 0.7, pr, pr, Ix.FxCloud);
        }
    }

    /// <summary>Speech bubble around a glyph (1px border, +1px tail on the left or right), clamped inside the canvas.</summary>
    private static void Bubble(PixelCanvas c, int x, int y, string[] glyph, bool tailRight = false)
    {
        int gw = glyph[0].Length, gh = glyph.Length;
        x = Math.Clamp(x, 0, W - (gw + 2));
        y = Math.Clamp(y, 0, H - (gh + 3));
        c.Rect(x + 1, y, x + gw, y, Ix.FxInk);
        c.Rect(x + 1, y + gh + 1, x + gw, y + gh + 1, Ix.FxInk);
        c.Rect(x, y + 1, x, y + gh, Ix.FxInk);
        c.Rect(x + gw + 1, y + 1, x + gw + 1, y + gh, Ix.FxInk);
        c.Rect(x + 1, y + 1, x + gw, y + gh, Ix.FxWhite);
        c.Blit(glyph, x + 1, y + 1);
        c.Set(tailRight ? x + gw : x + 1, y + gh + 2, Ix.FxInk);
    }

    /// <summary>"Thinking" bubble with three dots; the active dot cycles with the frame.</summary>
    private static void Dots(PixelCanvas c, int x, int y, int f)
    {
        x = Math.Clamp(x, 0, W - 11);
        y = Math.Clamp(y, 0, H - 5);
        c.Rect(x + 1, y, x + 9, y, Ix.FxInk);
        c.Rect(x + 1, y + 4, x + 9, y + 4, Ix.FxInk);
        c.Rect(x, y + 1, x, y + 3, Ix.FxInk);
        c.Rect(x + 10, y + 1, x + 10, y + 3, Ix.FxInk);
        c.Rect(x + 1, y + 1, x + 9, y + 3, Ix.FxWhite);
        for (var i = 0; i < 3; i++) c.Set(x + 3 + i * 2, y + 2, i == f % 3 ? Ix.FxInk : Ix.FxSteam);
    }

    private static void Glyph(PixelCanvas fx, int x, int y, byte colour, string[] grid)
    {
        for (var r = 0; r < grid.Length; r++)
        for (var col = 0; col < grid[r].Length; col++)
            if (grid[r][col] != '.') fx.Set(x + col, y + r, colour);
    }

    private static void InkOutline(PixelCanvas layer)
    {
        var src = (byte[])layer.Px.Clone();
        bool On(int x, int y) => (uint)x < W && (uint)y < H && src[y * W + x] != Ix.Clear && src[y * W + x] != Ix.FxInk;
        for (var y = 0; y < H; y++)
        for (var x = 0; x < W; x++)
            if (src[y * W + x] == Ix.Clear && (On(x - 1, y) || On(x + 1, y) || On(x, y - 1) || On(x, y + 1)))
                layer.Set(x, y, Ix.FxInk);
    }
}
