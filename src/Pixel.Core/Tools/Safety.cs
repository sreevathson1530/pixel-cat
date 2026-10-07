using System.Net;
using System.Net.Sockets;

namespace Pixel.Core.Tools;

/// <summary>
/// Which web links Pixel will open. Model output can be steered by text on screen (prompt injection), so links
/// must not hide their real host (user@host) and must not reach this PC or the local network.
/// </summary>
public static class UrlSafety
{
    private static readonly string[] LocalSuffixes = [".localhost", ".local", ".internal", ".lan", ".home.arpa", ".corp"];

    /// <param name="resolve">Optional DNS lookup; names resolving to private addresses (e.g. localtest.me) are refused.</param>
    public static bool TryCheck(string? url, out Uri? uri, out string? error, Func<string, IPAddress[]>? resolve = null)
    {
        uri = null;
        error = null;
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var u) || u.Scheme is not ("http" or "https"))
        {
            error = "I only open http(s) web links.";
            return false;
        }
        if (!string.IsNullOrEmpty(u.UserInfo))
        {
            error = "That link hides its real address, so I won't open it.";
            return false;
        }
        if (IsLocal(u) || ResolvesLocal(u, resolve))
        {
            error = "That link points at this PC or your local network, so I won't open it.";
            return false;
        }
        uri = u;
        return true;
    }

    /// <summary>Real (punycode) host first, then the full address — what the user approves.</summary>
    public static string Display(Uri uri) => $"{uri.IdnHost} — {uri.AbsoluteUri}";

    private static bool ResolvesLocal(Uri u, Func<string, IPAddress[]>? resolve)
    {
        if (resolve is null) return false;
        try
        {
            return resolve(u.IdnHost).Any(IsPrivate);
        }
        catch (SocketException)
        {
            return false; // unresolvable: the browser will fail on its own
        }
    }

    private static bool IsLocal(Uri u)
    {
        var host = u.IdnHost.Trim('[', ']').TrimEnd('.').ToLowerInvariant();
        if (IPAddress.TryParse(host, out var ip)) return IsPrivate(ip);
        // Numeric-looking hosts (hex/octal/decimal IPv4 spellings) that aren't plain addresses: refuse.
        if (host.All(c => char.IsAsciiHexDigit(c) || c is 'x' or '.')) return true;
        if (host == "localhost" || !host.Contains('.')) return true; // single-label names resolve on the intranet
        return LocalSuffixes.Any(s => host.EndsWith(s, StringComparison.Ordinal));
    }

    private static bool IsPrivate(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any)) return true;
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            return ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6UniqueLocal;
        var b = ip.GetAddressBytes();
        return b[0] == 10
               || b[0] == 127
               || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
               || (b[0] == 192 && b[1] == 168)
               || (b[0] == 169 && b[1] == 254)
               || (b[0] == 100 && b[1] >= 64 && b[1] <= 127) // carrier-grade NAT
               || b[0] == 0;
    }
}

/// <summary>Which files and apps Pixel will open. Never programs, scripts, shells or protocol handlers.</summary>
public static class LaunchSafety
{
    /// <summary>Allow-list: plain documents, media and folders. Everything else (macros, scripts, images of disks…) is refused.</summary>
    private static readonly HashSet<string> DocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".log", ".csv", ".pdf", ".rtf", ".docx", ".xlsx", ".pptx", ".odt", ".ods", ".odp",
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".heic", ".tif", ".tiff",
        ".mp3", ".wav", ".flac", ".m4a", ".ogg", ".mp4", ".mkv", ".mov", ".avi", ".webm",
    };

    private static readonly HashSet<string> BlockedApps = new(StringComparer.OrdinalIgnoreCase)
    {
        "cmd", "command prompt", "powershell", "windows powershell", "powershell_ise", "pwsh", "wt", "windows terminal",
        "terminal", "mshta", "rundll32", "regsvr32", "wscript", "cscript", "msiexec", "certutil", "bitsadmin", "reg",
        "regedit", "registry editor", "schtasks", "wmic", "bash", "wsl", "sh", "conhost", "msbuild", "installutil",
        "forfiles", "curl", "wget", "ftp", "sc", "net", "netsh", "taskkill", "shutdown", "cipher", "format", "diskpart",
        "bcdedit", "vssadmin", "mmc", "hh", "odbcconf", "msdt", "cmstp", "scriptrunner", "ssh", "python", "pythonw", "py",
        "node", "java", "javaw", "perl", "ruby", "control", "eventvwr", "gpedit", "secpol", "taskschd", "compmgmt",
    };

    private static readonly char[] UnsafeAppChars = ['\\', '/', '"', '&', '|', '>', '<', '^', ':', '%', '`', '\'', ';', '!', '\n', '\r', '\t'];

    public static string? CheckFile(string path)
    {
        var p = path.Trim().TrimEnd('.', ' ');
        if (p.StartsWith(@"\\", StringComparison.Ordinal) || p.StartsWith("//", StringComparison.Ordinal))
            return "I don't open files on network shares.";
        if (p.IndexOf(':', 2) >= 0) return "That path looks unusual, so I won't open it.";
        var ext = Path.GetExtension(p);
        if (ext.Length == 0) return null; // folder
        return DocumentExtensions.Contains(ext) ? null : "For safety I only open documents, pictures, media and folders.";
    }

    public static string? CheckApp(string name)
    {
        var n = name.Trim();
        if (n.Length is 0 or > 60 || n.IndexOfAny(UnsafeAppChars) >= 0) return "That doesn't look like an app name.";
        var bare = n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? n[..^4] : n;
        return BlockedApps.Contains(bare) ? $"I don't open {n} — command shells and system tools are off-limits for me." : null;
    }

    /// <summary>Also applied to resolved Start-menu shortcut names (e.g. "Windows PowerShell ISE").</summary>
    public static bool IsBlockedShortcut(string shortcutName) =>
        BlockedApps.Contains(shortcutName) ||
        shortcutName.Contains("PowerShell", StringComparison.OrdinalIgnoreCase) ||
        shortcutName.Contains("Command Prompt", StringComparison.OrdinalIgnoreCase) ||
        shortcutName.Contains("Terminal", StringComparison.OrdinalIgnoreCase) ||
        shortcutName.Contains("Registry", StringComparison.OrdinalIgnoreCase);

    /// <summary>A shortcut's target executable is a shell or script host.</summary>
    public static bool IsBlockedTarget(string targetPath) =>
        BlockedApps.Contains(Path.GetFileNameWithoutExtension(targetPath.Trim('"')));
}
