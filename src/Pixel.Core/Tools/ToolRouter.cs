using System.Text.RegularExpressions;
using Pixel.Core.Brain;

namespace Pixel.Core.Tools;

/// <summary>
/// Picks which tools to offer the brain for a question. A small model offered every tool calls them at random
/// (searching the web instead of answering, setting reminders nobody asked for), so ordinary questions get none and
/// action requests ("remind me…", "open…", "search for…") get only the tools they name.
/// </summary>
public static partial class ToolRouter
{
    [GeneratedRegex(@"\bremind|\breminder|\balarm\b|\bwake me\b", RegexOptions.IgnoreCase)]
    private static partial Regex Reminder();

    [GeneratedRegex(@"\bpomodoro\b|\bfocus (timer|session|mode)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Pomodoro();

    [GeneratedRegex(@"\b(stop|end|cancel|pause)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Stop();

    [GeneratedRegex(@"\bnotes?\b|\bjot\b|\bremember (that|this)\b|\bpin (this|that)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Note();

    [GeneratedRegex(@"\bcopy\b|\bclipboard\b", RegexOptions.IgnoreCase)]
    private static partial Regex Copy();

    [GeneratedRegex(@"\b(open|launch)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Open();

    [GeneratedRegex(@"https?://|\bwww\.|\b[\w-]+\.(com|org|net|io|dev|ai|in|co|edu|gov)\b|\b(website|web page|webpage|site|link)\b", RegexOptions.IgnoreCase)]
    private static partial Regex WebAddress();

    [GeneratedRegex(@"[A-Za-z]:\\|\\\\|\b(file|folder|directory)\b|\.\w{2,4}\b", RegexOptions.IgnoreCase)]
    private static partial Regex FilePath();

    [GeneratedRegex(@"\bsearch\b|\bgoogle\b|\bbing\b|\blook (it |this |that )?up\b|\b(online|on the web|on the internet)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Search();

    public static IReadOnlyList<ToolSpec> For(string question, IReadOnlyList<ToolSpec> all)
    {
        var wanted = new HashSet<string>();
        if (Reminder().IsMatch(question)) wanted.Add("set_reminder");
        if (Pomodoro().IsMatch(question)) wanted.Add(Stop().IsMatch(question) ? "stop_pomodoro" : "start_pomodoro");
        if (Note().IsMatch(question)) wanted.Add("add_note");
        if (Copy().IsMatch(question)) wanted.Add("copy_to_clipboard");
        if (Search().IsMatch(question)) wanted.Add("web_search");
        if (Open().IsMatch(question))
        {
            if (WebAddress().IsMatch(question)) wanted.Add("open_url");
            else if (FilePath().IsMatch(question)) wanted.Add("open_file");
            else wanted.Add("open_app");
        }
        return wanted.Count == 0 ? [] : all.Where(t => wanted.Contains(t.Name)).ToList();
    }
}
