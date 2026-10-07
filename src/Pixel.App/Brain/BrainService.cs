using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Windows.Threading;
using Pixel.App.Shell;
using Pixel.Core.Brain;
using Pixel.Core.Storage;

namespace Pixel.App.Brain;

/// <summary>Builds the real brain from settings and unloads it when idle or when Pixel vanishes.</summary>
internal sealed class BrainService : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = Timeout.InfiniteTimeSpan };
    private readonly HttpClient _probeHttp = new() { Timeout = TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer _idleTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly LlamaLauncher _launcher = new();
    private readonly Func<Settings> _settings;

    public BrainService(Func<Settings> settings, Func<string, Task<bool>> askUser, IReadOnlyList<ToolSpec> tools)
    {
        _settings = settings;
        Server = new ServerManager(_launcher, new HealthProbe(_probeHttp, () => Server!.Current, () => _launcher.LastProcessId),
            () => DateTimeOffset.Now, TimeSpan.FromMinutes(Math.Max(1, settings().IdleUnloadMinutes)));
        Brain = new LlamaBrain(
            Server,
            () => ModelLocator.Find(_settings().ModelDirectory, _settings().ModelPath),
            OptionsFor,
            CheckMemory,
            askUser,
            new LlamaClient(_http, LiveEndpoint),
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

    /// <summary>Address and key of the server we launched — only once it is verified ready.</summary>
    private (Uri, string?)? LiveEndpoint() =>
        Server.IsAlive && Server.Current is { } o ? (new Uri($"http://127.0.0.1:{o.Port}/"), o.ApiKey) : null;

    /// <summary>Fresh random port and API key for every launch, so no other local program can sit in between.</summary>
    private ServerOptions OptionsFor(string modelPath)
    {
        var s = _settings();
        var exe = LlamaPaths.FindServerExe(s.LlamaBackend)
                  ?? throw new FileNotFoundException($"llama-server.exe for backend '{s.LlamaBackend}' not found next to Pixel");
        var threads = s.LlamaThreads > 0 ? s.LlamaThreads : Math.Clamp(Environment.ProcessorCount / 2 + 1, 4, 12);
        var gpuLayers = s.LlamaBackend == "cpu" ? 0 : 99;
        var extra = s.LlamaExtraArgs;
        if (s.LlamaBackend == "cpu" && !MemoryGate.CanRepack(MemoryInfo.Read().AvailableCommit, new FileInfo(modelPath).Length))
            extra += " --no-repack"; // keep weights memory-mapped when Windows can't spare the repack copy
        return new ServerOptions(exe, modelPath, FreeLoopbackPort(), s.ContextSize, threads, gpuLayers, extra,
            ApiKey: RandomNumberGenerator.GetHexString(48, lowercase: true));
    }

    private static int FreeLoopbackPort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
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

/// <summary>
/// Locates bundled llama.cpp builds. Only the folder next to Pixel is trusted (runtime\llama\&lt;backend&gt;),
/// plus an explicit PIXEL_LLAMA_DIR for development — never a search up through parent folders,
/// where another account could plant an executable.
/// </summary>
internal static class LlamaPaths
{
    public static string? FindServerExe(string backend)
    {
        if (backend.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || backend.Contains("..")) return null;
        var roots = new List<string> { Path.Combine(AppContext.BaseDirectory, "runtime", "llama") };
        if (Environment.GetEnvironmentVariable("PIXEL_LLAMA_DIR") is { Length: > 0 } dev) roots.Add(dev);
        return roots.Select(r => Path.Combine(r, backend, "llama-server.exe")).FirstOrDefault(File.Exists);
    }
}
