using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace Pixel.Core.Brain;

/// <summary>A function the model may call. <see cref="ParametersJson"/> is a JSON Schema object.</summary>
public sealed record ToolSpec(string Name, string Description, string ParametersJson);

/// <summary>Streams chat completions from llama-server's OpenAI-compatible endpoint as <see cref="BrainEvent"/>s.</summary>
/// <param name="endpoint">Live server address and API key; null when no server is running. Defaults to the client's BaseAddress.</param>
public sealed class LlamaClient(HttpClient http, Func<(Uri BaseUri, string? ApiKey)?>? endpoint = null)
{
    public async IAsyncEnumerable<BrainEvent> StreamAsync(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolSpec>? tools,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var (response, failure) = await SendAsync(BuildRequest(messages, tools), ct);
        if (failure is not null)
        {
            yield return new ErrorEvent(failure);
            yield break;
        }

        using (response)
        {
            var parser = new SseParser();
            var filter = new InlineToolCallFilter();
            var calls = new ToolCallAccumulator();
            using var reader = new StreamReader(await response!.Content.ReadAsStreamAsync(ct), Encoding.UTF8);
            var buffer = new char[2048];
            var done = false;
            while (!done)
            {
                var n = await reader.ReadAsync(buffer.AsMemory(), ct);
                if (n == 0) break;
                foreach (var delta in parser.Feed(new string(buffer, 0, n)))
                {
                    if (delta.Done)
                    {
                        done = true;
                        break;
                    }
                    if (delta.Progress is var (processed, total, _) && total > 0)
                        yield return new ProgressEvent(processed, total);
                    foreach (var t in delta.ToolCalls) calls.Add(t);
                    if (delta.Content is { Length: > 0 } text && filter.Push(text) is { Length: > 0 } visible)
                        yield return new TokenEvent(visible);
                }
            }

            if (filter.Flush() is { Length: > 0 } rest) yield return new TokenEvent(rest);
            foreach (var call in calls.Build().Concat(filter.Calls)) yield return new ToolCallEvent(call);
            yield return new DoneEvent();
        }
    }

    /// <summary>
    /// Has the server read <paramref name="messages"/> into its cache without really answering, so a question that
    /// starts the same way only has to read what's new. Returns false if the server couldn't be reached.
    /// </summary>
    public async Task<bool> PrefillAsync(IReadOnlyList<ChatMessage> messages, IReadOnlyList<ToolSpec>? tools, CancellationToken ct)
    {
        var (response, failure) = await SendAsync(BuildRequest(messages, tools, maxTokens: 1), ct);
        if (failure is not null) return false;
        using (response) await response!.Content.ReadAsStringAsync(ct);
        return true;
    }

    private async Task<(HttpResponseMessage? Response, string? Failure)> SendAsync(string body, CancellationToken ct)
    {
        var target = endpoint is null ? (http.BaseAddress!, null) : endpoint();
        if (target is not var (baseUri, apiKey)) return (null, "My brain isn't running right now — ask me again to wake it up.");
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, "v1/chat/completions"))
            {
                Content = new StringContent(body, Encoding.UTF8, new MediaTypeHeaderValue("application/json")),
            };
            if (apiKey is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (response.IsSuccessStatusCode) return (response, null);
            var status = (int)response.StatusCode;
            response.Dispose();
            return (null, $"My brain hit an error ({status}). Try again, or start a new chat if the conversation got very long.");
        }
        catch (HttpRequestException)
        {
            return (null, "I couldn't reach my brain. It may have stopped — try again in a moment.");
        }
    }

    /// <param name="maxTokens">Cap on the answer; set only for a cache warm-up, which isn't streamed.</param>
    public static string BuildRequest(IReadOnlyList<ChatMessage> messages, IReadOnlyList<ToolSpec>? tools, int? maxTokens = null)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteBoolean("stream", maxTokens is null);
            if (maxTokens is { } max) w.WriteNumber("max_tokens", max);
            w.WriteNumber("temperature", 0.4);
            w.WriteBoolean("cache_prompt", true);
            w.WriteBoolean("return_progress", true);
            w.WriteStartArray("messages");
            foreach (var m in messages)
            {
                w.WriteStartObject();
                w.WriteString("role", m.Role switch
                {
                    ChatRole.System => "system",
                    ChatRole.Assistant => "assistant",
                    ChatRole.Tool => "tool",
                    _ => "user",
                });
                if (m.ImageJpeg is { Length: > 0 } image)
                {
                    // OpenAI-style parts: the picture first, then the words about it.
                    w.WriteStartArray("content");
                    w.WriteStartObject();
                    w.WriteString("type", "image_url");
                    w.WriteStartObject("image_url");
                    w.WriteString("url", "data:image/jpeg;base64," + Convert.ToBase64String(image));
                    w.WriteEndObject();
                    w.WriteEndObject();
                    w.WriteStartObject();
                    w.WriteString("type", "text");
                    w.WriteString("text", m.Content);
                    w.WriteEndObject();
                    w.WriteEndArray();
                }
                else
                {
                    w.WriteString("content", m.Content);
                }
                if (m.ToolCallId is not null) w.WriteString("tool_call_id", m.ToolCallId);
                if (m.ToolCalls is { Count: > 0 })
                {
                    w.WriteStartArray("tool_calls");
                    foreach (var c in m.ToolCalls)
                    {
                        w.WriteStartObject();
                        w.WriteString("id", c.Id);
                        w.WriteString("type", "function");
                        w.WriteStartObject("function");
                        w.WriteString("name", c.Name);
                        w.WriteString("arguments", c.ArgumentsJson);
                        w.WriteEndObject();
                        w.WriteEndObject();
                    }
                    w.WriteEndArray();
                }
                w.WriteEndObject();
            }
            w.WriteEndArray();

            if (tools is { Count: > 0 })
            {
                w.WriteStartArray("tools");
                foreach (var t in tools)
                {
                    w.WriteStartObject();
                    w.WriteString("type", "function");
                    w.WriteStartObject("function");
                    w.WriteString("name", t.Name);
                    w.WriteString("description", t.Description);
                    w.WritePropertyName("parameters");
                    w.WriteRawValue(t.ParametersJson);
                    w.WriteEndObject();
                    w.WriteEndObject();
                }
                w.WriteEndArray();
            }
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }
}
