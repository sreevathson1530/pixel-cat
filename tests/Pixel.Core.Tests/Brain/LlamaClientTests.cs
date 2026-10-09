using System.Net;
using System.Text;
using System.Text.Json;
using Pixel.Core.Brain;

namespace Pixel.Core.Tests.Brain;

public class LlamaClientTests
{
    private sealed class FakeServer(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "text/event-stream") };
        }
    }

    private static string Sse(params string[] json) => string.Concat(json.Select(j => $"data: {j}\n\n")) + "data: [DONE]\n\n";

    private static async Task<List<BrainEvent>> Run(FakeServer server, IReadOnlyList<ChatMessage>? msgs = null, IReadOnlyList<ToolSpec>? tools = null)
    {
        var client = new LlamaClient(new HttpClient(server) { BaseAddress = new Uri("http://127.0.0.1:17860/") });
        var events = new List<BrainEvent>();
        await foreach (var e in client.StreamAsync(msgs ?? [new ChatMessage(ChatRole.User, "hi")], tools, CancellationToken.None))
            events.Add(e);
        return events;
    }

    [Fact]
    public async Task Streams_tokens_then_done()
    {
        var events = await Run(new FakeServer(HttpStatusCode.OK, Sse(
            """{"choices":[{"delta":{"content":"Mrrp"}}]}""",
            """{"choices":[{"delta":{"content":"!"}}]}""")));
        Assert.Equal("Mrrp!", string.Concat(events.OfType<TokenEvent>().Select(t => t.Text)));
        Assert.IsType<DoneEvent>(events[^1]);
    }

    [Fact]
    public async Task Structured_tool_calls_become_tool_call_events()
    {
        var events = await Run(new FakeServer(HttpStatusCode.OK, Sse(
            """{"choices":[{"delta":{"tool_calls":[{"index":0,"id":"c9","function":{"name":"open_url","arguments":"{\"url\":"}}]}}]}""",
            """{"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":"\"https://x.dev\"}"}}]}}]}""")));
        var call = Assert.Single(events.OfType<ToolCallEvent>()).Call;
        Assert.Equal("open_url", call.Name);
        Assert.Equal("""{"url":"https://x.dev"}""", call.ArgumentsJson);
        Assert.IsType<DoneEvent>(events[^1]);
    }

    [Fact]
    public async Task Inline_tool_call_text_is_hidden_and_parsed()
    {
        var events = await Run(new FakeServer(HttpStatusCode.OK, Sse(
            """{"choices":[{"delta":{"content":"On it. <tool_call>{\"name\":\"add_note\",\"arguments\":{\"text\":\"milk\"}}</tool_call>"}}]}""")));
        Assert.Equal("On it. ", string.Concat(events.OfType<TokenEvent>().Select(t => t.Text)));
        Assert.Equal("add_note", Assert.Single(events.OfType<ToolCallEvent>()).Call.Name);
    }

    [Fact]
    public async Task Server_error_becomes_a_friendly_error_event()
    {
        var events = await Run(new FakeServer(HttpStatusCode.InternalServerError, "boom"));
        Assert.IsType<ErrorEvent>(Assert.Single(events));
    }

    [Fact]
    public async Task Request_carries_roles_tool_calls_and_tools()
    {
        var server = new FakeServer(HttpStatusCode.OK, Sse());
        var call = new ToolInvocation("c1", "set_reminder", """{"minutes":5}""");
        await Run(server,
            [
                new ChatMessage(ChatRole.System, "sys"),
                new ChatMessage(ChatRole.User, "remind me"),
                new ChatMessage(ChatRole.Assistant, "", ToolCalls: [call]),
                new ChatMessage(ChatRole.Tool, "done", ToolCallId: "c1"),
            ],
            [new ToolSpec("set_reminder", "Set a reminder", """{"type":"object","properties":{"minutes":{"type":"number"}}}""")]);

        using var doc = JsonDocument.Parse(server.RequestBody!);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("stream").GetBoolean());
        var msgs = root.GetProperty("messages");
        Assert.Equal(["system", "user", "assistant", "tool"], msgs.EnumerateArray().Select(m => m.GetProperty("role").GetString()!).ToArray());
        Assert.Equal("set_reminder", msgs[2].GetProperty("tool_calls")[0].GetProperty("function").GetProperty("name").GetString());
        Assert.Equal("c1", msgs[3].GetProperty("tool_call_id").GetString());
        Assert.Equal("number", root.GetProperty("tools")[0].GetProperty("function").GetProperty("parameters")
            .GetProperty("properties").GetProperty("minutes").GetProperty("type").GetString());
    }
}

public class LlamaClientSecurityTests
{
    private sealed class Capture : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("data: [DONE]\n\n") });
        }
    }

    [Fact]
    public async Task Uses_the_live_endpoint_and_sends_the_api_key()
    {
        var capture = new Capture();
        var client = new LlamaClient(new HttpClient(capture), () => (new Uri("http://127.0.0.1:51234/"), "s3cret"));
        await foreach (var _ in client.StreamAsync([new ChatMessage(ChatRole.User, "hi")], null, CancellationToken.None)) { }

        Assert.Equal("http://127.0.0.1:51234/v1/chat/completions", capture.Request!.RequestUri!.AbsoluteUri);
        Assert.Equal("Bearer", capture.Request.Headers.Authorization!.Scheme);
        Assert.Equal("s3cret", capture.Request.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task No_live_server_is_an_error_not_a_crash()
    {
        var client = new LlamaClient(new HttpClient(new Capture()), () => null);
        var events = new List<BrainEvent>();
        await foreach (var e in client.StreamAsync([new ChatMessage(ChatRole.User, "hi")], null, CancellationToken.None)) events.Add(e);
        Assert.IsType<ErrorEvent>(Assert.Single(events));
    }
}

public class LlamaClientProgressTests
{
    private sealed class Server(string body) : HttpMessageHandler
    {
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            RequestBody = await request.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        }
    }

    [Fact]
    public async Task Prompt_reading_progress_becomes_progress_events_and_is_requested()
    {
        var server = new Server(
            "data: {\"choices\":[{\"delta\":{}}],\"prompt_progress\":{\"total\":1000,\"cache\":0,\"processed\":500,\"time_ms\":1}}\n\n" +
            "data: {\"choices\":[{\"delta\":{\"content\":\"ok\"}}]}\n\ndata: [DONE]\n\n");
        var client = new LlamaClient(new HttpClient(server) { BaseAddress = new Uri("http://127.0.0.1:1/") });
        var events = new List<BrainEvent>();
        await foreach (var e in client.StreamAsync([new ChatMessage(ChatRole.User, "hi")], null, CancellationToken.None)) events.Add(e);

        var p = Assert.Single(events.OfType<ProgressEvent>());
        Assert.Equal(0.5, p.Fraction, 3);
        Assert.Contains("\"return_progress\":true", server.RequestBody);
    }
}
