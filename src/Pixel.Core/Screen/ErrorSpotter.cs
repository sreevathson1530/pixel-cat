using System.Text.RegularExpressions;

namespace Pixel.Core.Screen;

public sealed record ErrorFinding(string Line);

/// <summary>
/// Rule-based error detection on the active window's OCR text. No model involved — cheap enough to run on every
/// screen change. The same error line is reported at most once every two minutes.
/// </summary>
public sealed partial class ErrorSpotter
{
    private static readonly TimeSpan Quiet = TimeSpan.FromMinutes(2);
    private readonly Dictionary<string, DateTimeOffset> _seen = [];

    [GeneratedRegex(
        @"Traceback \(most recent call last\)" +
        @"|\b\w*(Error|Exception)\s?:\s" +
        @"|^\s*(error|fatal)(\[\w+\])?\s?:\s" +
        @"|\berror\s+(CS|TS|C|MSB|NU|LNK)\d{3,5}\b" +
        @"|npm ERR!" +
        @"|\bFAILED\b" +
        @"|is not recognized as an internal or external command" +
        @"|command not found" +
        @"|Unhandled exception" +
        @"|Segmentation fault",
        RegexOptions.Multiline)]
    private static partial Regex Pattern();

    [GeneratedRegex(@"^\s*(error|fatal)(\[\w+\])?\s?:\s", RegexOptions.IgnoreCase)]
    private static partial Regex LeadingWord();

    public ErrorFinding? Scan(ScreenSnapshot snapshot, DateTimeOffset now)
    {
        foreach (var raw in snapshot.ForegroundText.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || !(Pattern().IsMatch(line) || LeadingWord().IsMatch(line))) continue;

            var key = string.Join(' ', line.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
            if (_seen.TryGetValue(key, out var last) && now - last < Quiet) continue;
            _seen[key] = now;
            Prune(now);
            return new ErrorFinding(line);
        }
        return null;
    }

    private void Prune(DateTimeOffset now)
    {
        if (_seen.Count < 200) return;
        foreach (var k in _seen.Where(p => now - p.Value >= Quiet).Select(p => p.Key).ToList()) _seen.Remove(k);
    }
}
