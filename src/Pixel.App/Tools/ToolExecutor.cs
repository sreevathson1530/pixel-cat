using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Windows;
using Pixel.App.Shell;
using Pixel.Core.Tools;

namespace Pixel.App.Tools;

/// <summary>
/// An action ready to show and run. What the card displays and what <see cref="Run"/> does come from the same
/// resolution, so the user approves exactly what will happen.
/// </summary>
internal sealed record PreparedAction(string Title, string Detail, bool NeedsConfirm, Func<(bool Ok, string Text)> Run)
{
    public static PreparedAction Refused(string title, string reason) => new(title, reason, false, () => (false, reason));
}

/// <summary>Carries out validated (and, where needed, user-confirmed) tool requests.</summary>
internal sealed class ToolExecutor(Func<IProductivity> productivity)
{
    public PreparedAction Prepare(string toolName, ToolRequest request)
    {
        var (title, detail) = request.Describe();
        var confirm = ToolPolicy.RequiresConfirmation(toolName);
        switch (request)
        {
            case ToolRequest.Invalid i:
                return PreparedAction.Refused(title, i.Error);
            case ToolRequest.OpenApp a:
            {
                if (AppResolver.Resolve(a.Name) is not { } app)
                    return PreparedAction.Refused("Open app", $"I couldn't find an installed app called \"{a.Name}\".");
                return new PreparedAction("Open app", app.Describe(), confirm, () => Open(app.LaunchPath, $"Opened {app.Name}."));
            }
            case ToolRequest.OpenUrl u:
                // Re-check with DNS now: a public-looking name can point back at this PC (localtest.me, nip.io…).
                if (!UrlSafety.TryCheck(u.Url.AbsoluteUri, out var url, out var error, Dns.GetHostAddresses))
                    return PreparedAction.Refused("Open link", error!);
                return new PreparedAction(title, UrlSafety.Display(url!), confirm, () => Open(url!.AbsoluteUri, "Opened the link."));
            default:
                return new PreparedAction(title, detail, confirm, () => Execute(request));
        }
    }

    private (bool Ok, string Text) Execute(ToolRequest request)
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
                ToolRequest.WebSearch w => Open(w.Url.AbsoluteUri, $"Searched the web for \"{w.Query}\"."),
                ToolRequest.OpenFile f => Open(f.Path, "Opened it."),
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
