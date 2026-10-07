namespace Pixel.Core.Screen;

/// <summary>Cheap "did the screen change?" test on tiny grayscale thumbnails, so OCR only runs when needed.</summary>
public static class ChangeDetector
{
    public const int ThumbWidth = 64;
    public const int ThumbHeight = 36;
    private const int PixelDelta = 24;
    private const double ChangedFraction = 0.005;

    /// <summary>Box-averaged grayscale thumbnail of a BGRA image.</summary>
    public static byte[] Thumbnail(ReadOnlySpan<byte> bgra, int width, int height)
    {
        var thumb = new byte[ThumbWidth * ThumbHeight];
        for (var ty = 0; ty < ThumbHeight; ty++)
        for (var tx = 0; tx < ThumbWidth; tx++)
        {
            int x0 = tx * width / ThumbWidth, x1 = Math.Max(x0 + 1, (tx + 1) * width / ThumbWidth);
            int y0 = ty * height / ThumbHeight, y1 = Math.Max(y0 + 1, (ty + 1) * height / ThumbHeight);
            long sum = 0, n = 0;
            for (var y = y0; y < y1; y += 2)
            for (var x = x0; x < x1; x += 2)
            {
                var i = (y * width + x) * 4;
                sum += (bgra[i] * 29 + bgra[i + 1] * 150 + bgra[i + 2] * 77) >> 8;
                n++;
            }
            thumb[ty * ThumbWidth + tx] = (byte)(sum / Math.Max(1, n));
        }
        return thumb;
    }

    public static bool HasChanged(byte[]? previous, byte[] current)
    {
        if (previous is null || previous.Length != current.Length) return true;
        var changed = 0;
        for (var i = 0; i < current.Length; i++)
            if (Math.Abs(previous[i] - current[i]) > PixelDelta) changed++;
        return changed > current.Length * ChangedFraction;
    }
}
