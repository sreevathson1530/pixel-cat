using System.Text.Json;

namespace Pixel.Core.Reminders;

public enum ReminderKind { Water, Stretch, Custom, PomodoroFocusEnd, PomodoroBreakEnd }

public sealed record ReminderFired(ReminderKind Kind, string Message);

public sealed record CustomReminder(Guid Id, DateTimeOffset Due, string Message);

public sealed record Note(Guid Id, string Text, bool Pinned, DateTimeOffset Created);

/// <summary>
/// Water/stretch reminders (periodic, only while the user is at the PC) and one-off custom reminders.
/// A periodic reminder fires at most once after a long sleep, then reschedules from "now".
/// </summary>
public sealed class ReminderScheduler(DateTimeOffset start, TimeSpan? water, TimeSpan? stretch)
{
    private readonly List<CustomReminder> _custom = [];
    private (TimeSpan? Every, DateTimeOffset Next) _water = (water, start + (water ?? TimeSpan.Zero));
    private (TimeSpan? Every, DateTimeOffset Next) _stretch = (stretch, start + (stretch ?? TimeSpan.Zero));

    public Pomodoro Pomodoro { get; } = new();

    public IReadOnlyList<CustomReminder> Pending => _custom;

    public void Configure(TimeSpan? water, TimeSpan? stretch, DateTimeOffset now)
    {
        if (water != _water.Every) _water = (water, now + (water ?? TimeSpan.Zero));
        if (stretch != _stretch.Every) _stretch = (stretch, now + (stretch ?? TimeSpan.Zero));
    }

    public CustomReminder Add(DateTimeOffset due, string message)
    {
        var r = new CustomReminder(Guid.NewGuid(), due, message);
        _custom.Add(r);
        return r;
    }

    public void Restore(IEnumerable<CustomReminder> reminders) => _custom.AddRange(reminders);

    /// <summary>Next occurrence of a clock time: today if still ahead, otherwise tomorrow.</summary>
    public static DateTimeOffset DueAt(TimeOnly at, DateTimeOffset now)
    {
        var today = new DateTimeOffset(now.Date + at.ToTimeSpan(), now.Offset);
        return today > now ? today : today.AddDays(1);
    }

    public IReadOnlyList<ReminderFired> Tick(DateTimeOffset now, bool userPresent)
    {
        var fired = new List<ReminderFired>();
        foreach (var r in _custom.Where(r => r.Due <= now).ToList())
        {
            _custom.Remove(r);
            fired.Add(new ReminderFired(ReminderKind.Custom, r.Message));
        }

        if (userPresent)
        {
            if (Due(ref _water, now)) fired.Add(new ReminderFired(ReminderKind.Water, "Time for a sip of water! 💧"));
            if (Due(ref _stretch, now)) fired.Add(new ReminderFired(ReminderKind.Stretch, "Stretch break! Stand up and stretch like a cat 🐾"));
        }

        if (Pomodoro.Tick(now) is { } p) fired.Add(p);
        return fired;
    }

    private static bool Due(ref (TimeSpan? Every, DateTimeOffset Next) slot, DateTimeOffset now)
    {
        if (slot.Every is not { } every || now < slot.Next) return false;
        slot.Next = now + every;
        return true;
    }
}

public enum PomodoroPhase { Idle, Focus, Break, LongBreak }

/// <summary>Classic Pomodoro: focus / short break, with a long break after every fourth focus session.</summary>
public sealed class Pomodoro
{
    private const int LongBreakMinutes = 15;
    private const int SessionsBeforeLongBreak = 4;
    private int _work = 25, _break = 5;

    public PomodoroPhase Phase { get; private set; } = PomodoroPhase.Idle;
    public DateTimeOffset? PhaseEnds { get; private set; }
    public int CompletedFocus { get; private set; }

    public void Start(DateTimeOffset now, int workMinutes, int breakMinutes)
    {
        _work = workMinutes;
        _break = breakMinutes;
        CompletedFocus = 0;
        Phase = PomodoroPhase.Focus;
        PhaseEnds = now.AddMinutes(workMinutes);
    }

    public void Stop()
    {
        Phase = PomodoroPhase.Idle;
        PhaseEnds = null;
    }

    public TimeSpan? Remaining(DateTimeOffset now) => PhaseEnds is { } end ? end - now : null;

    public ReminderFired? Tick(DateTimeOffset now)
    {
        if (Phase == PomodoroPhase.Idle || PhaseEnds is not { } end || now < end) return null;

        if (Phase == PomodoroPhase.Focus)
        {
            CompletedFocus++;
            var longBreak = CompletedFocus % SessionsBeforeLongBreak == 0;
            Phase = longBreak ? PomodoroPhase.LongBreak : PomodoroPhase.Break;
            var minutes = longBreak ? LongBreakMinutes : _break;
            PhaseEnds = now.AddMinutes(minutes);
            return new ReminderFired(ReminderKind.PomodoroFocusEnd, $"Focus session done! Take a {minutes}-minute break 🐾");
        }

        Phase = PomodoroPhase.Focus;
        PhaseEnds = now.AddMinutes(_work);
        return new ReminderFired(ReminderKind.PomodoroBreakEnd, "Break's over — time to focus 🍅");
    }
}

public sealed record PlannerData(IReadOnlyList<CustomReminder> Reminders, IReadOnlyList<Note> Notes)
{
    public static PlannerData Empty { get; } = new([], []);
}

/// <summary>Custom reminders and notes, persisted to %APPDATA%\Pixel\planner.json.</summary>
public static class PlannerStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Pixel", "planner.json");

    public static PlannerData Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return PlannerData.Empty;
            var data = JsonSerializer.Deserialize<PlannerData>(File.ReadAllText(path), Json);
            return data is null ? PlannerData.Empty : new PlannerData(data.Reminders ?? [], data.Notes ?? []);
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return PlannerData.Empty;
        }
    }

    public static void Save(string path, PlannerData data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(data, Json));
        File.Move(tmp, path, overwrite: true);
    }
}
