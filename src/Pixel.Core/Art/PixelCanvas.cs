namespace Pixel.Core.Art;

/// <summary>Indexed-colour drawing surface. Index 0 is transparent; see <see cref="Ix"/>.</summary>
internal sealed class PixelCanvas(int width, int height)
{
    public int Width { get; } = width;
    public int Height { get; } = height;
    public byte[] Px { get; } = new byte[width * height];

    public byte Get(int x, int y) => (uint)x < (uint)Width && (uint)y < (uint)Height ? Px[y * Width + x] : Ix.Clear;

    public void Set(int x, int y, byte v)
    {
        if ((uint)x < (uint)Width && (uint)y < (uint)Height) Px[y * Width + x] = v;
    }

    public void SetIfClear(int x, int y, byte v)
    {
        if (Get(x, y) == Ix.Clear) Set(x, y, v);
    }

    /// <summary>Inclusive rectangle.</summary>
    public void Rect(int x0, int y0, int x1, int y1, byte v)
    {
        for (var y = Math.Min(y0, y1); y <= Math.Max(y0, y1); y++)
        for (var x = Math.Min(x0, x1); x <= Math.Max(x0, x1); x++)
            Set(x, y, v);
    }

    public void Ellipse(double cx, double cy, double rx, double ry, byte v)
    {
        for (var y = (int)Math.Floor(cy - ry); y <= (int)Math.Ceiling(cy + ry); y++)
        for (var x = (int)Math.Floor(cx - rx); x <= (int)Math.Ceiling(cx + rx); x++)
        {
            var nx = (x - cx) / rx;
            var ny = (y - cy) / ry;
            if (nx * nx + ny * ny <= 1.0) Set(x, y, v);
        }
    }

    /// <summary>Line with a 2×2 brush (tails, limbs).</summary>
    public void Thick(int x0, int y0, int x1, int y1, byte v)
    {
        int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
        int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
        var err = dx + dy;
        while (true)
        {
            Rect(x0, y0, x0 + 1, y0 + 1, v);
            if (x0 == x1 && y0 == y1) break;
            var e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    public void Path(byte v, int ox, int oy, params (int X, int Y)[] pts)
    {
        for (var i = 1; i < pts.Length; i++)
            Thick(pts[i - 1].X + ox, pts[i - 1].Y + oy, pts[i].X + ox, pts[i].Y + oy, v);
    }

    /// <summary>Stamps a character grid; '.' is skipped. Map chars via <see cref="Ix.FromChar"/>.</summary>
    public void Blit(string[] grid, int x, int y)
    {
        for (var r = 0; r < grid.Length; r++)
        for (var c = 0; c < grid[r].Length; c++)
        {
            var v = Ix.FromChar(grid[r][c]);
            if (v != Ix.Clear) Set(x + c, y + r, v);
        }
    }

    /// <summary>Top-edge fur becomes highlight, bottom-edge fur becomes shade — gives volume to any silhouette.</summary>
    public void RimShade()
    {
        var src = (byte[])Px.Clone();
        byte At(int x, int y) => (uint)x < (uint)Width && (uint)y < (uint)Height ? src[y * Width + x] : Ix.Clear;
        for (var y = 0; y < Height; y++)
        for (var x = 0; x < Width; x++)
        {
            if (src[y * Width + x] != Ix.Fur) continue;
            if (At(x, y - 1) == Ix.Clear) Set(x, y, Ix.Highlight);
            else if (At(x, y + 1) == Ix.Clear) Set(x, y, Ix.Shade);
        }
    }

    /// <summary>Adds a 1px outline (4-neighbour) around every cat pixel.</summary>
    public void Outline()
    {
        var src = (byte[])Px.Clone();
        bool IsCat(int x, int y) =>
            (uint)x < (uint)Width && (uint)y < (uint)Height && src[y * Width + x] is > Ix.Clear and < Ix.FirstFx;
        for (var y = 0; y < Height; y++)
        for (var x = 0; x < Width; x++)
        {
            if (src[y * Width + x] != Ix.Clear) continue;
            if (IsCat(x - 1, y) || IsCat(x + 1, y) || IsCat(x, y - 1) || IsCat(x, y + 1)) Set(x, y, Ix.Outline);
        }
    }

    public void FlipX()
    {
        for (var y = 0; y < Height; y++)
            Array.Reverse(Px, y * Width, Width);
    }

    public uint[] ToArgb(Palette p)
    {
        var map = Ix.Colours(p);
        var result = new uint[Px.Length];
        for (var i = 0; i < Px.Length; i++) result[i] = map[Px[i]];
        return result;
    }
}

/// <summary>Colour indices used by <see cref="PixelCanvas"/>.</summary>
internal static class Ix
{
    public const byte Clear = 0, Outline = 1, Fur = 2, Shade = 3, Highlight = 4, Iris = 5, Pupil = 6, Shine = 7,
        Ear = 8, Nose = 9, Line = 10;

    public const byte FirstFx = 16;
    public const byte FxWhite = 16, FxInk = 17, FxRed = 18, FxYellow = 19, FxSteam = 20,
        FxLaptop = 23, FxScreen = 24, FxHeart = 25, FxZ = 26, FxKey = 27, FxCloud = 28,
        FxLaptopDark = 29, FxCode = 30, FxCloudShade = 31;

    public static byte FromChar(char c) => c switch
    {
        'o' => Outline,
        'f' => Fur,
        's' => Shade,
        'h' => Highlight,
        'p' => Ear,
        'n' => Nose,
        'w' => FxWhite,
        'i' => FxInk,
        _ => Clear,
    };

    public static uint[] Colours(Palette p)
    {
        var m = new uint[32];
        m[Outline] = p.Outline;
        m[Fur] = p.Fur;
        m[Shade] = p.Shade;
        m[Highlight] = p.Highlight;
        m[Iris] = p.Iris;
        m[Pupil] = 0xFF0B0A0F;
        m[Shine] = 0xFFFFFFFF;
        m[Ear] = p.InnerEar;
        m[Nose] = p.Nose;
        m[Line] = p.Line;
        m[FxWhite] = 0xFFFFFFFF;
        m[FxInk] = 0xFF1B1922;
        m[FxRed] = 0xFFFF4D3D;
        m[FxYellow] = 0xFFFFD23F;
        m[FxSteam] = 0xFFC9CBD6;
        m[FxLaptop] = 0xFFA3ACBE;
        m[FxScreen] = 0xFF1E2A3A;
        m[FxHeart] = 0xFFFF6B9A;
        m[FxZ] = 0xFF9AD0FF;
        m[FxKey] = 0xFF5B6276;
        m[FxCloud] = 0xFFF1F1F6;
        m[FxLaptopDark] = 0xFF4A5164;
        m[FxCode] = 0xFF63E6A0;
        m[FxCloudShade] = 0xFFB9BBC9;
        return m;
    }
}
