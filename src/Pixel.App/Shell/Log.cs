using System.IO;

namespace Pixel.App.Shell;

/// <summary>Tiny append-only log at %APPDATA%\Pixel\pixel.log (rolled over at 1 MB). Never logs screen text or keys.</summary>
internal static class Log
{
    private static readonly object Gate = new();
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Pixel", "pixel.log");

    public static void Info(string message) => Write("INFO", message);
    public static void Error(string message, Exception? e = null) => Write("ERROR", e is null ? message : $"{message}: {e}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 1_000_000)
                    File.Move(FilePath, FilePath + ".old", overwrite: true);
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {message}{Environment.NewLine}");
            }
        }
        catch (IOException)
        {
            // Logging must never take the cat down.
        }
    }
}
