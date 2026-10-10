using System.Text.RegularExpressions;

namespace Pixel.Core.Screen;

/// <summary>What was on screen at a moment. Lives in memory only — never written to disk.</summary>
/// <param name="ForegroundText">OCR text inside the active window.</param>
/// <param name="OtherText">OCR text elsewhere on screen.</param>
/// <param name="Screenshot">The whole screen as a downscaled JPEG, for a brain that can see pictures; null when not taken.</param>
public sealed partial record ScreenSnapshot(
    DateTimeOffset At,
    string WindowTitle,
    string ProcessName,
    string ForegroundText,
    string OtherText,
    byte[]? Screenshot = null)
{
    /// <summary>
    /// The same screen as far as a question is concerned: same window and same words. Clock times are ignored —
    /// the taskbar clock ticks every minute without anything you're looking at changing.
    /// </summary>
    public bool SameScreen(ScreenSnapshot other) =>
        WindowTitle == other.WindowTitle
        && ProcessName == other.ProcessName
        && Clock().Replace(ForegroundText, "#") == Clock().Replace(other.ForegroundText, "#")
        && Clock().Replace(OtherText, "#") == Clock().Replace(other.OtherText, "#");

    [GeneratedRegex(@"\d{1,2}[:.]\d{2}(?:[:.]\d{2})?(?:\s?[AaPp]\.?[Mm]\.?)?")]
    private static partial Regex Clock();
}
