using System.Text;
using System.Text.Json;

namespace Pixel.Core.Brain;

public sealed record ToolCallDelta(int Index, string? Id, string? Name, string? ArgumentsFragment);

/// <param name="Progress">Prompt-reading progress (tokens processed, total, served from cache), when the server reports it.</param>
public sealed record SseDelta(
    string? Content,
    IReadOnlyList<ToolCallDelta> ToolCalls,
    string? FinishReason,
    bool Done,
    (int Processed, int Total, int Cache)? Progress = null);

/// <summary>Incremental parser for OpenAI-style <c>text/event-stream</c> chat completion chunks.</summary>
public sealed class SseParser
{
    private readonly StringBuilder _pending = new();

    public IReadOnlyList<SseDelta> Feed(string chunk)
    {
        _pending.Append(chunk);
        var text = _pending.ToString();
        var lastNewline = text.LastIndexOf('\n');
        if (lastNewline < 0) return [];
        _pending.Clear().Append(text, lastNewline + 1, text.Length - lastNewline - 1);

        var result = new List<SseDelta>();
        foreach (var raw in text[..lastNewline].Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue; // comments, event names, blanks
            var payload = line[5..].Trim();
            if (payload == "[DONE]")
            {
                result.Add(new SseDelta(null, [], null, Done: true));
                continue;
            }
            if (Parse(payload) is { } delta) result.Add(delta);
        }
        return result;
    }

    private static SseDelta? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            (int, int, int)? progress = null;
            if (doc.RootElement.TryGetProperty("prompt_progress", out var pp) && pp.ValueKind == JsonValueKind.Object)
                progress = (Int(pp, "processed"), Int(pp, "total"), Int(pp, "cache"));
            if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                return progress is null ? null : new SseDelta(null, [], null, Done: false, progress);
            var choice = choices[0];
            string? content = null, finish = null;
            var calls = new List<ToolCallDelta>();
            if (choice.TryGetProperty("delta", out var delta))
            {
                if (delta.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String) content = c.GetString();
                if (delta.TryGetProperty("tool_calls", out var tc) && tc.ValueKind == JsonValueKind.Array)
                {
                    foreach (var t in tc.EnumerateArray())
                    {
                        var index = t.TryGetProperty("index", out var i) ? i.GetInt32() : 0;
                        var id = t.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String ? idEl.GetString() : null;
                        string? name = null, args = null;
                        if (t.TryGetProperty("function", out var f))
                        {
                            if (f.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String) name = n.GetString();
                            if (f.TryGetProperty("arguments", out var a) && a.ValueKind == JsonValueKind.String) args = a.GetString();
                        }
                        calls.Add(new ToolCallDelta(index, id, name, args));
                    }
                }
            }
            if (choice.TryGetProperty("finish_reason", out var fr) && fr.ValueKind == JsonValueKind.String) finish = fr.GetString();
            return new SseDelta(content, calls, finish, Done: false, progress);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int Int(JsonElement o, string name) =>
        o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;
}

/// <summary>Reassembles streamed tool-call fragments into complete invocations.</summary>
public sealed class ToolCallAccumulator
{
    private readonly SortedDictionary<int, (string? Id, string? Name, StringBuilder Args)> _calls = [];

    public void Add(ToolCallDelta d)
    {
        if (!_calls.TryGetValue(d.Index, out var c)) c = (null, null, new StringBuilder());
        c.Id ??= d.Id;
        c.Name ??= d.Name;
        c.Args.Append(d.ArgumentsFragment);
        _calls[d.Index] = c;
    }

    public IReadOnlyList<ToolInvocation> Build() =>
        _calls.Where(kv => !string.IsNullOrEmpty(kv.Value.Name))
            .Select(kv => new ToolInvocation(
                kv.Value.Id ?? $"call_{kv.Key}",
                kv.Value.Name!,
                kv.Value.Args.Length == 0 ? "{}" : kv.Value.Args.ToString()))
            .ToList();
}

/// <summary>
/// Some model/template combinations write tool calls as <c>&lt;tool_call&gt;{json}&lt;/tool_call&gt;</c> text.
/// This filter hides those tags from the streamed answer and parses them into invocations.
/// </summary>
public sealed class InlineToolCallFilter
{
    private const string Open = "<tool_call>";
    private const string Close = "</tool_call>";
    private readonly StringBuilder _pending = new();
    private readonly List<ToolInvocation> _calls = [];
    private bool _inside;

    public IReadOnlyList<ToolInvocation> Calls => _calls;

    /// <summary>Feeds streamed text; returns the part that is safe to show now.</summary>
    public string Push(string text)
    {
        _pending.Append(text);
        var output = new StringBuilder();
        while (true)
        {
            var s = _pending.ToString();
            if (_inside)
            {
                var end = s.IndexOf(Close, StringComparison.Ordinal);
                if (end < 0) break;
                Parse(s[..end]);
                _pending.Clear().Append(s, end + Close.Length, s.Length - end - Close.Length);
                _inside = false;
                continue;
            }

            var start = s.IndexOf(Open, StringComparison.Ordinal);
            if (start >= 0)
            {
                output.Append(s, 0, start);
                _pending.Clear().Append(s, start + Open.Length, s.Length - start - Open.Length);
                _inside = true;
                continue;
            }

            // Hold back a trailing fragment that could be the start of "<tool_call>".
            var hold = 0;
            for (var k = Math.Min(Open.Length - 1, s.Length); k > 0; k--)
            {
                if (Open.StartsWith(s[^k..], StringComparison.Ordinal))
                {
                    hold = k;
                    break;
                }
            }
            output.Append(s, 0, s.Length - hold);
            _pending.Clear().Append(s, s.Length - hold, hold);
            break;
        }
        return output.ToString();
    }

    /// <summary>End of stream: release anything held back (an unterminated call is shown as text).</summary>
    public string Flush()
    {
        var rest = (_inside ? Open : "") + _pending;
        _pending.Clear();
        _inside = false;
        return rest;
    }

    private void Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json.Trim());
            var root = doc.RootElement;
            var name = root.GetProperty("name").GetString();
            if (string.IsNullOrEmpty(name)) return;
            var args = root.TryGetProperty("arguments", out var a)
                ? a.ValueKind == JsonValueKind.String ? a.GetString() ?? "{}" : a.GetRawText()
                : "{}";
            _calls.Add(new ToolInvocation($"inline_{_calls.Count}", name, args));
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            // Malformed call: ignore it rather than show raw JSON.
        }
    }
}
