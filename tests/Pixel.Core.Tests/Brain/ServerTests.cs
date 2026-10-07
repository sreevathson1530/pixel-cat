using Pixel.Core.Brain;

namespace Pixel.Core.Tests.Brain;

public sealed class ModelLocatorTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("pixel-models-").FullName;
    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public void Missing_directory_is_missing() =>
        Assert.IsType<ModelStatus.Missing>(ModelLocator.Find(Path.Combine(_dir, "nope"), null));

    [Fact]
    public void Partial_download_is_reported_as_downloading()
    {
        File.WriteAllText(Path.Combine(_dir, "Unconfirmed 1234.crdownload"), "x");
        Assert.IsType<ModelStatus.Downloading>(ModelLocator.Find(_dir, null));
    }

    [Fact]
    public void Finished_gguf_wins_over_leftover_partial_files()
    {
        File.WriteAllText(Path.Combine(_dir, "Unconfirmed 1234.crdownload"), "x");
        var gguf = Path.Combine(_dir, "qwen2.5-coder-14b-instruct-q4_k_m.gguf");
        File.WriteAllText(gguf, "model");
        Assert.Equal(gguf, Assert.IsType<ModelStatus.Found>(ModelLocator.Find(_dir, null)).Path);
    }

    [Fact]
    public void Explicit_path_is_used_when_it_exists()
    {
        var gguf = Path.Combine(_dir, "custom.gguf");
        File.WriteAllText(gguf, "model");
        Assert.Equal(gguf, Assert.IsType<ModelStatus.Found>(ModelLocator.Find(Path.Combine(_dir, "other"), gguf)).Path);
    }

    [Fact]
    public void Split_models_start_from_the_first_part()
    {
        File.WriteAllText(Path.Combine(_dir, "m-00002-of-00002.gguf"), "b");
        var first = Path.Combine(_dir, "m-00001-of-00002.gguf");
        File.WriteAllText(first, "a");
        Assert.Equal(first, Assert.IsType<ModelStatus.Found>(ModelLocator.Find(_dir, null)).Path);
    }
}

public class MemoryGateTests
{
    private const long GB = 1L << 30;

    [Fact]
    public void Plenty_of_memory_is_ok() =>
        Assert.IsType<MemoryVerdict.Ok>(MemoryGate.Check(availablePhys: 14 * GB, availableCommit: 20 * GB, modelBytes: 9 * GB, gpu: false));

    [Fact]
    public void Cpu_backend_with_little_free_ram_is_slow_but_allowed() =>
        Assert.IsType<MemoryVerdict.Slow>(MemoryGate.Check(2 * GB, 6 * GB, 9 * GB, gpu: false));

    [Fact]
    public void Gpu_backend_needs_commit_for_the_whole_model() =>
        Assert.IsType<MemoryVerdict.TooLow>(MemoryGate.Check(12 * GB, 6 * GB, 9 * GB, gpu: true));

    [Fact]
    public void No_commit_left_for_the_cache_is_too_low() =>
        Assert.IsType<MemoryVerdict.TooLow>(MemoryGate.Check(12 * GB, GB / 2, 9 * GB, gpu: false));
}

public class ServerOptionsTests
{
    [Fact]
    public void Arguments_bind_localhost_and_enable_tool_templates()
    {
        var args = new ServerOptions(@"C:\llama\llama-server.exe", @"C:\m\model.gguf", Port: 17860, ContextSize: 8192, Threads: 6, GpuLayers: 0).Arguments;
        Assert.Contains("--host 127.0.0.1", args);
        Assert.Contains("--port 17860", args);
        Assert.Contains("-c 8192", args);
        Assert.Contains("--jinja", args);
        Assert.Contains("\"C:\\m\\model.gguf\"", args);
        Assert.Contains("-ngl 0", args);
    }
}

public class ServerManagerTests
{
    private sealed class FakeProcess : IServerProcess
    {
        public bool HasExited { get; set; }
        public bool Killed { get; private set; }
        public void Kill() { Killed = true; HasExited = true; }
    }

    private sealed class FakeLauncher : IServerLauncher
    {
        public List<FakeProcess> Launched { get; } = [];
        public Queue<bool> CrashOnStart { get; } = new();
        public IServerProcess Launch(ServerOptions options)
        {
            var p = new FakeProcess { HasExited = CrashOnStart.Count > 0 && CrashOnStart.Dequeue() };
            Launched.Add(p);
            return p;
        }
    }

    private sealed class FakeProbe(FakeLauncher l) : IHealthProbe
    {
        public Task<bool> IsReadyAsync(CancellationToken ct) => Task.FromResult(l.Launched.Count > 0 && !l.Launched[^1].HasExited);
    }

    private static readonly ServerOptions Opts = new("llama-server.exe", "m.gguf", 17860, 8192, 4, 0);

    private static (ServerManager M, FakeLauncher L, Func<DateTimeOffset> Clock, Action<TimeSpan> Advance) Make()
    {
        var l = new FakeLauncher();
        var now = new DateTimeOffset(2026, 10, 7, 0, 0, 0, TimeSpan.Zero);
        var m = new ServerManager(l, new FakeProbe(l), () => now, TimeSpan.FromMinutes(10), pollDelay: TimeSpan.Zero, startTimeout: TimeSpan.FromSeconds(1));
        return (m, l, () => now, d => now += d);
    }

    [Fact]
    public async Task Starts_and_becomes_ready()
    {
        var (m, l, _, _) = Make();
        Assert.True(await m.EnsureReadyAsync(Opts, CancellationToken.None));
        Assert.Equal(ServerState.Ready, m.State);
        Assert.Single(l.Launched);
    }

    [Fact]
    public async Task Ready_server_is_reused()
    {
        var (m, l, _, _) = Make();
        await m.EnsureReadyAsync(Opts, CancellationToken.None);
        await m.EnsureReadyAsync(Opts, CancellationToken.None);
        Assert.Single(l.Launched);
    }

    [Fact]
    public async Task Crash_on_start_is_retried_once()
    {
        var (m, l, _, _) = Make();
        l.CrashOnStart.Enqueue(true);
        Assert.True(await m.EnsureReadyAsync(Opts, CancellationToken.None));
        Assert.Equal(2, l.Launched.Count);
    }

    [Fact]
    public async Task Crashing_twice_fails_without_a_third_attempt()
    {
        var (m, l, _, _) = Make();
        l.CrashOnStart.Enqueue(true);
        l.CrashOnStart.Enqueue(true);
        Assert.False(await m.EnsureReadyAsync(Opts, CancellationToken.None));
        Assert.Equal(ServerState.Failed, m.State);
        Assert.Equal(2, l.Launched.Count);
    }

    [Fact]
    public async Task Idle_server_is_stopped_after_timeout()
    {
        var (m, l, _, advance) = Make();
        await m.EnsureReadyAsync(Opts, CancellationToken.None);
        advance(TimeSpan.FromMinutes(9));
        Assert.False(m.StopIfIdle());
        m.Touch();
        advance(TimeSpan.FromMinutes(9));
        Assert.False(m.StopIfIdle());
        advance(TimeSpan.FromMinutes(2));
        Assert.True(m.StopIfIdle());
        Assert.True(l.Launched[0].Killed);
        Assert.Equal(ServerState.Stopped, m.State);
    }

    [Fact]
    public async Task Server_that_died_while_ready_is_relaunched()
    {
        var (m, l, _, _) = Make();
        await m.EnsureReadyAsync(Opts, CancellationToken.None);
        l.Launched[0].HasExited = true;
        Assert.True(await m.EnsureReadyAsync(Opts, CancellationToken.None));
        Assert.Equal(2, l.Launched.Count);
    }
}

public class RepackRuleTests
{
    private const long GB = 1L << 30;

    [Fact]
    public void Repacking_is_skipped_when_commit_is_short() =>
        Assert.False(MemoryGate.CanRepack(availableCommit: 2 * GB, modelBytes: 9 * GB));

    [Fact]
    public void Repacking_is_used_when_memory_allows() =>
        Assert.True(MemoryGate.CanRepack(availableCommit: 12 * GB, modelBytes: 9 * GB));
}
