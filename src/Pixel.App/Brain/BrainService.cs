using System.IO;
using System.Net.Http;
using System.Windows.Threading;
using Pixel.App.Shell;
using Pixel.Core.Brain;
using Pixel.Core.Storage;

namespace Pixel.App.Brain;

/// <summary>Builds the real brain from settings and unloads it when idle or when Pixel vanishes.</summary>
internal sealed class BrainService : IDisposable
{
    public const int Port = 17860;
    private readonly HttpClient _http = new() { BaseAddress = new Uri($"http://127.0.0.1:{Port}/"), Timeout = Timeout.InfiniteTimeSpan };
    private readonly HttpClient _probeHttp = new() { BaseAddress = new Uri($"http://127.0.0.1:{Port}/"), Timeout = TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer _idleTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly Func<Settings> _settings;

    public BrainService(Func<Settings> settings, Func<string, Task<bool>> askUser, IReadOnlyList<ToolSpec> tools)
    {
        _settings = settings;
        Server = new ServerManager(new LlamaLauncher(), new HealthProbe(_probeHttp), () => DateTimeOffset.Now,
            TimeSpan.FromMinutes(Math.Max(1, settings().IdleUnloadMinutes)));
        Brain = new LlamaBrain(
            Server,
            () => ModelLocator.Find(_settings().ModelDirectory, _settings().ModelPath),
            OptionsFor,
            CheckMemory,
            askUser,
            new LlamaClient(_http),
            tools,
            () => DateTimeOffset.Now);
        _idleTimer.Tick += (_, _) =>
        {
            if (Server.StopIfIdle()) Log.Info("brain unloaded after idle timeout");
        };
        _idleTimer.Start();
    }

    public ServerManager Server { get; }
    public IBrain Brain { get; }

    public void Unload()
    {
        if (Server.State == ServerState.Stopped) return;
        Server.Stop();
        Log.Info("brain unloaded");
    }

    private ServerOptions OptionsFor(string modelPath)
    {
        var s = _settings();
        var exe = LlamaPaths.FindServerExe(s.LlamaBackend)
                  ?? throw new FileNotFoundException($"llama-server.exe for backend '{s.LlamaBackend}' not found");
        var threads = s.LlamaThreads > 0 ? s.LlamaThreads : Math.Clamp(Environment.ProcessorCount / 2 + 1, 4, 12);
        var gpuLayers = s.LlamaBackend == "cpu" ? 0 : 99;
        var extra = s.LlamaExtraArgs;
        if (s.LlamaBackend == "cpu" && !MemoryGate.CanRepack(MemoryInfo.Read().AvailableCommit, new FileInfo(modelPath).Length))
            extra += " --no-repack"; // keep weights memory-mapped when Windows can't spare the repack copy
        return new ServerOptions(exe, modelPath, Port, s.ContextSize, threads, gpuLayers, extra);
    }

    private MemoryVerdict CheckMemory(long modelBytes)
    {
        var (phys, commit) = MemoryInfo.Read();
        var verdict = MemoryGate.Check(phys, commit, modelBytes, gpu: _settings().LlamaBackend != "cpu");
        Log.Info($"memory check: phys {phys >> 20} MB, commit {commit >> 20} MB, model {modelBytes >> 20} MB → {verdict.GetType().Name}");
        return verdict;
    }

    public void Dispose()
    {
        _idleTimer.Stop();
        Server.Stop();
        _http.Dispose();
        _probeHttp.Dispose();
    }
}

/// <summary>Locates bundled llama.cpp builds (runtime\llama\&lt;backend&gt;\llama-server.exe) next to the app or above it in dev.</summary>
internal static class LlamaPaths
{
    public static string? FindServerExe(string backend)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 7 && dir is not null; i++, dir = dir.Parent)
        {
            var exe = Path.Combine(dir.FullName, "runtime", "llama", backend, "llama-server.exe");
            if (File.Exists(exe)) return exe;
        }
        return null;
    }
}
