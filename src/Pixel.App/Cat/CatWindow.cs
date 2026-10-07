using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Pixel.App.Native;
using Pixel.Core.Art;

namespace Pixel.App.Cat;

/// <summary>
/// Transparent, topmost, non-activating window that shows the cat. Only opaque cat pixels catch the mouse —
/// the layered window lets clicks on transparent pixels fall through to whatever is underneath.
/// </summary>
internal sealed class CatWindow : Window
{
    private readonly WriteableBitmap _bitmap =
        new(CatRenderer.Width, CatRenderer.Height, 96, 96, PixelFormats.Pbgra32, null);

    public CatWindow(int scale)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        Title = "Pixel";
        Width = CatRenderer.Width * scale;
        Height = CatRenderer.Height * scale;

        var image = new Image { Source = _bitmap, Stretch = Stretch.Fill };
        System.Windows.Media.RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        Content = image;
    }

    public IntPtr Handle { get; private set; }

    /// <summary>Physical pixels per DIP for the monitor the cat is on.</summary>
    public double DpiScale => VisualTreeHelper.GetDpi(this).DpiScaleX;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Handle = new WindowInteropHelper(this).Handle;
        Win32.AddExStyle(Handle, Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE);
        if (Environment.GetEnvironmentVariable("PIXEL_DEBUG_CAPTURE") != "1")
            Win32.SetWindowDisplayAffinity(Handle, Win32.WDA_EXCLUDEFROMCAPTURE);
    }

    public void Draw(uint[] pixels) =>
        _bitmap.WritePixels(new Int32Rect(0, 0, CatRenderer.Width, CatRenderer.Height), pixels, CatRenderer.Width * 4, 0);
}
