using System.Collections.Concurrent;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Pixel.App.Native;
using Pixel.Core.Art;
using Pixel.Core.Behaviour;
using Activity = Pixel.Core.Behaviour.Activity;
using Pixel.Core.Input;
using Pixel.Core.Motion;

namespace Pixel.App.Cat;

internal enum InputKind { Key, Scroll, Pointer }

/// <summary>Raw input notification from the hook thread. Carries timing only — never which key.</summary>
internal readonly record struct InputEvent(InputKind Kind, double T);

/// <summary>Runs the cat: collects signals, ticks the pet director, renders frames and moves the window.</summary>
internal sealed class CatController
{
    private const double TickSeconds = 1 / 30.0;

    private readonly CatWindow _window;
    private readonly MotionController _motion;
    private readonly PetDirector _director;
    private readonly ActivityMeter _meter = new();
    private readonly PettingDetector _petting = new();
    private readonly ConcurrentQueue<InputEvent> _input = new();
    private readonly DispatcherTimer _timer;
    private readonly Func<IntPtr, bool> _isOwnWindow;

    private List<Platform> _platforms = [];
    private double _lastTick, _lastPlatformScan = -1, _lastAreaScan = -1, _lastTopmost, _lastUserInput;
    private Point _lastCursor;
    private (Frame Frame, RenderOptions Options)? _drawn;
    private bool _pressed, _dragging;
    private Point _pressAt;
    private Action? _onVanished;
    private bool _userStaying;
    private double _alertUntil;

    public CatController(int scale, Palette palette, Func<IntPtr, bool> isOwnWindow)
    {
        Palette = palette;
        _isOwnWindow = isOwnWindow;
        _window = new CatWindow(scale);
        _motion = new MotionController(new Bounds(0, 0, SystemParameters.WorkArea.Width, SystemParameters.WorkArea.Height));
        _director = new PetDirector(_motion, new Roamer(new Random()));
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromSeconds(TickSeconds) };
        _timer.Tick += (_, _) => Tick();

        _window.MouseLeftButtonDown += OnMouseDown;
        _window.MouseMove += OnMouseMove;
        _window.MouseLeftButtonUp += OnMouseUp;
        _window.MouseLeave += (_, _) => _petting.Leave();
    }

    public static Stopwatch Clock { get; } = Stopwatch.StartNew();
    public static double Now => Clock.Elapsed.TotalSeconds;

    public Palette Palette { get; set; }
    public IntPtr Handle => _window.Handle;
    public bool IsVisible => _window.IsVisible;

    /// <summary>Signals owned by other services (chat, agents, alerts, focus mode, poof).</summary>
    public PetSignals Overlay { get; set; } = new();

    /// <summary>Cat feet position and size in DIPs, for placing the chat panel and bubbles.</summary>
    public Rect Bounds => new(_window.Left, _window.Top, _window.Width, _window.Height);

    /// <summary>User dropped the cat somewhere (stays until double-clicked).</summary>
    public bool Staying => _userStaying;

    /// <summary>Keeps the cat beside the open chat panel instead of wandering off.</summary>
    public bool HoldStill { get; set; }
    public double UserIdleSeconds => Now - _lastUserInput;

    /// <summary>Physical pixels per DIP for the cat's monitor.</summary>
    public double DpiScale => _window.Handle != IntPtr.Zero ? _window.DpiScale : 1;

    /// <summary>Single click on the cat (not a drag).</summary>
    public event Action? Clicked;

    /// <summary>The cat switched what it is doing (used for sounds).</summary>
    public event Action<Activity>? ActivityChanged;

    public bool AlertActive => Now < _alertUntil;

    /// <summary>Shows the "!" alert; if <paramref name="atX"/> is given the cat runs there first.</summary>
    public void RaiseAlert(double? atX, double seconds = 20)
    {
        _alertUntil = Now + seconds;
        _director.AttentionX = atX;
    }

    public void ClearAlert()
    {
        _alertUntil = 0;
        _director.AttentionX = null;
    }

    /// <summary>Thread-safe: called from the input hook thread.</summary>
    public void Post(InputEvent e) => _input.Enqueue(e);

    public void Start()
    {
        var area = SystemParameters.WorkArea;
        _motion.SetArea(new Bounds(area.Left, area.Top, area.Right, area.Bottom));
        _motion.Place(area.Left + area.Width * 0.75, area.Bottom);
        _window.Show();
        _lastTick = Now;
        _lastUserInput = Now;
        _timer.Start();
        Tick();
    }

    /// <summary>Plays the poof, then hides the window and calls <paramref name="onGone"/>.</summary>
    public void Vanish(Action onGone)
    {
        _onVanished = onGone;
        Overlay = Overlay with { Poof = PoofState.Out };
    }

    public void Revive()
    {
        _window.Show();
        _timer.Start();
        _lastTick = Now;
        Overlay = Overlay with { Poof = PoofState.In };
    }

    public void ToggleStay()
    {
        _userStaying = !_userStaying;
    }

    private void Tick()
    {
        var now = Now;
        var dt = Math.Clamp(now - _lastTick, 0, 0.1);
        _lastTick = now;

        while (_input.TryDequeue(out var e))
        {
            _lastUserInput = Math.Max(_lastUserInput, e.T);
            if (e.Kind == InputKind.Key) _meter.OnKeyDown(e.T);
            else if (e.Kind == InputKind.Scroll) _meter.OnScroll(e.T);
        }

        RefreshWorld(now);
        var cursor = CursorDip();
        if (cursor != _lastCursor)
        {
            _lastUserInput = now;
            _lastCursor = cursor;
        }
        _meter.OnMouse(cursor.X, cursor.Y, now);
        _meter.Update(now);

        if (_dragging) _motion.Hold(cursor.X, cursor.Y + (CatRenderer.Height - 5) * _window.Width / CatRenderer.Width);

        var center = new Point(_motion.Body.X, _motion.Body.Y - _window.Height / 2);
        var distance = (cursor - center).Length;
        var signals = Overlay with
        {
            Alert = Overlay.Alert || now < _alertUntil,
            Held = _motion.Mode == MotionMode.Held,
            Overheated = _meter.IsOverheated,
            Hunting = !_dragging && HuntRule.ShouldHunt(_meter.MouseSpeed, distance),
            Purring = _petting.IsPurring(now),
            Typing = _meter.IsTyping,
            Scrolling = _meter.IsScrolling,
        };

        _director.CursorX = cursor.X;
        _director.Staying = _userStaying || HoldStill;
        var before = _director.Activity;
        _director.Tick(now, dt, signals, _platforms, UserIdleSeconds);
        if (_director.Activity != before) ActivityChanged?.Invoke(_director.Activity);
        FinishPoof(now);
        Draw(now, cursor);
        Place();
    }

    private void FinishPoof(double now)
    {
        var anim = Animations.Get(_director.Animation);
        if (!anim.IsFinished((now - _director.AnimationStart) * 1000)) return;
        if (_director.Animation == AnimationId.PoofOut)
        {
            Overlay = Overlay with { Poof = PoofState.None };
            _timer.Stop();
            _window.Hide();
            _onVanished?.Invoke();
            _onVanished = null;
        }
        else if (_director.Animation == AnimationId.PoofIn)
        {
            Overlay = Overlay with { Poof = PoofState.None };
        }
    }

    private void Draw(double now, Point cursor)
    {
        var frame = Animations.Get(_director.Animation).At((now - _director.AnimationStart) * 1000);
        var head = new Point(_motion.Body.X, _motion.Body.Y - _window.Height * 0.62);
        var dx = cursor.X - head.X;
        var dy = cursor.Y - head.Y;
        var options = new RenderOptions(
            FacingLeft: _motion.Body.Facing == Facing.Left,
            PupilDx: dx < -40 ? -1 : dx > 40 ? 1 : 0,
            PupilDy: dy < -60 ? -1 : dy > 60 ? 1 : 0,
            Overheated: _director.Overheated);

        if (_drawn is (Frame f, RenderOptions o) && ReferenceEquals(f, frame) && o == options) return;
        _window.Draw(CatRenderer.Render(frame.Pose, Palette, options));
        _drawn = (frame, options);
    }

    private void Place()
    {
        var left = Math.Round(_motion.Body.X - _window.Width / 2);
        var top = Math.Round(_motion.Body.Y - _window.Height);
        if (Overlay.FocusMode)
        {
            // Full-screen app: tuck into the bottom-right corner and just peek over the edge.
            var area = SystemParameters.WorkArea;
            left = Math.Round(area.Right - _window.Width * 0.62);
            top = Math.Round(area.Bottom - _window.Height);
        }
        if (_window.Left != left) _window.Left = left;
        if (_window.Top != top) _window.Top = top;
    }

    private void RefreshWorld(double now)
    {
        if (now - _lastAreaScan > 2)
        {
            _lastAreaScan = now;
            var a = SystemParameters.WorkArea;
            var area = new Bounds(a.Left, a.Top, a.Right, a.Bottom);
            if (area != _motion.Area) _motion.SetArea(area);
        }
        if (now - _lastPlatformScan > 0.5 && _window.Handle != IntPtr.Zero)
        {
            _lastPlatformScan = now;
            _platforms = PlatformScanner.Scan(_window.DpiScale, _isOwnWindow);
        }
        if (now - _lastTopmost > 2 && _window.Handle != IntPtr.Zero)
        {
            _lastTopmost = now;
            Win32.KeepTopmost(_window.Handle);
        }
    }

    private Point CursorDip()
    {
        Win32.GetCursorPos(out var p);
        var s = _window.Handle != IntPtr.Zero ? _window.DpiScale : 1;
        return new Point(p.X / s, p.Y / s);
    }

    // ---------------------------------------------------------------- mouse on the cat

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            _userStaying = false; // double-click: back to roaming
            return;
        }
        _pressed = true;
        _pressAt = CursorDip();
        _window.CaptureMouse();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var cursor = CursorDip();
        if (_pressed && !_dragging && (cursor - _pressAt).Length > 4) _dragging = true;
        if (!_pressed && e.GetPosition(_window).Y < _window.Height * 0.55) _petting.OnPointer(cursor.X, Now);
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        _window.ReleaseMouseCapture();
        if (_dragging)
        {
            _motion.Release();
            _userStaying = true; // dropped: stay here until double-clicked
        }
        else if (_pressed)
        {
            Clicked?.Invoke();
        }
        _pressed = _dragging = false;
    }
}
