namespace Pixel.Core.Screen;

/// <summary>What was on screen at a moment. Lives in memory only — never written to disk.</summary>
/// <param name="ForegroundText">OCR text inside the active window.</param>
/// <param name="OtherText">OCR text elsewhere on screen.</param>
/// <param name="Screenshot">The whole screen as a downscaled JPEG, for a brain that can see pictures; null when not taken.</param>
public sealed record ScreenSnapshot(
    DateTimeOffset At,
    string WindowTitle,
    string ProcessName,
    string ForegroundText,
    string OtherText,
    byte[]? Screenshot = null);
