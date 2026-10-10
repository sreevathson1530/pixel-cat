namespace Pixel.Core.Art;

/// <summary>
/// Props and particles around the cat. "Attached" props (laptop, keyboard, paper) live in cat space and are
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

    public static void DrawAttached(PixelCanvas c, CatPose p)
    {
        var f = p.EffectFrame;
        var front = new PixelCanvas(W, H);
        var behind = new PixelCanvas(W, H);

        if (p.Body == BodyShape.Front && p.Laptop != LaptopStage.None) Laptop(front, p, f);

        if (p.Effect.HasFlag(Effect.Paper))
        {
            var len = 4 + (((f % 4) + 4) % 4) * 4;
            behind.Rect(32, 37, Math.Min(W - 2, 32 + len), 38, Ix.FxPaper);
            for (var x = 34; x <= Math.Min(W - 2, 32 + len); x += 3) behind.Set(x, 37, Ix.FxPaperLine);
            front.Rect(32, 31, 33, 36, Ix.FxPaper);
            front.Ellipse(31, 30, 2.2, 2.2, Ix.FxPaper);
            front.Set(31, 30, Ix.FxPaperLine);
        }

        InkOutline(behind);
        InkOutline(front);
        for (var i = 0; i < c.Px.Length; i++)
        {
            if (front.Px[i] != Ix.Clear) c.Px[i] = front.Px[i];
            else if (behind.Px[i] != Ix.Clear && c.Px[i] == Ix.Clear) c.Px[i] = behind.Px[i];
        }
    }

    /// <summary>
    /// The laptop routine, centred on the cat (x = 24): reach into the pocket, pull the laptop out, set it down,
    /// open the lid, type. Paws are drawn here too so they sit on top of the laptop.
    /// </summary>
    private static void Laptop(PixelCanvas l, CatPose p, int f)
    {
        int ox = p.BodyDx, oy = p.BodyDy;
        void Rect(int x0, int y0, int x1, int y1, byte v) => l.Rect(x0 + ox, y0 + oy, x1 + ox, y1 + oy, v);
        void Paw(int x, int y)
        {
            Rect(x, y, x + 2, y + 1, Ix.Fur);
            Rect(x, y, x + 2, y, Ix.Highlight);
        }

        switch (p.Laptop)
        {
            case LaptopStage.PawInPocket:
                Paw(25, 30); // dipping into the pocket
                l.Set(29 + ox, 28 + oy, Ix.FxYellow);
                l.Set(19 + ox, 27 + oy, Ix.FxYellow);
                break;

            case LaptopStage.Emerging:
                Rect(20, 25, 28, 31, Ix.FxLaptop); // half out of the pocket, still closed
                Rect(20, 25, 28, 25, Ix.FxLaptopDark);
                Paw(23, 23);
                l.Set(30 + ox, 24 + oy, Ix.FxYellow);
                break;

            case LaptopStage.HeldUp:
                Rect(16, 28, 32, 31, Ix.FxLaptop); // held out in front with both paws
                Rect(16, 31, 32, 31, Ix.FxLaptopDark);
                Paw(13, 28);
                Paw(33, 28);
                break;

            case LaptopStage.Closed:
                Rect(13, 36, 35, 36, Ix.FxLaptopDark); // closed lid resting on the deck
                Rect(13, 37, 35, 37, Ix.FxLaptop);
                Rect(13, 38, 35, 38, Ix.FxLaptopDark);
                Paw(18, 34);
                Paw(28, 34);
                break;

            case LaptopStage.Half:
                Rect(13, 37, 35, 37, Ix.FxLaptop);
                Rect(13, 38, 35, 38, Ix.FxLaptopDark);
                Rect(15, 31, 33, 36, Ix.FxLaptop); // lid swinging up
                Rect(15, 31, 33, 31, Ix.FxLaptopDark);
                Paw(16, 35);
                Paw(30, 35);
                break;

            case LaptopStage.Open:
            {
                Rect(13, 37, 35, 37, Ix.FxLaptop); // keyboard deck
                Rect(13, 38, 35, 38, Ix.FxLaptopDark);
                Rect(15, 26, 33, 36, Ix.FxLaptop); // silver back of the lid, facing us — dark paws show up on it
                Rect(15, 27, 15, 36, Ix.FxLaptopDark);
                Rect(33, 27, 33, 36, Ix.FxLaptopDark);
                for (var x = 16; x <= 32; x++) // screen light spilling over the top edge
                    l.Set(x + ox, 26 + oy, (x + f) % 3 == 0 ? Ix.FxCode : Ix.FxScreen);
                l.Set(22 + ox, 29 + oy, Ix.FxWhite); // paw-print logo
                l.Set(24 + ox, 28 + oy, Ix.FxWhite);
                l.Set(26 + ox, 29 + oy, Ix.FxWhite);
                Rect(23, 30, 25, 31, Ix.FxWhite);

                // Typing: paws on the keys, one hops up at a time.
                var leftUp = p.Paws == PawState.LeftUp;
                var rightUp = p.Paws == PawState.RightUp;
                Paw(18, leftUp ? 33 : 35);
                Paw(28, rightUp ? 33 : 35);
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

    /// <summary>Speech bubble 9×10 (+1px tail) holding a 7×8 glyph, clamped inside the canvas.</summary>
    private static void Bubble(PixelCanvas c, int x, int y, string[] glyph)
    {
        x = Math.Clamp(x, 0, W - 9);
        y = Math.Clamp(y, 0, H - 11);
        c.Rect(x + 1, y, x + 7, y, Ix.FxInk);
        c.Rect(x + 1, y + 9, x + 7, y + 9, Ix.FxInk);
        c.Rect(x, y + 1, x, y + 8, Ix.FxInk);
        c.Rect(x + 8, y + 1, x + 8, y + 8, Ix.FxInk);
        c.Rect(x + 1, y + 1, x + 7, y + 8, Ix.FxWhite);
        c.Blit(glyph, x + 1, y + 1);
        c.Set(x + 1, y + 10, Ix.FxInk);
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
