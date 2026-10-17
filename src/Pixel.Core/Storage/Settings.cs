using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pixel.Core.Storage;

/// <summary>User settings persisted to %APPDATA%\Pixel\settings.json. Only value types/strings so equality is by value.</summary>
public sealed record Settings
{
    public string Palette { get; init; } = "black";
    public int Scale { get; init; } = 3;

    /// <summary>Walk anywhere on screen (true) or only along the floor and window tops (false).</summary>
    public bool FreeRoam { get; init; } = true;
    public bool StartWithWindows { get; init; } = true;
    public double Volume { get; init; } = 0.6;
    public bool Muted { get; init; }
    public string ModelDirectory { get; init; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "pixel");
    public string? ModelPath { get; init; }
    public string LlamaBackend { get; init; } = "cpu";

    /// <summary>CPU threads for llama-server; 0 = automatic.</summary>
    public int LlamaThreads { get; init; }
    public int ContextSize { get; init; } = 8192;

    /// <summary>Extra llama-server flags. q8_0 KV cache halves its memory (needs flash attention).</summary>
    public string LlamaExtraArgs { get; init; } = "-fa on -ctk q8_0 -ctv q8_0";
    /// <summary>
    /// Minutes without questions before the brain is unloaded to give memory back. 0 (the default) keeps it loaded,
    /// so answers start within a second instead of waiting for it to wake up.
    /// </summary>
    public int IdleUnloadMinutes { get; init; }
    public int WaterReminderMinutes { get; init; } = 60;
    public int StretchReminderMinutes { get; init; } = 45;
    public bool ProactiveErrors { get; init; } = true;
    public bool ClaudeHooksInstalled { get; init; }

    /// <summary>Comma-separated process names watched for AI-agent activity (best effort).</summary>
    public string AgentProcesses { get; init; } = "Cursor,Code,codex";
}

public static class SettingsStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Pixel", "settings.json");

    public static Settings Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new Settings();
            return JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), Json) ?? new Settings();
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return new Settings();
        }
    }

    public static void Save(string path, Settings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(settings, Json));
        File.Move(tmp, path, overwrite: true);
    }
}
