using Pixel.App.Native;
using Pixel.Core.Motion;

namespace Pixel.App.Cat;

/// <summary>Finds the visible top edges of normal app windows for the cat to stand on (DIPs).</summary>
internal static class PlatformScanner
{
    private static readonly HashSet<string> ShellClasses =
        ["Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Progman", "WorkerW", "Windows.UI.Core.CoreWindow"];

    private const int SampleStep = 16;
    private const double MinTopDip = 90;   // room above the edge for the cat itself
    private const double MinWidthDip = 100;

    public static List<Platform> Scan(double scale, Func<IntPtr, bool> isOwnWindow)
    {
        var result = new List<Platform>();
        Win32.EnumWindows((hwnd, _) =>
        {
            if (isOwnWindow(hwnd) || !Win32.IsWindowVisible(hwnd) || Win32.IsIconic(hwnd) || Win32.IsZoomed(hwnd)) return true;
            if (Win32.IsCloaked(hwnd) || Win32.GetWindowTextLength(hwnd) == 0) return true;
            var ex = Win32.GetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE).ToInt64();
            if ((ex & Win32.WS_EX_TOOLWINDOW) != 0) return true;
            if (ShellClasses.Contains(Win32.ClassName(hwnd))) return true;

            var r = Win32.FrameBounds(hwnd);
            if (r.Width / scale < MinWidthDip || r.Top / scale < MinTopDip) return true;

            if (VisibleTopSpan(hwnd, r, scale, isOwnWindow) is (int left, int right))
                result.Add(new Platform(left / scale, right / scale, r.Top / scale, hwnd.ToInt64()));
            return true;
        }, IntPtr.Zero);
        return result;
    }

    /// <summary>Longest run of the window's top edge that isn't covered by another window.</summary>
    private static (int Left, int Right)? VisibleTopSpan(IntPtr hwnd, Win32.RECT r, double scale, Func<IntPtr, bool> isOwnWindow)
    {
        int? runStart = null;
        (int, int)? best = null;
        var bestLen = 0;
        var y = r.Top + 2;
        for (var x = r.Left + 4; x <= r.Right - 4; x += SampleStep)
        {
            var hit = Win32.GetAncestor(Win32.WindowFromPoint(new Win32.POINT { X = x, Y = y }), Win32.GA_ROOT);
            var visible = hit == hwnd || isOwnWindow(hit);
            if (visible)
            {
                runStart ??= x;
                if (x - runStart.Value > bestLen)
                {
                    bestLen = x - runStart.Value;
                    best = (runStart.Value, x);
                }
            }
            else
            {
                runStart = null;
            }
        }
        return bestLen / scale >= MinWidthDip ? best : null;
    }
}
