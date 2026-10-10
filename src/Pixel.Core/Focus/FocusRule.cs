namespace Pixel.Core.Focus;

public readonly record struct ScreenRect(int Left, int Top, int Right, int Bottom);

/// <summary>Is the foreground window a full-screen app (video, game, presentation)? Then the cat steps aside.</summary>
public static class FocusRule
{
    private static readonly HashSet<string> ShellClasses = ["Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd"];

    /// <param name="maximized">The window is in the normal "maximised" state.</param>
    /// <param name="hasCaption">The window has a title bar.</param>
    /// <remarks>
    /// With an auto-hiding taskbar an ordinary maximised window covers the whole monitor too, so covering it is not
    /// enough: real full-screen apps drop the title bar and aren't in the maximised state.
    /// </remarks>
    public static bool IsFullscreen(ScreenRect window, ScreenRect monitor, string className, bool maximized, bool hasCaption) =>
        !ShellClasses.Contains(className) &&
        !(maximized && hasCaption) &&
        window.Left <= monitor.Left && window.Top <= monitor.Top &&
        window.Right >= monitor.Right && window.Bottom >= monitor.Bottom;
}
