using System.Runtime.InteropServices;
using System.Windows.Threading;
using Pixel.App.Shell;
using Pixel.Core.Reminders;
using Pixel.Core.Tools;

namespace Pixel.App.Reminders;

/// <summary>
/// Water/stretch reminders, custom reminders, Pomodoro and notes. Reminders appear as a speech bubble above the
/// cat (with an alert pose) while Pixel is visible, and as a Windows notification while it is vanished.
/// </summary>
internal sealed class ReminderService : IProductivity, IDisposable
{
    private static readonly TimeSpan AwayAfter = TimeSpan.FromMinutes(5);
    private readonly AppHost _host;
    private readonly ReminderScheduler _scheduler;
    private readonly List<Note> _notes;
    private readonly Dictionary<Guid, StickyNoteWindow> _stickies = [];
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly SpeechBubble _bubble = new();
    private readonly TimerBadge _badge = new();

    public ReminderService(AppHost host)
    {
        _host = host;
        var s = host.Settings;
        _scheduler = new ReminderScheduler(DateTimeOffset.Now, Minutes(s.WaterReminderMinutes), Minutes(s.StretchReminderMinutes));
        var saved = PlannerStore.Load(PlannerStore.DefaultPath);
        _scheduler.Restore(saved.Reminders);
        _notes = [.. saved.Notes];
        foreach (var n in _notes.Where(n => n.Pinned)) ShowSticky(n);

        host.Vanishing += () =>
        {
            _bubble.Hide();
            _badge.Hide();
        };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    public IReadOnlyList<Note> Notes => _notes;

    /// <summary>Raised when notes are added, removed or (un)pinned.</summary>
    public event Action? NotesChanged;

    /// <summary>Raised when a reminder fires (used for the meow).</summary>
    public event Action<ReminderFired>? Fired;

    public void ApplySettings()
    {
        var s = _host.Settings;
        _scheduler.Configure(Minutes(s.WaterReminderMinutes), Minutes(s.StretchReminderMinutes), DateTimeOffset.Now);
    }

    // ---------------------------------------------------------------- IProductivity (tool calls)

    public string SetReminder(TimeSpan? inTime, TimeOnly? at, string message)
    {
        var now = DateTimeOffset.Now;
        var due = inTime is { } i ? now + i : ReminderScheduler.DueAt(at!.Value, now);
        _scheduler.Add(due, message);
        Save();
        var when = inTime is { } d
            ? d.TotalMinutes < 60 ? $"in {d.TotalMinutes:0.#} min" : $"at {due.LocalDateTime:HH:mm}"
            : due.Date == now.Date ? $"at {due.LocalDateTime:HH:mm}" : $"tomorrow at {due.LocalDateTime:HH:mm}";
        return $"Reminder set {when}: {message}";
    }

    public string StartPomodoro(int workMinutes, int breakMinutes)
    {
        _scheduler.Pomodoro.Start(DateTimeOffset.Now, workMinutes, breakMinutes);
        UpdateBadge();
        return $"Pomodoro started: {workMinutes} min focus, then a {breakMinutes} min break.";
    }

    public string StopPomodoro()
    {
        _scheduler.Pomodoro.Stop();
        _badge.Hide();
        return "Pomodoro stopped.";
    }

    public string AddNote(string text, bool pinned)
    {
        var note = new Note(Guid.NewGuid(), text, pinned, DateTimeOffset.Now);
        _notes.Add(note);
        if (pinned) ShowSticky(note);
        Save();
        NotesChanged?.Invoke();
        return pinned ? "Pinned your note to the desktop." : "Saved your note.";
    }

    public void SetPinned(Guid id, bool pinned)
    {
        var i = _notes.FindIndex(n => n.Id == id);
        if (i < 0) return;
        _notes[i] = _notes[i] with { Pinned = pinned };
        if (pinned) ShowSticky(_notes[i]);
        else if (_stickies.Remove(id, out var w)) w.Close();
        Save();
        NotesChanged?.Invoke();
    }

    public void DeleteNote(Guid id)
    {
        _notes.RemoveAll(n => n.Id == id);
        if (_stickies.Remove(id, out var w)) w.Close();
        Save();
        NotesChanged?.Invoke();
    }

    // ---------------------------------------------------------------- firing

    private void Tick()
    {
        var before = _scheduler.Pending.Count;
        foreach (var r in _scheduler.Tick(DateTimeOffset.Now, userPresent: IdleTime() < AwayAfter)) Deliver(r);
        if (_scheduler.Pending.Count != before) Save();
        UpdateBadge();
    }

    private void Deliver(ReminderFired r)
    {
        Log.Info($"reminder fired: {r.Kind}");
        Fired?.Invoke(r);
        if (_host.IsVanished)
        {
            _host.Tray.Notify("Pixel 🐾", r.Message);
            return;
        }
        _host.Cat.RaiseAlert(null, seconds: 8);
        _bubble.Say(r.Message, _host.Cat.Bounds, TimeSpan.FromSeconds(r.Kind == ReminderKind.Custom ? 20 : 10));
    }

    private void UpdateBadge()
    {
        var p = _scheduler.Pomodoro;
        if (p.Phase == PomodoroPhase.Idle || _host.IsVanished || p.Remaining(DateTimeOffset.Now) is not { } left)
        {
            if (_badge.IsVisible) _badge.Hide();
            return;
        }
        var label = p.Phase == PomodoroPhase.Focus ? "focus" : "break";
        if (left < TimeSpan.Zero) left = TimeSpan.Zero;
        _badge.Show($"🍅 {(int)left.TotalMinutes:00}:{left.Seconds:00} {label}", _host.Cat.Bounds, _bubble.IsVisible);
    }

    private void ShowSticky(Note note)
    {
        if (_stickies.ContainsKey(note.Id)) return;
        var area = System.Windows.SystemParameters.WorkArea;
        var w = new StickyNoteWindow(note, n => SetPinned(n.Id, false))
        {
            Left = area.Right - 250,
            Top = area.Top + 20 + _stickies.Count * 40,
        };
        _stickies[note.Id] = w;
        w.Show();
    }

    private void Save()
    {
        try
        {
            PlannerStore.Save(PlannerStore.DefaultPath, new PlannerData(_scheduler.Pending.ToList(), _notes.ToList()));
        }
        catch (Exception e)
        {
            Log.Error("saving reminders/notes failed", e);
        }
    }

    private static TimeSpan? Minutes(int m) => m > 0 ? TimeSpan.FromMinutes(m) : null;

    /// <summary>Time since the last keyboard/mouse input anywhere on the PC.</summary>
    private static TimeSpan IdleTime()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        return GetLastInputInfo(ref info) ? TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - info.dwTime)) : TimeSpan.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO info);

    public void Dispose()
    {
        _timer.Stop();
        _bubble.Close();
        _badge.Close();
        foreach (var w in _stickies.Values) w.Close();
    }
}
