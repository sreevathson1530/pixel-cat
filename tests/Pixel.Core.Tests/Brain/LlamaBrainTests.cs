using System.Net;
using System.Text;
using Pixel.Core.Brain;

namespace Pixel.Core.Tests.Brain;

public sealed class LlamaBrainTests : IDisposable
{
    private readonly string _model = Path.GetTempFileName();
    public void Dispose() => File.Delete(_model);

    private sealed class Proc : IServerProcess
    {
        public bool HasExited { get; set; }
        public void Kill() => HasExited = true;
    }

    private sealed class Launcher : IServerLauncher
    {
        public int Launches;
        public IServerProcess Launch(ServerOptions o)
        {
            Launches++;
            return new Proc();
        }
    }

    private sealed class AlwaysReady : IHealthProbe
    {
        public Task<bool> IsReadyAsync(CancellationToken ct) => Task.FromResult(true);
    }

    private sealed class Sse : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("data: {\"choices\":[{\"delta\":{\"content\":\"purr\"}}]}\n\ndata: [DONE]\n\n", Encoding.UTF8),
            });
    }

    private (LlamaBrain Brain, Launcher Launcher, List<string> Asked) Make(
        ModelStatus status, MemoryVerdict verdict, bool userAccepts = true)
    {
        var launcher = new Launcher();
        var server = new ServerManager(launcher, new AlwaysReady(), () => DateTimeOffset.Now, TimeSpan.FromMinutes(10), TimeSpan.Zero);
        var asked = new List<string>();
        var brain = new LlamaBrain(
            server,
            () => status,
            path => new ServerOptions("llama-server.exe", path, 17860, 8192, 4, 0),
            _ => verdict,
            msg =>
            {
                asked.Add(msg);
                return Task.FromResult(userAccepts);
            },
            new LlamaClient(new HttpClient(new Sse()) { BaseAddress = new Uri("http://127.0.0.1:17860/") }),
            [],
            () => DateTimeOffset.Now);
        return (brain, launcher, asked);
    }

    private static async Task<List<BrainEvent>> Ask(LlamaBrain b)
    {
        var list = new List<BrainEvent>();
        await foreach (var e in b.AskAsync([new ChatMessage(ChatRole.User, "hi")], null, CancellationToken.None)) list.Add(e);
        return list;
    }

    [Fact]
    public async Task Missing_model_says_where_one_belongs_so_the_chat_can_offer_a_download()
    {
        var (b, l, _) = Make(new ModelStatus.Missing(@"C:\models"), new MemoryVerdict.Ok());
        var e = Assert.IsType<ModelMissingEvent>(Assert.Single(await Ask(b)));
        Assert.Equal(@"C:\models", e.Directory);
        Assert.Equal(0, l.Launches);
    }

    [Fact]
    public async Task Still_downloading_model_says_so()
    {
        var (b, _, _) = Make(new ModelStatus.Downloading("x.crdownload"), new MemoryVerdict.Ok());
        Assert.Contains("download", Assert.IsType<ErrorEvent>(Assert.Single(await Ask(b))).Message);
    }

    [Fact]
    public async Task Too_little_memory_refuses_without_launching()
    {
        var (b, l, _) = Make(new ModelStatus.Found(_model), new MemoryVerdict.TooLow("close apps"));
        Assert.IsType<ErrorEvent>(Assert.Single(await Ask(b)));
        Assert.Equal(0, l.Launches);
    }

    [Fact]
    public async Task Slow_memory_asks_first_and_respects_no()
    {
        var (b, l, asked) = Make(new ModelStatus.Found(_model), new MemoryVerdict.Slow("will be slow"), userAccepts: false);
        Assert.IsType<ErrorEvent>((await Ask(b))[^1]);
        Assert.Equal(["will be slow"], asked);
        Assert.Equal(0, l.Launches);
    }

    [Fact]
    public async Task Healthy_path_wakes_the_brain_and_streams_the_answer()
    {
        var (b, l, _) = Make(new ModelStatus.Found(_model), new MemoryVerdict.Ok());
        var events = await Ask(b);
        Assert.Contains(events, e => e is StatusEvent s && s.Text.Contains("waking"));
        Assert.Equal("purr", string.Concat(events.OfType<TokenEvent>().Select(t => t.Text)));
        Assert.IsType<DoneEvent>(events[^1]);
        Assert.Equal(1, l.Launches);

        await Ask(b);
        Assert.Equal(1, l.Launches); // stays loaded between questions
    }
}
