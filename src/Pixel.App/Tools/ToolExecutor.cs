using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using Pixel.App.Shell;
using Pixel.Core.Tools;

namespace Pixel.App.Tools;

/// <summary>Carries out validated (and, where needed, user-confirmed) tool requests.</summary>
internal sealed class ToolExecutor(Func<IProductivity> productivity)
{
    public (bool Ok, string Text) Execute(ToolRequest request)
    {
        try
        {
            return request switch
            {
                ToolRequest.SetReminder r => (true, productivity().SetReminder(r.In, r.At, r.Message)),
                ToolRequest.StartPomodoro p => (true, productivity().StartPomodoro(p.WorkMinutes, p.BreakMinutes)),
                ToolRequest.StopPomodoro => (true, productivity().StopPomodoro()),
                ToolRequest.AddNote n => (true, productivity().AddNote(n.Text, n.Pinned)),
                ToolRequest.CopyToClipboard c => Copy(c.Text),
                ToolRequest.OpenUrl u => Open(u.Url.AbsoluteUri, "Opened the link."),
                ToolRequest.WebSearch w => Open(w.Url.AbsoluteUri, $"Searched the web for \"{w.Query}\"."),
                ToolRequest.OpenFile f => Open(f.Path, "Opened it."),
                ToolRequest.OpenApp a => Open(a.Name, $"Opened {a.Name}."),
                ToolRequest.Invalid i => (false, i.Error),
                _ => (false, "I can't do that one."),
            };
        }
        catch (Exception e)
        {
            Log.Error("tool failed", e);
            return (false, "Something went wrong doing that.");
        }
    }

    private static (bool, string) Copy(string text)
    {
        Clipboard.SetText(text);
        return (true, "Copied to your clipboard.");
    }

    private static (bool, string) Open(string target, string done)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
            return (true, done);
        }
        catch (Win32Exception)
        {
            return (false, $"I couldn't open \"{target}\" — is it installed?");
        }
    }
}

/// <summary>Placeholder until reminders are wired; keeps tool calls answerable.</summary>
internal sealed class NoProductivity : IProductivity
{
    private const string Soon = "Reminders and notes aren't ready yet.";
    public string SetReminder(TimeSpan? inTime, TimeOnly? at, string message) => Soon;
    public string StartPomodoro(int workMinutes, int breakMinutes) => Soon;
    public string StopPomodoro() => Soon;
    public string AddNote(string text, bool pinned) => Soon;
}
