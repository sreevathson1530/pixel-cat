using System.Text.Json;
using System.Text.Json.Nodes;

namespace Pixel.Core.Agents;

/// <summary>
/// Adds (or removes) Pixel's hooks in Claude Code's settings.json without touching anything else. Pixel's entries
/// are recognised by a marker header, so re-connecting replaces them instead of duplicating them.
/// </summary>
public static class ClaudeHookConfig
{
    private const string Marker = "X-Pixel-Cat: 1";
    private static readonly (string Event, string Kind)[] Hooks =
        [("UserPromptSubmit", "working"), ("Stop", "done"), ("Notification", "needs-you")];

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>
    /// An empty POST: Claude Code pipes the hook's JSON — including the user's whole prompt — to stdin, and Pixel
    /// only needs to know that something happened, so none of it is forwarded.
    /// </summary>
    public static string Command(int port, string kind) =>
        $"curl -s -m 2 -X POST -H \"{Marker}\" -d \"\" http://127.0.0.1:{port}/event/{kind}";

    /// <exception cref="JsonException">The existing file is not a JSON object — never overwrite it blindly.</exception>
    public static string Merge(string? existingJson, int port)
    {
        var root = ParseObject(existingJson);
        var hooks = root["hooks"] as JsonObject ?? new JsonObject();
        foreach (var (evt, kind) in Hooks)
        {
            var entries = WithoutPixel(hooks[evt] as JsonArray);
            entries.Add(new JsonObject
            {
                ["hooks"] = new JsonArray(new JsonObject { ["type"] = "command", ["command"] = Command(port, kind) }),
            });
            hooks[evt] = entries;
        }
        root["hooks"] = hooks;
        return root.ToJsonString(Indented);
    }

    public static string Remove(string? existingJson)
    {
        var root = ParseObject(existingJson);
        if (root["hooks"] is not JsonObject hooks) return root.ToJsonString(Indented);
        foreach (var (evt, _) in Hooks)
        {
            var entries = WithoutPixel(hooks[evt] as JsonArray);
            if (entries.Count == 0) hooks.Remove(evt);
            else hooks[evt] = entries;
        }
        if (hooks.Count == 0) root.Remove("hooks");
        return root.ToJsonString(Indented);
    }

    public static bool IsInstalled(string? existingJson)
    {
        try
        {
            return ParseObject(existingJson)["hooks"] is JsonObject hooks &&
                   Hooks.All(h => hooks[h.Event] is JsonArray a && a.Any(IsPixelEntry));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static JsonObject ParseObject(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new JsonObject();
        return JsonNode.Parse(json) as JsonObject ?? throw new JsonException("settings.json is not a JSON object");
    }

    /// <summary>A detached copy of the event's entries, minus Pixel's own.</summary>
    private static JsonArray WithoutPixel(JsonArray? entries)
    {
        var kept = new JsonArray();
        foreach (var e in entries ?? [])
            if (e is not null && !IsPixelEntry(e)) kept.Add(e.DeepClone());
        return kept;
    }

    private static bool IsPixelEntry(JsonNode? entry) =>
        entry?["hooks"] is JsonArray inner &&
        inner.Any(h => h?["command"] is JsonValue v && v.TryGetValue<string>(out var c) && c.Contains(Marker, StringComparison.Ordinal));
}
