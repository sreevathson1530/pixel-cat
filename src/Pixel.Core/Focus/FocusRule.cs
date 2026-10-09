namespace Pixel.Core.Focus;

public readonly record struct ScreenRect(int Left, int Top, int Right, int Bottom);

/// <summary>Is the foreground window a full-screen app (video, game, presentation)? Then the cat steps aside.</summary>
public static class FocusRule
{
    private static readonly HashSet<string> ShellClasses = ["Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd"];

    public static bool IsFullscreen(ScreenRect window, ScreenRect monitor, string className) =>
        !ShellClasses.Contains(className) &&
        window.Left <= monitor.Left && window.Top <= monitor.Top &&
        window.Right >= monitor.Right && window.Bottom >= monitor.Bottom;
}
