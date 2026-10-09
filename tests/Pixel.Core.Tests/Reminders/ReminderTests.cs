using Pixel.Core.Reminders;

namespace Pixel.Core.Tests.Reminders;

public class ReminderSchedulerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 8, 9, 0, 0, TimeSpan.FromHours(5.5));

    private static List<ReminderFired> RunMinutes(ReminderScheduler s, double from, double to, bool present = true)
    {
        var fired = new List<ReminderFired>();
        for (var m = from; m <= to; m += 0.5) fired.AddRange(s.Tick(T0.AddMinutes(m), present));
        return fired;
    }

    [Fact]
    public void Water_fires_every_hour()
    {
        var s = new ReminderScheduler(T0, water: TimeSpan.FromMinutes(60), stretch: null);
        var fired = RunMinutes(s, 0, 150);
        Assert.Equal(2, fired.Count(f => f.Kind == ReminderKind.Water));
    }

    [Fact]
    public void Stretch_fires_every_45_minutes()
    {
        var s = new ReminderScheduler(T0, water: null, stretch: TimeSpan.FromMinutes(45));
        Assert.Equal(3, RunMinutes(s, 0, 140).Count(f => f.Kind == ReminderKind.Stretch));
    }

    [Fact]
    public void Disabled_periodic_reminders_never_fire()
    {
        var s = new ReminderScheduler(T0, water: null, stretch: null);
        Assert.Empty(RunMinutes(s, 0, 300));
    }

    [Fact]
    public void A_long_sleep_fires_a_periodic_reminder_once_not_many_times()
    {
        var s = new ReminderScheduler(T0, water: TimeSpan.FromMinutes(60), stretch: null);
        var fired = s.Tick(T0.AddHours(8), userPresent: true);
        Assert.Single(fired);
    }

    [Fact]
    public void Periodic_reminders_wait_while_the_user_is_away()
    {
        var s = new ReminderScheduler(T0, water: TimeSpan.FromMinutes(60), stretch: null);
        Assert.Empty(RunMinutes(s, 0, 90, present: false));
        Assert.Single(s.Tick(T0.AddMinutes(91), userPresent: true));
    }

    [Fact]
    public void Custom_reminder_fires_once_even_when_away()
    {
        var s = new ReminderScheduler(T0, null, null);
        s.Add(T0.AddMinutes(5), "call mom");
        var fired = RunMinutes(s, 0, 30, present: false);
        var f = Assert.Single(fired);
        Assert.Equal(ReminderKind.Custom, f.Kind);
        Assert.Equal("call mom", f.Message);
        Assert.Empty(s.Pending);
    }

    [Fact]
    public void Clock_time_already_past_today_means_tomorrow()
    {
        var due = ReminderScheduler.DueAt(new TimeOnly(8, 30), T0);
        Assert.Equal(T0.Date.AddDays(1).AddHours(8.5), due.DateTime);
        var later = ReminderScheduler.DueAt(new TimeOnly(17, 0), T0);
        Assert.Equal(T0.Date.AddHours(17), later.DateTime);
    }

    [Fact]
    public void Restored_reminders_keep_their_due_time()
    {
        var s = new ReminderScheduler(T0, null, null);
        s.Restore([new CustomReminder(Guid.NewGuid(), T0.AddMinutes(10), "stand up")]);
        Assert.Equal("stand up", Assert.Single(RunMinutes(s, 0, 20)).Message);
    }
}

public class PomodoroTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 8, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Focus_then_break_then_focus()
    {
        var p = new Pomodoro();
        p.Start(T0, 25, 5);
        Assert.Equal(PomodoroPhase.Focus, p.Phase);
        Assert.Null(p.Tick(T0.AddMinutes(24)));

        var end = p.Tick(T0.AddMinutes(25));
        Assert.Equal(ReminderKind.PomodoroFocusEnd, end!.Kind);
        Assert.Equal(PomodoroPhase.Break, p.Phase);

        Assert.Equal(ReminderKind.PomodoroBreakEnd, p.Tick(T0.AddMinutes(30))!.Kind);
        Assert.Equal(PomodoroPhase.Focus, p.Phase);
    }

    [Fact]
    public void Every_fourth_focus_earns_a_long_break()
    {
        var p = new Pomodoro();
        var t = T0;
        p.Start(t, 25, 5);
        for (var i = 0; i < 3; i++)
        {
            t = t.AddMinutes(25);
            p.Tick(t);
            Assert.Equal(PomodoroPhase.Break, p.Phase);
            t = t.AddMinutes(5);
            p.Tick(t);
        }
        t = t.AddMinutes(25);
        p.Tick(t);
        Assert.Equal(PomodoroPhase.LongBreak, p.Phase);
        Assert.Equal(t.AddMinutes(15), p.PhaseEnds);
    }

    [Fact]
    public void Stop_returns_to_idle_and_stays_quiet()
    {
        var p = new Pomodoro();
        p.Start(T0, 25, 5);
        p.Stop();
        Assert.Equal(PomodoroPhase.Idle, p.Phase);
        Assert.Null(p.Tick(T0.AddHours(1)));
    }

    [Fact]
    public void Remaining_time_counts_down()
    {
        var p = new Pomodoro();
        p.Start(T0, 25, 5);
        Assert.Equal(TimeSpan.FromMinutes(15), p.Remaining(T0.AddMinutes(10)));
    }
}

public sealed class PlannerStoreTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("pixel-planner-").FullName;
    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public void Notes_and_reminders_round_trip()
    {
        var path = Path.Combine(_dir, "planner.json");
        var data = new PlannerData(
            [new CustomReminder(Guid.NewGuid(), DateTimeOffset.Now.AddHours(1), "meeting")],
            [new Note(Guid.NewGuid(), "buy milk", Pinned: true, DateTimeOffset.Now)]);
        PlannerStore.Save(path, data);
        var back = PlannerStore.Load(path);
        Assert.Equal("meeting", Assert.Single(back.Reminders).Message);
        Assert.True(Assert.Single(back.Notes).Pinned);
    }

    [Fact]
    public void Missing_or_corrupt_file_is_empty()
    {
        Assert.Empty(PlannerStore.Load(Path.Combine(_dir, "none.json")).Notes);
        var bad = Path.Combine(_dir, "bad.json");
        File.WriteAllText(bad, "{nope");
        Assert.Empty(PlannerStore.Load(bad).Reminders);
    }
}
