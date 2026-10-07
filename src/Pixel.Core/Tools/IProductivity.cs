namespace Pixel.Core.Tools;

/// <summary>Reminders, Pomodoro and notes — the actions that need no confirmation. Each returns a short result.</summary>
public interface IProductivity
{
    string SetReminder(TimeSpan? inTime, TimeOnly? at, string message);
    string StartPomodoro(int workMinutes, int breakMinutes);
    string StopPomodoro();
    string AddNote(string text, bool pinned);
}
