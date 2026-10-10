using System.Net;
using System.Text;
using System.Text.Json;
using Pixel.Core.Brain;
using Pixel.Core.Screen;

namespace Pixel.Core.Tests.Brain;

public class VisionPromptTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 17, 0, 0, TimeSpan.FromHours(5.5));
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3];
    private static ChatMessage U(string t) => new(ChatRole.User, t);
    private static ChatMessage A(string t) => new(ChatRole.Assistant, t);

    private static ScreenSnapshot Desktop(string words = "") =>
        new(Now, "Desktop (wallpaper and icons)", "explorer", words, "", Jpeg);

    [Fact]
    public void Vision_brain_gets_the_screenshot_on_the_newest_question()
    {
        var msgs = PromptBuilder.Build([U("who is that person?")], Desktop(), Now, vision: true);
        Assert.Same(Jpeg, msgs[^1].ImageJpeg);
        Assert.Contains("Screenshot: attached", msgs[^1].Content);
        Assert.Equal(PromptBuilder.VisionPersona, msgs[0].Content);
    }

    [Fact]
    public void Text_brain_never_gets_pictures()
    {
        var msgs = PromptBuilder.Build([U("what's this?")], Desktop("Recycle Bin"), Now, vision: false);
        Assert.All(msgs, m => Assert.Null(m.ImageJpeg));
        Assert.DoesNotContain("Screenshot", msgs[^1].Content);
        Assert.Equal(PromptBuilder.Persona, msgs[0].Content);
    }

    [Fact]
    public void Earlier_questions_never_carry_old_screenshots()
    {
        var convo = new List<ChatMessage> { U("first") with { ImageJpeg = Jpeg }, A("answer"), U("second") };
        var msgs = PromptBuilder.Build(convo, Desktop(), Now, vision: true);
        Assert.Null(msgs[1].ImageJpeg);
        Assert.Same(Jpeg, msgs[^1].ImageJpeg);
    }

    [Fact]
    public void Screenshot_room_is_taken_from_the_text_budget()
    {
        var convo = new List<ChatMessage>();
        for (var i = 0; i < 20; i++)
        {
            convo.Add(U($"q{i} " + new string('x', 1500)));
            convo.Add(A($"a{i} " + new string('y', 1500)));
        }
        convo.Add(U("latest"));
        var msgs = PromptBuilder.Build(convo, Desktop(), Now, vision: true);
        Assert.True(msgs.Sum(m => m.Content.Length) + PromptBuilder.ScreenshotChars <= PromptBuilder.MaxPromptChars);
    }

    [Fact]
    public void A_screen_without_words_says_so()
    {
        var msgs = PromptBuilder.Build([U("what's on my wallpaper?")], Desktop(), Now, vision: true);
        Assert.Contains("(no readable words on screen)", msgs[^1].Content);
        Assert.Contains("Desktop (wallpaper and icons)", msgs[^1].Content);
    }

    [Fact]
    public void Screen_switched_off_is_stated_rather_than_silent()
    {
        var msgs = PromptBuilder.Build([U("hi")], null, Now, vision: true);
        Assert.Contains("[Screen: not shared", msgs[^1].Content);
        Assert.Null(msgs[^1].ImageJpeg);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Persona_claims_sight_and_forbids_saying_it_cannot_see(bool vision)
    {
        var persona = vision ? PromptBuilder.VisionPersona : PromptBuilder.Persona;
        Assert.Contains("Never say you can't see the screen", persona);
        Assert.DoesNotContain("say you can't see it instead", persona);
        Assert.Contains("not you", persona); // "that person" is on screen, not Pixel
    }

    [Fact]
    public void Only_the_vision_persona_mentions_the_screenshot()
    {
        Assert.Contains("screenshot", PromptBuilder.VisionPersona);
        Assert.Contains("vision brain", PromptBuilder.Persona);
    }
}

public class VisionRequestTests
{
    [Fact]
    public void Message_with_a_picture_is_sent_as_image_and_text_parts()
    {
        var body = LlamaClient.BuildRequest([new ChatMessage(ChatRole.User, "look", ImageJpeg: [1, 2, 3])], null);
        var content = JsonDocument.Parse(body).RootElement.GetProperty("messages")[0].GetProperty("content");
        Assert.Equal(JsonValueKind.Array, content.ValueKind);
        Assert.Equal("image_url", content[0].GetProperty("type").GetString());
        Assert.Equal("data:image/jpeg;base64,AQID", content[0].GetProperty("image_url").GetProperty("url").GetString());
        Assert.Equal("look", content[1].GetProperty("text").GetString());
    }

    [Fact]
    public void Message_without_a_picture_stays_plain_text()
    {
        var body = LlamaClient.BuildRequest([new ChatMessage(ChatRole.User, "hi")], null);
        Assert.Equal("hi", JsonDocument.Parse(body).RootElement.GetProperty("messages")[0].GetProperty("content").GetString());
    }

    [Fact]
    public void Projector_is_passed_to_the_server_with_an_image_size_cap()
    {
        var args = new ServerOptions("llama-server.exe", @"C:\m\vl.gguf", 1, 8192, 4, 0, ProjectorPath: @"C:\m\mmproj-vl.gguf").Arguments;
        Assert.Contains("--mmproj \"C:\\m\\mmproj-vl.gguf\"", args);
        Assert.Contains($"--image-max-tokens {ServerOptions.ImageMaxTokens}", args);
        Assert.DoesNotContain("--mmproj", new ServerOptions("llama-server.exe", "m.gguf", 1, 8192, 4, 0).Arguments);
    }
}

public sealed class ProjectorLocatorTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("pixel-vision-").FullName;
    public void Dispose() => Directory.Delete(_dir, true);

    private string Touch(string name)
    {
        var p = Path.Combine(_dir, name);
        File.WriteAllText(p, "x");
        return p;
    }

    [Fact]
    public void Named_projector_pairs_with_its_model_only()
    {
        var vl = Touch("Qwen3VL-4B-Instruct-Q4_K_M.gguf");
        var coder = Touch("qwen2.5-coder-7b-instruct-q4_k_m.gguf");
        var other = Touch("Qwen3VL-8B-Instruct-Q4_K_M.gguf");
        var mmproj = Touch("mmproj-Qwen3VL-4B-Instruct-Q8_0.gguf");

        Assert.Equal(mmproj, ModelLocator.ProjectorFor(vl));
        Assert.Null(ModelLocator.ProjectorFor(coder));
        Assert.Null(ModelLocator.ProjectorFor(other));
    }

    [Fact]
    public void Generic_projector_pairs_with_a_vision_model_but_not_a_text_one()
    {
        var vl = Touch("Qwen3-VL-4B-Instruct-Q4_K_M.gguf");
        var coder = Touch("qwen2.5-coder-7b-instruct-q4_k_m.gguf");
        var mmproj = Touch("mmproj-F16.gguf");

        Assert.Equal(mmproj, ModelLocator.ProjectorFor(vl));
        Assert.Null(ModelLocator.ProjectorFor(coder));
    }

    [Fact]
    public void A_projector_is_never_picked_as_the_model()
    {
        Touch("mmproj-Qwen3VL-4B-Instruct-Q8_0.gguf"); // sorts first
        var vl = Touch("Qwen3VL-4B-Instruct-Q4_K_M.gguf");
        Assert.Equal(vl, Assert.IsType<ModelStatus.Found>(ModelLocator.Find(_dir, null)).Path);
    }

    [Fact]
    public void A_folder_with_only_a_projector_has_no_model()
    {
        Touch("mmproj-F16.gguf");
        Assert.IsType<ModelStatus.Missing>(ModelLocator.Find(_dir, null));
    }
}

public sealed class VisionBrainTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("pixel-vision-brain-").FullName;
    public void Dispose() => Directory.Delete(_dir, true);

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

    private sealed class Capture : HttpMessageHandler
    {
        public string Body = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Body = await r.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("data: {\"choices\":[{\"delta\":{\"content\":\"a cat\"}}]}\n\ndata: [DONE]\n\n", Encoding.UTF8),
            };
        }
    }

    private static readonly ScreenSnapshot Wallpaper =
        new(DateTimeOffset.Now, "Desktop (wallpaper and icons)", "explorer", "", "", [9, 9, 9]);

    private (LlamaBrain Brain, Capture Http, Launcher Launcher, List<long> MemoryAsked) Make(bool withProjector, MemoryVerdict? verdict = null)
    {
        var model = Path.Combine(_dir, "Qwen3VL-4B-Instruct-Q4_K_M.gguf");
        File.WriteAllBytes(model, new byte[100]);
        var projector = Path.Combine(_dir, "mmproj-Qwen3VL-4B-Instruct-Q8_0.gguf");
        File.WriteAllBytes(projector, new byte[40]);

        var http = new Capture();
        var launcher = new Launcher();
        var asked = new List<long>();
        var brain = new LlamaBrain(
            new ServerManager(launcher, new AlwaysReady(), () => DateTimeOffset.Now, TimeSpan.FromMinutes(10), TimeSpan.Zero),
            () => new ModelStatus.Found(model),
            path => new ServerOptions("llama-server.exe", path, 17860, 8192, 4, 0, ProjectorPath: withProjector ? projector : null),
            bytes =>
            {
                asked.Add(bytes);
                return verdict ?? new MemoryVerdict.Ok();
            },
            _ => Task.FromResult(true),
            new LlamaClient(new HttpClient(http) { BaseAddress = new Uri("http://127.0.0.1:17860/") }),
            [],
            () => DateTimeOffset.Now);
        return (brain, http, launcher, asked);
    }

    private static async Task<List<BrainEvent>> Ask(LlamaBrain brain)
    {
        var events = new List<BrainEvent>();
        await foreach (var e in brain.AskAsync([new ChatMessage(ChatRole.User, "who is on my wallpaper?")], Wallpaper, CancellationToken.None))
            events.Add(e);
        return events;
    }

    [Fact]
    public async Task Vision_brain_sends_the_screenshot_and_counts_the_projector_memory()
    {
        var (brain, http, _, memory) = Make(withProjector: true);
        var events = await Ask(brain);
        Assert.Contains("data:image/jpeg;base64,", http.Body);
        Assert.Equal([140L], memory);
        Assert.Contains(events, e => e is StatusEvent { Text: "looking at your screen…" });
    }

    [Fact]
    public async Task Text_brain_sends_only_the_words()
    {
        var (brain, http, _, memory) = Make(withProjector: false);
        await Ask(brain);
        Assert.DoesNotContain("image_url", http.Body);
        var messages = JsonDocument.Parse(http.Body).RootElement.GetProperty("messages");
        Assert.Contains("<screen>", messages[messages.GetArrayLength() - 1].GetProperty("content").GetString());
        Assert.Equal([100L], memory);
    }

    [Fact]
    public async Task Warm_up_loads_the_brain_and_reads_the_screen_without_answering()
    {
        var (brain, http, launcher, _) = Make(withProjector: true);
        await brain.WarmUpAsync([], Wallpaper, CancellationToken.None);

        Assert.Equal(1, launcher.Launches);
        var request = JsonDocument.Parse(http.Body).RootElement;
        Assert.False(request.GetProperty("stream").GetBoolean());
        Assert.Equal(1, request.GetProperty("max_tokens").GetInt32());
        Assert.Contains("data:image/jpeg;base64,", http.Body);
    }

    [Fact]
    public async Task Warm_up_never_loads_when_memory_is_short()
    {
        var (brain, http, launcher, _) = Make(withProjector: true, new MemoryVerdict.Slow("slow"));
        await brain.WarmUpAsync([], Wallpaper, CancellationToken.None);

        Assert.Equal(0, launcher.Launches);
        Assert.Equal("", http.Body);
    }

    [Fact]
    public async Task The_question_after_a_warm_up_reuses_the_loaded_brain()
    {
        var (brain, _, launcher, memory) = Make(withProjector: true);
        await brain.WarmUpAsync([], Wallpaper, CancellationToken.None);
        await Ask(brain);

        Assert.Equal(1, launcher.Launches);
        Assert.Single(memory);
    }
}

public class StickyScreenTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 17, 9, 0, TimeSpan.FromHours(5.5));
    private static ChatMessage A(string t) => new(ChatRole.Assistant, t);

    private static ScreenSnapshot Shot(string words, byte id, string taskbar = "17:09") =>
        new(Now, "Notes - notepad", "notepad", words, taskbar, [0xFF, 0xD8, id]);

    private static ChatMessage Q(string text, ScreenSnapshot? screen, int minute) =>
        new(ChatRole.User, text, Screen: screen, AskedAt: Now.AddMinutes(minute));

    [Fact]
    public void Follow_up_on_an_unchanged_screen_keeps_the_first_question_byte_identical()
    {
        var s1 = Shot("hello world", 1);
        var first = PromptBuilder.Build([Q("what is this?", s1, 0)], s1, Now, vision: true);

        var s2 = Shot("hello world", 2, taskbar: "17:10"); // looked again a minute later: only the clock changed
        var second = PromptBuilder.Build([Q("what is this?", s1, 0), A("a note"), Q("and the title?", s2, 1)], s2, Now.AddMinutes(1), vision: true);

        Assert.Equal(first[1].Content, second[1].Content);
        Assert.Same(first[1].ImageJpeg, second[1].ImageJpeg);
        Assert.Null(second[^1].ImageJpeg);
        Assert.StartsWith("[Screen: unchanged", second[^1].Content);
        Assert.EndsWith("and the title?", second[^1].Content);
    }

    [Fact]
    public void A_changed_screen_moves_the_picture_to_the_newest_question()
    {
        var s1 = Shot("page one", 1);
        var s2 = Shot("page two", 2);
        var msgs = PromptBuilder.Build([Q("q1", s1, 0), A("a1"), Q("q2", s2, 1)], s2, Now, vision: true);

        Assert.Null(msgs[1].ImageJpeg);
        Assert.StartsWith("[Screen: an earlier screen", msgs[1].Content);
        Assert.DoesNotContain("page one", string.Concat(msgs.Select(m => m.Content)));
        Assert.Same(s2.Screenshot, msgs[^1].ImageJpeg);
        Assert.Contains("page two", msgs[^1].Content);
    }

    [Fact]
    public void Eye_off_hides_every_earlier_screen_too()
    {
        var s1 = Shot("private stuff", 1);
        var msgs = PromptBuilder.Build([Q("q1", s1, 0), A("a1"), Q("q2", null, 1)], null, Now, vision: true);

        Assert.All(msgs, m => Assert.Null(m.ImageJpeg));
        Assert.DoesNotContain("private stuff", string.Concat(msgs.Select(m => m.Content)));
        Assert.StartsWith("[Screen: not shared", msgs[^1].Content);
    }

    [Fact]
    public void Warm_up_prompt_is_a_prefix_of_the_real_question()
    {
        var s = Shot("code", 1);
        var warm = PromptBuilder.Build([new ChatMessage(ChatRole.User, "")], s, Now, vision: true);
        var real = PromptBuilder.Build([Q("why does this fail?", s, 0)], s, Now, vision: true);

        Assert.Equal(warm[0].Content, real[0].Content);
        Assert.Same(warm[^1].ImageJpeg, real[^1].ImageJpeg);
        Assert.StartsWith(warm[^1].Content, real[^1].Content);
    }

    [Fact]
    public void Screen_part_comes_before_the_clock_so_a_new_minute_keeps_the_screen_cached()
    {
        var s = Shot("code", 1);
        var content = PromptBuilder.Build([Q("q", s, 0)], s, Now, vision: true)[^1].Content;
        Assert.True(content.IndexOf("</screen>", StringComparison.Ordinal) < content.IndexOf("[Now:", StringComparison.Ordinal));
    }

    [Fact]
    public void Clock_ticks_are_not_a_screen_change_but_new_words_are()
    {
        Assert.True(Shot("x", 1).SameScreen(Shot("x", 2, taskbar: "5:10 PM")));
        Assert.False(Shot("x", 1).SameScreen(Shot("y", 1)));
        Assert.False(Shot("x", 1).SameScreen(Shot("x", 1) with { WindowTitle = "Other" }));
    }
}
