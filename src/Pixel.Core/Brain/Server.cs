using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Pixel.Core.Brain;

public abstract record ModelStatus
{
    public sealed record Found(string Path) : ModelStatus;
    public sealed record Downloading(string File) : ModelStatus;
    public sealed record Missing(string Directory) : ModelStatus;
}

/// <summary>Finds the GGUF model file, recognising browser downloads that haven't finished yet.</summary>
public static partial class ModelLocator
{
    private static readonly string[] PartialPatterns = ["*.crdownload", "*.part", "*.partial", "*.download"];

    [GeneratedRegex(@"-0*1-of-\d+\.gguf$", RegexOptions.IgnoreCase)]
    private static partial Regex FirstSplit();

    [GeneratedRegex(@"-\d+-of-\d+\.gguf$", RegexOptions.IgnoreCase)]
    private static partial Regex AnySplit();

    /// <summary>"mmproj-Qwen3VL-4B-Instruct-Q8_0" → base "Qwen3VL-4B-Instruct"; "mmproj-F16" → empty base.</summary>
    [GeneratedRegex(@"^mmproj[-_.]?(?<base>.*?)(?:[-_.]?(?:f16|f32|bf16|q\d\w*))?$", RegexOptions.IgnoreCase)]
    private static partial Regex ProjectorName();

    [GeneratedRegex(@"vl|vision|llava|gemma-?3|pixtral|minicpm-?v", RegexOptions.IgnoreCase)]
    private static partial Regex MultimodalName();

    /// <summary>A vision projector ("mmproj") file: it goes with a model, it isn't one.</summary>
    public static bool IsProjector(string path) =>
        Path.GetFileName(path).StartsWith("mmproj", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The vision projector that lets <paramref name="modelPath"/> see pictures, if one sits next to it.
    /// "mmproj-Qwen3VL-4B-Instruct-Q8_0.gguf" pairs with "Qwen3VL-4B-Instruct-Q4_K_M.gguf"; a bare "mmproj-F16.gguf"
    /// (the usual community layout, one model per folder) pairs with a vision model in the same folder.
    /// </summary>
    public static string? ProjectorFor(string modelPath)
    {
        if (IsProjector(modelPath)) return null;
        var dir = Path.GetDirectoryName(Path.GetFullPath(modelPath));
        if (dir is null || !Directory.Exists(dir)) return null;
        var model = Path.GetFileNameWithoutExtension(modelPath);
        string? generic = null;
        foreach (var p in Directory.EnumerateFiles(dir, "mmproj*.gguf").Order(StringComparer.OrdinalIgnoreCase))
        {
            var stem = ProjectorName().Match(Path.GetFileNameWithoutExtension(p)).Groups["base"].Value;
            if (stem.Length == 0) generic ??= p;
            else if (model.StartsWith(stem, StringComparison.OrdinalIgnoreCase)) return p;
        }
        return generic is not null && MultimodalName().IsMatch(model) ? generic : null;
    }

    public static ModelStatus Find(string directory, string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath) && File.Exists(explicitPath)) return new ModelStatus.Found(explicitPath);
        if (!Directory.Exists(directory)) return new ModelStatus.Missing(directory);

        var model = Directory.EnumerateFiles(directory, "*.gguf")
            .Where(f => !IsProjector(f) && (!AnySplit().IsMatch(f) || FirstSplit().IsMatch(f)))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (model is not null) return new ModelStatus.Found(model);

        var partial = PartialPatterns.SelectMany(p => Directory.EnumerateFiles(directory, p)).FirstOrDefault();
        return partial is not null ? new ModelStatus.Downloading(Path.GetFileName(partial)) : new ModelStatus.Missing(directory);
    }
}

public abstract record MemoryVerdict
{
    public sealed record Ok : MemoryVerdict;
    public sealed record Slow(string Message) : MemoryVerdict;
    public sealed record TooLow(string Message) : MemoryVerdict;
}

/// <summary>
/// Decides whether loading the model is sensible right now. CPU inference memory-maps the weights (no commit
/// charge, but slow if they don't fit in RAM); GPU backends copy them into committed memory.
/// </summary>
public static class MemoryGate
{
    private const long CacheBytes = 1200L << 20; // KV cache (q8_0, 8k) + compute buffers ≈ 1.2 GB

    /// <summary>
    /// CPU "repacking" copies most weights into a faster layout in committed memory (~0.75× the file).
    /// Only worth it when Windows can spare that in both commit and physical RAM (a paged-out repack is far slower
    /// than reading the memory-mapped file); otherwise the weights stay memory-mapped.
    /// </summary>
    public static bool CanRepack(long availablePhys, long availableCommit, long modelBytes) =>
        availableCommit > modelBytes * 3 / 4 + CacheBytes && availablePhys > modelBytes * 3 / 4 + CacheBytes;

    public static MemoryVerdict Check(long availablePhys, long availableCommit, long modelBytes, bool gpu)
    {
        static string Gb(long b) => $"{b / (double)(1L << 30):0.#} GB";

        if (gpu && availableCommit < modelBytes + CacheBytes)
            return new MemoryVerdict.TooLow($"Loading my brain on the GPU needs about {Gb(modelBytes + CacheBytes)} of free memory, but only {Gb(availableCommit)} is available. Close some apps and try again.");
        if (availableCommit < CacheBytes)
            return new MemoryVerdict.TooLow($"Windows is almost out of memory ({Gb(availableCommit)} left). Close some apps and try again.");
        if (availablePhys < modelBytes * 8 / 10)
            return new MemoryVerdict.Slow($"Only {Gb(availablePhys)} of RAM is free and my brain is {Gb(modelBytes)}, so answers will be very slow and other apps may lag. Close some apps for best results.");
        return new MemoryVerdict.Ok();
    }
}

/// <param name="ApiKey">Per-launch secret, handed to the server through LLAMA_API_KEY — never on the command line.</param>
/// <param name="ProjectorPath">Vision projector (mmproj) that lets the model see the screenshot; null for a text-only brain.</param>
public sealed record ServerOptions(
    string ExePath,
    string ModelPath,
    int Port,
    int ContextSize,
    int Threads,
    int GpuLayers,
    string ExtraArgs = "",
    string? ApiKey = null,
    string? ProjectorPath = null)
{
    /// <summary>Most context a screenshot may take. Pixel already sends it downscaled to well under this.</summary>
    public const int ImageMaxTokens = 512;

    /// <summary>RAM llama-server may use to keep recent prompts (with and without tools) ready to reuse, MiB.</summary>
    public const int PromptCacheMiB = 512;

    public string Arguments =>
        ($"-m \"{ModelPath}\" --host 127.0.0.1 --port {Port} -c {ContextSize} -np 1 -t {Threads} -ngl {GpuLayers} --jinja --no-webui --cache-ram {PromptCacheMiB} "
         + (ProjectorPath is null ? "" : $"--mmproj \"{ProjectorPath}\" --image-max-tokens {ImageMaxTokens} ")
         + ExtraArgs).Trim();
}

public enum ServerState { Stopped, Starting, Ready, Failed }

public interface IServerProcess
{
    bool HasExited { get; }
    void Kill();
}

public interface IServerLauncher
{
    IServerProcess Launch(ServerOptions options);
}

public interface IHealthProbe
{
    Task<bool> IsReadyAsync(CancellationToken ct);
}

/// <summary>
/// Lifecycle of the llama-server process: start on demand, retry once if it dies while starting,
/// stop after an idle period so the model's RAM is given back.
/// </summary>
public sealed class ServerManager(
    IServerLauncher launcher,
    IHealthProbe probe,
    Func<DateTimeOffset> clock,
    TimeSpan idleTimeout,
    TimeSpan? pollDelay = null,
    TimeSpan? startTimeout = null)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeSpan _poll = pollDelay ?? TimeSpan.FromMilliseconds(500);
    private readonly TimeSpan _startTimeout = startTimeout ?? TimeSpan.FromMinutes(4);
    private IServerProcess? _process;
    private DateTimeOffset _lastUse;
    private int _inFlight;

    public ServerState State { get; private set; } = ServerState.Stopped;

    /// <summary>Options of the running (or starting) server: its port and API key.</summary>
    public ServerOptions? Current { get; private set; }

    public bool IsAlive => State == ServerState.Ready && _process is { HasExited: false };

    public async Task<bool> EnsureReadyAsync(ServerOptions options, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            Touch();
            if (IsAlive) return true;
            KillProcess();

            for (var attempt = 0; attempt < 2; attempt++)
            {
                State = ServerState.Starting;
                Current = options;
                _process = launcher.Launch(options);
                if (await WaitUntilReadyAsync(ct))
                {
                    State = ServerState.Ready;
                    Touch();
                    return true;
                }
                KillProcess();
            }
            State = ServerState.Failed;
            return false;
        }
        catch (OperationCanceledException)
        {
            // Called off mid-start (panel closed, Stop): don't leave a half-loaded brain that idle unload can't see.
            KillProcess();
            State = ServerState.Stopped;
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Touch() => _lastUse = clock();

    /// <summary>
    /// Marks a question as in progress until disposed. The server is never unloaded mid-question — on a
    /// memory-starved PC reading a long prompt can take many minutes before the first word arrives.
    /// </summary>
    public IDisposable BeginRequest()
    {
        Interlocked.Increment(ref _inFlight);
        Touch();
        return new RequestScope(this);
    }

    private sealed class RequestScope(ServerManager owner) : IDisposable
    {
        private int _done;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _done, 1) == 1) return;
            Interlocked.Decrement(ref owner._inFlight);
            owner.Touch();
        }
    }

    /// <summary>Stops the server if it has been idle too long. Returns true if it was stopped.</summary>
    public bool StopIfIdle()
    {
        if (State != ServerState.Ready || Volatile.Read(ref _inFlight) > 0 || clock() - _lastUse < idleTimeout) return false;
        Stop();
        return true;
    }

    public void Stop()
    {
        KillProcess();
        State = ServerState.Stopped;
    }

    private async Task<bool> WaitUntilReadyAsync(CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < _startTimeout)
        {
            if (_process is null || _process.HasExited) return false;
            if (await probe.IsReadyAsync(ct)) return true;
            await Task.Delay(_poll, ct);
        }
        return false;
    }

    private void KillProcess()
    {
        if (_process is { HasExited: false } p) p.Kill();
        _process = null;
        Current = null;
    }
}
