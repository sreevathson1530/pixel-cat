using System.Globalization;
using System.Text.RegularExpressions;

namespace Pixel.Core.Tools;

/// <summary>
/// Reads "when" and "what" from the user's own words ("remind me in 10 minutes to drink water"), for when a small
/// model calls set_reminder but forgets the time or just echoes the request back as the message.
/// </summary>
public static partial class ReminderText
{
    // "in 2 hours", "in 1 hour and 30 minutes", "in 45 mins" — always a number, so "in the oven" never counts.
    [GeneratedRegex(@"\bin\s+(?:(?<h>\d+)\s*(?:hours?|hrs?|h)\b(?:\s*(?:and\s+)?(?<m>\d+)\s*(?:minutes?|mins?|m)\b)?|(?<m>\d+)\s*(?:minutes?|mins?|m)\b)", RegexOptions.IgnoreCase)]
    private static partial Regex Duration();

    [GeneratedRegex(@"\bat\s+(?<h>\d{1,2})(?::(?<m>\d{2}))?\s*(?<ap>am|pm|a\.m\.|p\.m\.)?", RegexOptions.IgnoreCase)]
    private static partial Regex Clock();

    [GeneratedRegex(@"\b(?:to|that|about)\s+(?<what>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex What();

    [GeneratedRegex(@"\bremind(?:er)?\b|\bin\s+\d+\s*(?:hours?|hrs?|minutes?|mins?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Echo();

    /// <summary>How long from now ("in 2 hours", "in 1 hour and 30 minutes"), if said.</summary>
    public static TimeSpan? In(string text)
    {
        foreach (Match m in Duration().Matches(text))
        {
            var h = m.Groups["h"].Success ? int.Parse(m.Groups["h"].Value, CultureInfo.InvariantCulture) : 0;
            var min = m.Groups["m"].Success ? int.Parse(m.Groups["m"].Value, CultureInfo.InvariantCulture) : 0;
            var total = h * 60 + min;
            if (total > 0 && total <= 24 * 60) return TimeSpan.FromMinutes(total);
        }
        return null;
    }

    /// <summary>A clock time ("at 17:30", "at 5pm"), if said.</summary>
    public static TimeOnly? At(string text)
    {
        var m = Clock().Match(text);
        if (!m.Success) return null;
        var hour = int.Parse(m.Groups["h"].Value, CultureInfo.InvariantCulture);
        var minute = m.Groups["m"].Success ? int.Parse(m.Groups["m"].Value, CultureInfo.InvariantCulture) : 0;
        var ap = m.Groups["ap"].Value.Replace(".", "").ToLowerInvariant();
        if (ap == "pm" && hour < 12) hour += 12;
        if (ap == "am" && hour == 12) hour = 0;
        if (!m.Groups["m"].Success && ap.Length == 0) return null; // "at 5" alone is too vague
        return hour is >= 0 and < 24 && minute is >= 0 and < 60 ? new TimeOnly(hour, minute) : null;
    }

    /// <summary>What to be reminded of ("…to drink water" → "drink water"), without the time phrase.</summary>
    public static string? Subject(string text)
    {
        var withoutTime = Clock().Replace(Duration().Replace(text, " "), " ");
        var m = What().Match(withoutTime);
        var what = m.Success ? Regex.Replace(m.Groups["what"].Value, @"\s+", " ").Trim().TrimEnd('.', '!', '?') : "";
        return what.Length > 0 ? what : null;
    }

    /// <summary>The model's message just repeats the request ("remind me in 10 minutes") instead of saying what for.</summary>
    public static bool IsEcho(string message) => Echo().IsMatch(message);
}
