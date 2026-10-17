using System.IO;
using System.Media;
using System.Windows.Threading;
using Pixel.App.Native;
using Pixel.Core.Behaviour;
using Pixel.Core.Focus;
using Pixel.Core.Sound;

namespace Pixel.App.Shell;

/// <summary>Soft meows and poofs, synthesised in memory at the user's volume.</summary>
internal sealed class SoundService
{
    private readonly AppHost _host;
    private DateTime _lastPlayed = DateTime.MinValue;

    public SoundService(AppHost host)
    {
        _host = host;
        host.Cat.ActivityChanged += OnActivity;
    }

    private void OnActivity(Activity activity)
    {
        if (CatSounds.For(activity) is { } k) Play(k);
    }

    public void Play(SoundKind kind)
    {
        var s = _host.Settings;
        if (s.Muted || s.Volume <= 0) return;
        if (DateTime.Now - _lastPlayed < TimeSpan.FromSeconds(1.5) && kind != SoundKind.Poof) return; // no meow spam
        _lastPlayed = DateTime.Now;
        try
        {
            using var player = new SoundPlayer(new MemoryStream(ChipSynth.Render(kind, s.Volume)));
            player.Play(); // async; the stream is loaded before Play returns
        }
        catch (Exception e) when (e is InvalidOperationException or IOException)
        {
            Log.Info($"sound failed: {e.Message}");
        }
    }
}

/// <summary>Watches for full-screen apps (videos, games, slides) so the cat can step aside and peek.</summary>
internal sealed class FocusWatcher
{
    private readonly AppHost _host;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };

    public FocusWatcher(AppHost host)
    {
        _host = host;
        _timer.Tick += (_, _) => Check();
        _timer.Start();
    }

    private void Check()
    {
        var fg = Win32.GetForegroundWindow();
        var fullscreen = false;
        if (fg != IntPtr.Zero && !OwnWindows.Contains(fg))
        {
            var r = Win32.FrameBounds(fg);
            var info = new Win32.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Win32.MONITORINFO>() };
            if (Win32.GetMonitorInfo(Win32.MonitorFromWindow(fg, Win32.MONITOR_DEFAULTTONEAREST), ref info))
            {
                var m = info.rcMonitor;
                const long WS_CAPTION = 0x00C00000;
                var style = Win32.GetWindowLongPtr(fg, Win32.GWL_STYLE).ToInt64();
                fullscreen = FocusRule.IsFullscreen(new ScreenRect(r.Left, r.Top, r.Right, r.Bottom),
                    new ScreenRect(m.Left, m.Top, m.Right, m.Bottom), Win32.ClassName(fg),
                    maximized: Win32.IsZoomed(fg), hasCaption: (style & WS_CAPTION) == WS_CAPTION);
            }
        }
        if (_host.Cat.Overlay.FocusMode != fullscreen)
            _host.Cat.Overlay = _host.Cat.Overlay with { FocusMode = fullscreen };
    }
}
