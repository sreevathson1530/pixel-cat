using System.Globalization;
using System.Text.Json;
using Pixel.Core.Brain;

namespace Pixel.Core.Tools;

/// <summary>The actions Pixel can take, as JSON-schema tool specs for the model.</summary>
public static class ToolDefinitions
{
    public static IReadOnlyList<ToolSpec> All { get; } =
    [
        new("set_reminder", "Only when the user asks to be reminded: remind them later with a meow and a message. Give either minutes from now or a 24h clock time.",
            """{"type":"object","properties":{"minutes":{"type":"number","description":"minutes from now"},"at":{"type":"string","description":"24h time HH:mm"},"message":{"type":"string"}},"required":["message"]}"""),
        new("start_pomodoro", "Only when the user asks for a Pomodoro or focus timer: start one.",
            """{"type":"object","properties":{"work_minutes":{"type":"number"},"break_minutes":{"type":"number"}}}"""),
        new("stop_pomodoro", "Only when the user asks to stop the Pomodoro: stop the running timer.", """{"type":"object","properties":{}}"""),
        new("add_note", "Only when the user asks to save or remember a note: save it; pinned notes stay visible on the desktop.",
            """{"type":"object","properties":{"text":{"type":"string"},"pinned":{"type":"boolean"}},"required":["text"]}"""),
        new("copy_to_clipboard", "Only when the user asks to copy something: copy that text (for example code) to the clipboard.",
            """{"type":"object","properties":{"text":{"type":"string"}},"required":["text"]}"""),
        new("open_url", "Only when the user asks to open a web page: open it in the default browser.",
            """{"type":"object","properties":{"url":{"type":"string"}},"required":["url"]}"""),
        new("open_app", "Only when the user asks to open an app: open it by name, e.g. notepad, calc, chrome, code.",
            """{"type":"object","properties":{"name":{"type":"string"}},"required":["name"]}"""),
        new("open_file", "Only when the user asks to open a file or folder: open it by full path.",
            """{"type":"object","properties":{"path":{"type":"string"}},"required":["path"]}"""),
        new("web_search", "Only when the user explicitly asks to search the web or look something up online: open that search in their browser. Never use it to answer a question - answer questions yourself.",
            """{"type":"object","properties":{"query":{"type":"string"}},"required":["query"]}"""),
    ];
}

public static class ToolPolicy
{
    /// <summary>
    /// Only actions that show nothing outside the chat run without asking. Reminders are confirmed too: their
    /// text pops up later, and text on screen could plant one that impersonates a real warning — no link
    /// detector catches every disguise, so the user approves the exact words once.
    /// </summary>
    private static readonly HashSet<string> Harmless = ["start_pomodoro", "stop_pomodoro", "add_note"];

    /// <summary>Anything that touches the clipboard, launches something, goes online or speaks later needs a "Do it?" first.</summary>
    public static bool RequiresConfirmation(string tool) => !Harmless.Contains(tool);

    /// <summary>Per-request policy: a pinned note stays visible on the desktop, so it is confirmed like a reminder.</summary>
    public static bool RequiresConfirmation(ToolRequest request, string tool) => request switch
    {
        ToolRequest.AddNote { Pinned: true } => true,
        _ => RequiresConfirmation(tool),
    };
}

/// <summary>A validated tool call.</summary>
public abstract record ToolRequest
{
    public sealed record SetReminder(TimeSpan? In, TimeOnly? At, string Message) : ToolRequest;
    public sealed record StartPomodoro(int WorkMinutes, int BreakMinutes) : ToolRequest;
    public sealed record StopPomodoro : ToolRequest;
    public sealed record AddNote(string Text, bool Pinned) : ToolRequest;
    public sealed record CopyToClipboard(string Text) : ToolRequest;
    public sealed record OpenUrl(Uri Url) : ToolRequest;
    public sealed record OpenApp(string Name) : ToolRequest;
    public sealed record OpenFile(string Path) : ToolRequest;
    public sealed record WebSearch(string Query, Uri Url) : ToolRequest;
    public sealed record Invalid(string Error) : ToolRequest;

    /// <summary>Title and detail for the chat's action card.</summary>
    public (string Title, string Detail) Describe() => this switch
    {
        SetReminder r => ("Set a reminder", (r.In is { } i ? $"in {i.TotalMinutes:0.#} min" : $"at {r.At?.ToString("HH:mm", CultureInfo.InvariantCulture)}") + $": {r.Message}"),
        StartPomodoro p => ("Start Pomodoro", $"{p.WorkMinutes} min focus / {p.BreakMinutes} min break"),
        StopPomodoro => ("Stop Pomodoro", ""),
        AddNote n => (n.Pinned ? "Pin a note" : "Save a note", Short(n.Text)),
        CopyToClipboard c => ("Copy to clipboard", c.Text), // the whole text: you approve exactly what gets copied
        OpenUrl u => ("Open link", UrlSafety.Display(u.Url)),
        OpenApp a => ("Open app", a.Name),
        OpenFile f => ("Open file", f.Path),
        WebSearch w => ("Search the web", w.Query),
        Invalid e => ("Couldn't do that", e.Error),
        _ => ("Action", ""),
    };

    private static string Short(string s) => s.Length <= 120 ? s : s[..117] + "…";
}

/// <summary>Turns the model's raw tool calls into validated requests.</summary>
public static class ToolArgs
{

    /// <param name="asked">The user's question, used to fill in what a small model left out (e.g. a reminder's time).</param>
    public static ToolRequest Parse(ToolInvocation call, Func<string, bool> pathExists, string? asked = null)
    {
        JsonElement a;
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(call.ArgumentsJson) ? "{}" : call.ArgumentsJson);
            a = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return new ToolRequest.Invalid("I couldn't understand the details for that action.");
        }
        if (a.ValueKind != JsonValueKind.Object) return new ToolRequest.Invalid("I couldn't understand the details for that action.");

        switch (call.Name)
        {
            case "set_reminder":
            {
                var message = Str(a, "message") is { Length: > 0 } m ? CleanReminder(m) : "";
                if (asked is not null && (message.Length == 0 || ReminderText.IsEcho(message)) && ReminderText.Subject(asked) is { } what)
                    message = CleanReminder(what);
                if (message.Length == 0) message = "Reminder";
                if (Num(a, "minutes") is { } min && min > 0 && min <= 24 * 60)
                    return new ToolRequest.SetReminder(TimeSpan.FromMinutes(min), null, message);
                if (Str(a, "at") is { } at && TimeOnly.TryParseExact(at, ["HH:mm", "H:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var t))
                    return new ToolRequest.SetReminder(null, t, message);
                // The model forgot the time: take it from the user's own words.
                if (asked is not null && ReminderText.In(asked) is { } inn) return new ToolRequest.SetReminder(inn, null, message);
                if (asked is not null && ReminderText.At(asked) is { } clock) return new ToolRequest.SetReminder(null, clock, message);
                return new ToolRequest.Invalid("A reminder needs minutes from now or a time like 17:30.");
            }
            case "start_pomodoro":
                return new ToolRequest.StartPomodoro(
                    (int)Math.Clamp(Num(a, "work_minutes") ?? 25, 1, 180),
                    (int)Math.Clamp(Num(a, "break_minutes") ?? 5, 1, 60));
            case "stop_pomodoro":
                return new ToolRequest.StopPomodoro();
            case "add_note":
                return Str(a, "text") is { Length: > 0 } text
                    ? new ToolRequest.AddNote(text, a.TryGetProperty("pinned", out var p) && p.ValueKind == JsonValueKind.True)
                    : new ToolRequest.Invalid("The note was empty.");
            case "copy_to_clipboard":
                return Str(a, "text") is { Length: > 0 } copy
                    ? new ToolRequest.CopyToClipboard(copy)
                    : new ToolRequest.Invalid("Nothing to copy.");
            case "open_url":
                return UrlSafety.TryCheck(Str(a, "url"), out var u, out var urlError)
                    ? new ToolRequest.OpenUrl(u!)
                    : new ToolRequest.Invalid(urlError!);
            case "open_app":
            {
                if (Str(a, "name") is not { Length: > 0 } name) return new ToolRequest.Invalid("Which app?");
                return LaunchSafety.CheckApp(name) is { } appError ? new ToolRequest.Invalid(appError) : new ToolRequest.OpenApp(name);
            }
            case "open_file":
            {
                if (Str(a, "path") is not { Length: > 0 } path) return new ToolRequest.Invalid("Which file?");
                if (LaunchSafety.CheckFile(path) is { } fileError) return new ToolRequest.Invalid(fileError);
                return pathExists(path) ? new ToolRequest.OpenFile(path) : new ToolRequest.Invalid("I couldn't find that file.");
            }
            case "web_search":
                return Str(a, "query") is { Length: > 0 } q
                    ? new ToolRequest.WebSearch(q, new Uri("https://www.google.com/search?q=" + Uri.EscapeDataString(q)))
                    : new ToolRequest.Invalid("The search was empty.");
            default:
                return new ToolRequest.Invalid($"I don't know how to \"{call.Name}\".");
        }
    }

    /// <summary>Reminder text appears later in a bubble or notification: one short line, no control characters.</summary>
    private static string CleanReminder(string text)
    {
        var clean = new string(text.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return clean.Length <= 140 ? clean : clean[..139] + "…";
    }

    private static string? Str(JsonElement o, string name) =>
        o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()?.Trim() : null;

    private static double? Num(JsonElement o, string name)
    {
        if (!o.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number) return v.GetDouble();
        if (v.ValueKind == JsonValueKind.String &&
            double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return d;
        return null;
    }
}
