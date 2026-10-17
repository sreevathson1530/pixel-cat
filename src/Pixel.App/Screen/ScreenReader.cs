using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using Pixel.App.Native;
using Pixel.App.Shell;
using Pixel.Core.Screen;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Security.Cryptography;

namespace Pixel.App.Screen;

/// <summary>One recognised line of text and where it is on screen (physical pixels).</summary>
internal readonly record struct OcrLine(string Text, Rectangle Bounds);

/// <summary>A captured screen frame (BGRA, top-down) in physical pixels.</summary>
internal sealed record ScreenFrame(byte[] Bgra, int Width, int Height);

/// <summary>Captures the primary screen and reads its text with Windows' built-in OCR. Nothing touches disk.</summary>
internal sealed class ScreenReader
{
    private readonly OcrEngine? _engine = OcrEngine.TryCreateFromUserProfileLanguages();

    public bool OcrAvailable => _engine is not null;

    public static ScreenFrame Capture()
    {
        var bounds = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
        using var bmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
            g.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
        var data = bmp.LockBits(new Rectangle(Point.Empty, bounds.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var bytes = new byte[data.Stride * data.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            return new ScreenFrame(bytes, data.Width, data.Height);
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }

    public async Task<IReadOnlyList<OcrLine>> RecognizeAsync(ScreenFrame frame)
    {
        if (_engine is null) return [];
        var (bytes, w, h, scale) = FitForOcr(frame);
        var buffer = CryptographicBuffer.CreateFromByteArray(bytes);
        using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(buffer, BitmapPixelFormat.Bgra8, w, h, BitmapAlphaMode.Ignore);
        var result = await _engine.RecognizeAsync(bitmap);

        var lines = new List<OcrLine>(result.Lines.Count);
        foreach (var line in result.Lines)
        {
            double l = double.MaxValue, t = double.MaxValue, r = 0, b = 0;
            foreach (var word in line.Words)
            {
                var wr = word.BoundingRect;
                l = Math.Min(l, wr.X);
                t = Math.Min(t, wr.Y);
                r = Math.Max(r, wr.X + wr.Width);
                b = Math.Max(b, wr.Y + wr.Height);
            }
            lines.Add(new OcrLine(line.Text, Rectangle.FromLTRB(
                (int)(l / scale), (int)(t / scale), (int)(r / scale), (int)(b / scale))));
        }
        return lines;
    }

    /// <summary>
    /// Longest side of the screenshot shown to a vision brain: 640×360 for a 16:9 screen ≈ 300 image tokens — about
    /// half the reading time of 1024 px. Exact text comes from OCR, so the picture only needs to show what things look like.
    /// </summary>
    public const int VisionMaxSide = 640;

    private static readonly ImageCodecInfo Jpeg = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);

    /// <summary>The frame as a downscaled JPEG for a brain that can see pictures. Kept in memory only.</summary>
    public static byte[] EncodeForVision(ScreenFrame f, int maxSide = VisionMaxSide)
    {
        var k = Math.Min(1.0, maxSide / (double)Math.Max(f.Width, f.Height));
        int w = Math.Max(1, (int)Math.Round(f.Width * k)), h = Math.Max(1, (int)Math.Round(f.Height * k));
        var pin = GCHandle.Alloc(f.Bgra, GCHandleType.Pinned);
        try
        {
            using var source = new Bitmap(f.Width, f.Height, f.Width * 4, PixelFormat.Format32bppRgb, pin.AddrOfPinnedObject());
            using var small = new Bitmap(w, h, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(small))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(source, 0, 0, w, h);
            }
            using var ms = new MemoryStream();
            using var quality = new EncoderParameters(1);
            quality.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 85L);
            small.Save(ms, Jpeg, quality);
            return ms.ToArray();
        }
        finally
        {
            pin.Free();
        }
    }

    /// <summary>OCR has a maximum image size; downscale by an integer factor when needed.</summary>
    private static (byte[] Bytes, int W, int H, double Scale) FitForOcr(ScreenFrame f)
    {
        var max = (int)OcrEngine.MaxImageDimension;
        if (f.Width <= max && f.Height <= max) return (f.Bgra, f.Width, f.Height, 1);
        var k = (int)Math.Ceiling(Math.Max(f.Width, f.Height) / (double)max);
        int w = f.Width / k, h = f.Height / k;
        var dst = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
            Buffer.BlockCopy(f.Bgra, ((y * k) * f.Width + x * k) * 4, dst, (y * w + x) * 4, 4);
        return (dst, w, h, 1.0 / k);
    }

    /// <summary>Title and process of a window, best effort. The desktop is called that, not "Program Manager".</summary>
    public static (string Title, string Process) Describe(IntPtr hwnd)
    {
        if (Win32.ClassName(hwnd) is "Progman" or "WorkerW") return ("Desktop (wallpaper and icons)", "explorer");
        var title = Win32.WindowText(hwnd);
        try
        {
            Win32.GetWindowThreadProcessId(hwnd, out var pid);
            using var p = Process.GetProcessById((int)pid);
            return (title, p.ProcessName);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            Log.Info($"process lookup failed: {e.Message}");
            return (title, "");
        }
    }
}
