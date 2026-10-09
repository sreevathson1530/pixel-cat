using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Threading;
using Pixel.App.Brain;
using Pixel.App.Cat;
using Pixel.App.Chat;
using Pixel.App.History;
using Pixel.App.Input;
using Pixel.App.Reminders;
using Pixel.App.Screen;
using Pixel.App.Tools;
using Pixel.Core.Art;
using Pixel.Core.Input;
using Pixel.Core.Screen;
using Pixel.Core.Storage;
using Pixel.Core.Tools;

namespace Pixel.App.Shell;

/// <summary>Handles of Pixel's own windows (excluded from platforms and screen reading).</summary>
internal static class OwnWindows
{
    private static readonly ConcurrentDictionary<IntPtr, byte> Handles = new();
    public static void Add(IntPtr hwnd) => Handles[hwnd] = 0;
    public static void Remove(IntPtr hwnd) => Handles.TryRemove(hwnd, out _);
    public static bool Contains(IntPtr hwnd) => Handles.ContainsKey(hwnd);
}

/// <summary>Composition root: owns the cat, hooks, tray and the vanish/revive lifecycle.</summary>
internal sealed class AppHost : IDisposable
{
    private readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;
    private HookThread? _hooks;
    private TrayIcon? _tray;

    public Settings Settings { get; private set; } = SettingsStore.Load(SettingsStore.DefaultPath);
    public CatController Cat { get; private set; } = null!;
    public ChatController Chat { get; private set; } = null!;
    public ScreenWatcher Screen { get; private set; } = null!;
    public BrainService Brain { get; private set; } = null!;
    public IProductivity Productivity { get; set; } = new NoProductivity();
    public ReminderService? Reminders { get; private set; }
    private NotesWindow? _notesWindow;
    public HistoryStore? History { get; private set; }
    private HistoryWindow? _historyWindow;
    private readonly ErrorSpotter _errors = new();
    private ErrorFinding? _finding;
    public TrayIcon Tray => _tray!;
    public bool IsVanished { get; private set; }

    /// <summary>Raised when the vanish poof starts: end the conversation, stop watching, unload the brain.</summary>
    public event Action? Vanishing;
    /// <summary>Raised when Pixel comes back: start a fresh conversation, resume watching.</summary>
    public event Action? Revived;
    public event Action? ChatToggled;

    public void Start()
    {
        Cat = new CatController(Settings.Scale, Palette.Named(Settings.Palette), OwnWindows.Contains);
        Cat.Start();
        OwnWindows.Add(Cat.Handle);
        var reader = new ScreenReader();
        if (!reader.OcrAvailable) Log.Info("Windows OCR unavailable (no OCR language installed)");
        Screen = new ScreenWatcher(reader, OwnWindows.Contains);
        Screen.Updated += OnScreenUpdated;
        Screen.Start();
        Vanishing += Screen.Stop;
        Revived += Screen.Start;

        History = OpenHistory();
        Chat = new ChatController(this, Screen.CaptureNowAsync, new ToolExecutor(() => Productivity), History);
        Brain = new BrainService(() => Settings, msg => Chat.ConfirmAsync("Load my brain anyway?", msg), ToolDefinitions.All);
        if (Environment.GetEnvironmentVariable("PIXEL_FAKE_BRAIN") != "1") Chat.UseBrain(Brain.Brain);
        Vanishing += Brain.Unload;
        Cat.Clicked += OnCatClicked;

        _hooks = new HookThread(new HotkeyDetector(), OnHotkey, Cat.Post);
        _hooks.Start();
        Log.Info($"Pixel started (pid {Environment.ProcessId})");

        _tray = new TrayIcon(Palette.Named(Settings.Palette));
        _tray.ToggleVanish += ToggleVanish;
        _tray.ToggleChat += ToggleChat;
        _tray.ToggleStay += () => Cat.ToggleStay();
        _tray.ToggleAutoStart += () => SetAutoStart(!AutoStart.IsEnabled());
        _tray.Opening += () => _tray.SetState(!IsVanished, Cat.Staying, AutoStart.IsEnabled());
        _tray.Quit += () => Application.Current.Shutdown();
        _tray.OpenHistory += ShowHistory;

        Reminders = new ReminderService(this);
        Productivity = Reminders;
        _tray.OpenNotes += ShowNotes;

        // Only the installed build registers itself; a dev build under bin\ never should.
        var devBuild = Environment.ProcessPath?.Contains(@"\bin\", StringComparison.OrdinalIgnoreCase) == true;
        if (!devBuild && Settings.StartWithWindows && !AutoStart.IsEnabled()) AutoStart.Set(true);
    }

    /// <summary>Worker thread: look for errors in the active window and send the cat to them.</summary>
    private void OnScreenUpdated(ScreenSnapshot snapshot, IReadOnlyList<OcrLine> foregroundLines)
    {
        if (!Settings.ProactiveErrors || Chat.IsOpen) return;
        if (_errors.Scan(snapshot, DateTimeOffset.Now) is not { } finding) return;
        var line = foregroundLines.FirstOrDefault(l => l.Text.Trim() == finding.Line);
        _ui.BeginInvoke(() =>
        {
            _finding = finding;
            double? x = line.Text is null ? null : (line.Bounds.Left + line.Bounds.Width / 2.0) / Cat.DpiScale;
            Cat.RaiseAlert(x);
            Log.Info("error spotted on screen");
        });
    }

    private void OnCatClicked()
    {
        if (!Cat.AlertActive || _finding is null) return;
        var line = _finding.Line;
        Cat.ClearAlert();
        _finding = null;
        _ = Chat.AskAsync($"What does this error mean, and how do I fix it?\n\n{line}");
    }

    private static HistoryStore? OpenHistory()
    {
        try
        {
            return new HistoryStore(HistoryStore.DefaultPath);
        }
        catch (Exception e)
        {
            Log.Error("history database unavailable", e);
            return null;
        }
    }

    private void ShowNotes()
    {
        if (Reminders is null) return;
        if (_notesWindow is { IsLoaded: true })
        {
            _notesWindow.Activate();
            return;
        }
        _notesWindow = new NotesWindow(Reminders);
        _notesWindow.Closed += (_, _) => _notesWindow = null;
        _notesWindow.Show();
        _notesWindow.Activate();
    }

    private void ShowHistory()
    {
        if (History is null) return;
        if (_historyWindow is { IsLoaded: true })
        {
            _historyWindow.Activate();
            return;
        }
        _historyWindow = new HistoryWindow(History);
        _historyWindow.Closed += (_, _) => _historyWindow = null;
        _historyWindow.Show();
        _historyWindow.Activate();
    }

    public void UpdateSettings(Settings settings)
    {
        Settings = settings;
        SettingsStore.Save(SettingsStore.DefaultPath, settings);
        Cat.Palette = Palette.Named(settings.Palette);
        Reminders?.ApplySettings();
    }

    private void SetAutoStart(bool enabled)
    {
        AutoStart.Set(enabled);
        UpdateSettings(Settings with { StartWithWindows = enabled });
    }

    /// <summary>Called on the hook thread — hop to the UI thread immediately.</summary>
    private void OnHotkey(HotkeyAction action) => _ui.BeginInvoke(() =>
    {
        Log.Info($"hotkey {action} (hooks active: {_hooks?.HooksActive})");
        if (action == HotkeyAction.ToggleVanish) ToggleVanish();
        else ToggleChat();
    });

    public void ToggleVanish()
    {
        if (IsVanished)
        {
            IsVanished = false;
            Log.Info("revive");
            Cat.Revive();
            Revived?.Invoke();
        }
        else
        {
            IsVanished = true;
            Log.Info("vanish");
            Vanishing?.Invoke();
            Cat.Vanish(() => Log.Info($"hidden (window visible: {Cat.IsVisible})"));
        }
    }

    public void ToggleChat()
    {
        if (!IsVanished) ChatToggled?.Invoke();
    }

    public void Dispose()
    {
        Screen?.Dispose();
        Brain?.Dispose();
        History?.Dispose();
        Reminders?.Dispose();
        _hooks?.Dispose();
        _tray?.Dispose();
    }
}
