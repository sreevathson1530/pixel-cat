using System.IO;
using Microsoft.Win32;
using Pixel.Core.Tools;

namespace Pixel.App.Tools;

/// <summary>
/// Turns "chrome" or "Visual Studio Code" into something installed: a Start-menu shortcut or an App Paths entry.
/// Anything not installed (or a shell/system tool) is refused rather than handed to ShellExecute blindly.
/// </summary>
internal static class AppResolver
{
    public static string? Resolve(string name)
    {
        var wanted = name.Trim();
        var shortcuts = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
                Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            }
            .Where(Directory.Exists)
            .SelectMany(d => SafeEnumerate(d))
            .Select(p => (Path: p, Name: Path.GetFileNameWithoutExtension(p)))
            .Where(s => !s.Name.Contains("uninstall", StringComparison.OrdinalIgnoreCase) && !LaunchSafety.IsBlockedShortcut(s.Name))
            .ToList();

        var match = shortcuts.FirstOrDefault(s => s.Name.Equals(wanted, StringComparison.OrdinalIgnoreCase)).Path
                    ?? shortcuts.Where(s => s.Name.StartsWith(wanted, StringComparison.OrdinalIgnoreCase)).OrderBy(s => s.Name.Length).FirstOrDefault().Path
                    ?? shortcuts.Where(s => s.Name.Contains(wanted, StringComparison.OrdinalIgnoreCase)).OrderBy(s => s.Name.Length).FirstOrDefault().Path;
        if (match is not null) return match;

        var exe = wanted.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? wanted : wanted + ".exe";
        foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var key = root.OpenSubKey($@"Software\Microsoft\Windows\CurrentVersion\App Paths\{exe}");
            if (key?.GetValue(null) is string path && File.Exists(path.Trim('"'))) return path.Trim('"');
        }

        // Built-in Windows apps that have no shortcut (notepad, calc, mspaint…) live in System32.
        var system = Path.Combine(Environment.SystemDirectory, exe);
        return File.Exists(system) && LaunchSafety.CheckApp(Path.GetFileNameWithoutExtension(exe)) is null ? system : null;
    }

    private static IEnumerable<string> SafeEnumerate(string dir)
    {
        try
        {
            return Directory.EnumerateFiles(dir, "*.lnk", SearchOption.AllDirectories).ToList();
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException)
        {
            return [];
        }
    }
}
