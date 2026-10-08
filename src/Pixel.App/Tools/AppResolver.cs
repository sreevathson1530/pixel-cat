using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Microsoft.Win32;
using Pixel.Core.Tools;

namespace Pixel.App.Tools;

/// <summary>An installed app Pixel may launch: what to start, and what it really runs (shown on the card).</summary>
internal sealed record AppTarget(string Name, string LaunchPath, string? TargetExe)
{
    public string Describe() => TargetExe is { Length: > 0 } t && !t.Equals(LaunchPath, StringComparison.OrdinalIgnoreCase)
        ? $"{Name} — runs {t}"
        : $"{Name} — {LaunchPath}";
}

/// <summary>
/// Turns "chrome" or "Visual Studio Code" into something installed: a Start-menu shortcut, an App Paths entry
/// or a System32 built-in. Shortcuts are opened up to see their real target, and any that point at a shell or
/// script host are refused — the name on a shortcut proves nothing.
/// </summary>
internal static class AppResolver
{
    public static AppTarget? Resolve(string name)
    {
        var wanted = name.Trim();
        if (LaunchSafety.CheckApp(wanted) is not null) return null;

        var shortcuts = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
                Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            }
            .Where(Directory.Exists)
            .SelectMany(SafeEnumerate)
            .Select(p => (Path: p, Name: Path.GetFileNameWithoutExtension(p)))
            .Where(s => !s.Name.Contains("uninstall", StringComparison.OrdinalIgnoreCase) && !LaunchSafety.IsBlockedShortcut(s.Name))
            .ToList();

        // Best match first: exact name, then starts-with, then whole word ("code" → "Visual Studio Code"),
        // then any substring ("code" → "OpenCode").
        var word = new System.Text.RegularExpressions.Regex($@"\b{System.Text.RegularExpressions.Regex.Escape(wanted)}\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var candidates = shortcuts.Where(s => s.Name.Equals(wanted, StringComparison.OrdinalIgnoreCase))
            .Concat(shortcuts.Where(s => s.Name.StartsWith(wanted, StringComparison.OrdinalIgnoreCase)).OrderBy(s => s.Name.Length))
            .Concat(shortcuts.Where(s => word.IsMatch(s.Name)).OrderBy(s => s.Name.Length))
            .Concat(shortcuts.Where(s => s.Name.Contains(wanted, StringComparison.OrdinalIgnoreCase)).OrderBy(s => s.Name.Length));
        foreach (var (path, shortcutName) in candidates)
        {
            var target = ShortcutTarget(path);
            if (target is { Length: > 0 } && LaunchSafety.IsBlockedTarget(target)) continue;
            return new AppTarget(shortcutName, path, target);
        }

        var exe = wanted.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? wanted : wanted + ".exe";
        foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var key = root.OpenSubKey($@"Software\Microsoft\Windows\CurrentVersion\App Paths\{exe}");
            if (key?.GetValue(null) is string registered && File.Exists(registered.Trim('"')) && !LaunchSafety.IsBlockedTarget(registered))
                return new AppTarget(wanted, registered.Trim('"'), null);
        }

        // Built-in Windows apps without a shortcut (notepad, calc, mspaint…) live in System32.
        var system = Path.Combine(Environment.SystemDirectory, exe);
        return File.Exists(system) && !LaunchSafety.IsBlockedTarget(system) ? new AppTarget(wanted, system, null) : null;
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

    /// <summary>The executable a .lnk points at (empty for advertised installer shortcuts), or null if unreadable.</summary>
    private static string? ShortcutTarget(string lnk)
    {
        try
        {
            var link = (IShellLinkW)new ShellLink();
            ((IPersistFile)link).Load(lnk, 0);
            var sb = new StringBuilder(1024);
            link.GetPath(sb, sb.Capacity, IntPtr.Zero, 0);
            return sb.ToString();
        }
        catch (COMException)
        {
            return null;
        }
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink;

    /// <summary>Only the leading methods are declared; vtable order matters.</summary>
    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int max, IntPtr findData, uint flags);
    }
}
