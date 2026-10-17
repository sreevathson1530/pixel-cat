using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace Pixel.Core.Brain;

/// <summary>One file of a brain, with the size and SHA-256 it must have.</summary>
public sealed record ModelFile(string Name, long Size, string Sha256);

/// <summary>A brain Pixel can fetch on first run: files from one pinned Hugging Face revision.</summary>
/// <param name="Files">Downloaded in order — the vision projector first, so a finished model always has its eyes.</param>
public sealed record ModelPack(string Title, string Repo, string Revision, IReadOnlyList<ModelFile> Files)
{
    public long TotalBytes => Files.Sum(f => f.Size);
    public ModelFile Model => Files.Single(f => !ModelLocator.IsProjector(f.Name));
    public Uri UrlFor(ModelFile f) => new($"https://huggingface.co/{Repo}/resolve/{Revision}/{f.Name}");

    /// <summary>
    /// Qwen3-VL-2B (Apache-2.0): sees the screen, and on a laptop CPU answers about twice as fast as the 4B
    /// (≈80 tokens/s reading, ≈27 tokens/s writing) in about half the memory.
    /// </summary>
    public static readonly ModelPack VisionDefault = new(
        "Qwen3-VL-2B",
        "Qwen/Qwen3-VL-2B-Instruct-GGUF",
        "52d6c8ffea26cc873ac5ad116f8631268d7eb503",
        [
            new("mmproj-Qwen3VL-2B-Instruct-Q8_0.gguf", 445_053_216, "f9a68fabba69c3b81e153367b2c7521030b0fa8bb0de400c9599c8e6725f9c82"),
            new("Qwen3VL-2B-Instruct-Q4_K_M.gguf", 1_107_409_952, "089d75c52f4b7ffc56ba998ffc50aae89fcafc755f9e7208aacca281dca6c2ae"),
        ]);
}

/// <param name="Reconnecting">The connection dropped and the download is about to resume from where it stopped.</param>
public readonly record struct DownloadProgress(long Done, long Total, bool Reconnecting = false)
{
    public double Fraction => Total > 0 ? Math.Clamp((double)Done / Total, 0, 1) : 0;
}

/// <summary>A download problem worth telling the user about, in words they can act on.</summary>
public sealed class ModelDownloadException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Downloads a <see cref="ModelPack"/> into a folder. Each file goes to "name.gguf.pixel-partial" first, resumes
/// from there after an interruption, and only becomes "name.gguf" once its size and SHA-256 match — so a damaged or
/// tampered file is never loaded. A dropped or stalled connection reconnects and resumes by itself.
/// </summary>
/// <param name="retryDelay">Wait before reconnect attempt n (1-based); defaults to 2, 4, 8, 16, 30 s.</param>
public sealed class ModelDownloader(HttpClient http, TimeSpan? stallTimeout = null, int bufferSize = 1 << 20,
    Func<int, TimeSpan>? retryDelay = null)
{
    /// <summary>Suffix of a file still downloading. Not a pattern <see cref="ModelLocator"/> treats as a browser download.</summary>
    public const string PartialSuffix = ".pixel-partial";

    /// <summary>Tries in a row without progress before giving up; any progress starts the count again.</summary>
    public const int MaxAttempts = 6;

    private readonly TimeSpan _stall = stallTimeout ?? TimeSpan.FromSeconds(60);
    private readonly Func<int, TimeSpan> _retryDelay = retryDelay ?? (n => TimeSpan.FromSeconds(Math.Min(30, 2 << (n - 1))));

    /// <summary>The connection failed in a way that's worth simply trying again (resuming from the partial file).</summary>
    private sealed class ConnectionDropped(string reason, Exception? inner = null) : Exception(reason, inner);

    /// <summary>Bytes of the pack already on disk, finished or partial — to offer a resume.</summary>
    public static long BytesPresent(ModelPack pack, string dir) => pack.Files.Sum(f =>
    {
        var done = new FileInfo(Path.Combine(dir, f.Name));
        if (done.Exists && done.Length == f.Size) return f.Size;
        var part = new FileInfo(Path.Combine(dir, f.Name + PartialSuffix));
        return part.Exists ? Math.Min(part.Length, f.Size) : 0;
    });

    /// <returns>Path of the downloaded model (its projector sits next to it).</returns>
    public async Task<string> DownloadAsync(ModelPack pack, string dir, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(dir);
        var reporter = new Reporter(progress, pack.TotalBytes);
        long before = 0;
        foreach (var f in pack.Files)
        {
            var target = Path.Combine(dir, f.Name);
            if (!(File.Exists(target) && new FileInfo(target).Length == f.Size))
                await FetchWithRetriesAsync(pack.UrlFor(f), target, f, before, reporter, ct);
            before += f.Size;
            reporter.Report(before, force: true);
        }
        return Path.Combine(dir, pack.Model.Name);
    }

    private static long PartialLength(string part) => File.Exists(part) ? new FileInfo(part).Length : 0;

    /// <summary>
    /// Fetches one file, reconnecting after drops. Gives up only after <see cref="MaxAttempts"/> tries in a row that
    /// made no progress, so a long download on a shaky network keeps going.
    /// </summary>
    private async Task FetchWithRetriesAsync(Uri url, string target, ModelFile f, long before, Reporter reporter, CancellationToken ct)
    {
        var part = target + PartialSuffix;
        var failures = 0;
        while (true)
        {
            var had = PartialLength(part);
            try
            {
                await FetchAsync(url, target, f, before, reporter, ct);
                return;
            }
            catch (ConnectionDropped e)
            {
                failures = PartialLength(part) > had ? 1 : failures + 1;
                if (failures >= MaxAttempts)
                    throw new ModelDownloadException(
                        $"The connection to huggingface.co kept dropping ({e.Message}). Ask me again and I'll pick up where I left off — or try another network.", e.InnerException);
                reporter.Reconnecting(before + PartialLength(part));
                await Task.Delay(_retryDelay(failures), ct);
            }
        }
    }

    private async Task FetchAsync(Uri url, string target, ModelFile f, long before, Reporter reporter, CancellationToken ct)
    {
        var part = target + PartialSuffix;
        var have = PartialLength(part);
        if (have > f.Size) have = 0;

        if (have < f.Size)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (have > 0) request.Headers.Range = new RangeHeaderValue(have, null);
            using var stall = CancellationTokenSource.CreateLinkedTokenSource(ct);
            stall.CancelAfter(_stall);

            HttpResponseMessage response;
            try
            {
                response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, stall.Token);
            }
            catch (HttpRequestException e)
            {
                throw new ConnectionDropped("couldn't reach it", e);
            }
            catch (OperationCanceledException e) when (!ct.IsCancellationRequested)
            {
                throw new ConnectionDropped("no answer", e);
            }

            using (response)
            {
                var status = (int)response.StatusCode;
                if (status is 408 or 429 || status >= 500) throw new ConnectionDropped($"the server answered {status}");
                if (!response.IsSuccessStatusCode)
                    throw new ModelDownloadException($"huggingface.co answered {status} for {f.Name}. Try again later.");
                if (have > 0 && response.StatusCode != HttpStatusCode.PartialContent) have = 0; // no resume offered: start over

                FileStream file;
                try
                {
                    file = new FileStream(part, have > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, bufferSize);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    throw new ModelDownloadException($"I couldn't save {f.Name} in {Path.GetDirectoryName(part)} ({e.Message}).", e);
                }

                await using (file)
                {
                    var buffer = new byte[bufferSize];
                    try
                    {
                        await using var source = await response.Content.ReadAsStreamAsync(stall.Token);
                        while (true)
                        {
                            stall.CancelAfter(_stall); // a live connection keeps pushing the deadline out
                            var n = await source.ReadAsync(buffer, stall.Token);
                            if (n == 0) break;
                            if (have + n > f.Size) throw new ModelDownloadException($"{f.Name} came out bigger than expected, so I stopped. Try again later.");
                            try
                            {
                                await file.WriteAsync(buffer.AsMemory(0, n), ct);
                            }
                            catch (IOException e)
                            {
                                throw new ModelDownloadException($"Saving {f.Name} failed ({e.Message}). Check there's free disk space, then ask me again.", e);
                            }
                            have += n;
                            reporter.Report(before + have);
                        }
                    }
                    catch (OperationCanceledException e) when (!ct.IsCancellationRequested)
                    {
                        throw new ConnectionDropped("the connection went quiet", e);
                    }
                    catch (Exception e) when (e is IOException or HttpRequestException)
                    {
                        throw new ConnectionDropped("the connection was cut", e); // reading from the network; disk errors are handled above
                    }
                }
            }
        }

        if (have != f.Size) throw new ConnectionDropped("the download ended early");
        if (!await HashMatchesAsync(part, f.Sha256, ct))
        {
            File.Delete(part);
            throw new ModelDownloadException($"{f.Name} arrived damaged, so I deleted it. Ask me again to download it fresh.");
        }
        File.Move(part, target, overwrite: true);
    }

    private static async Task<bool> HashMatchesAsync(string path, string expected, CancellationToken ct)
    {
        await using var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, useAsync: true);
        var hash = await SHA256.HashDataAsync(s, ct);
        return Convert.ToHexStringLower(hash) == expected.ToLowerInvariant();
    }

    /// <summary>Reports at most every half percent, so a UI isn't flooded with thousands of updates.</summary>
    private sealed class Reporter(IProgress<DownloadProgress>? progress, long total)
    {
        private long _last = -1;

        public void Reconnecting(long done) => progress?.Report(new DownloadProgress(done, total, Reconnecting: true));

        public void Report(long done, bool force = false)
        {
            if (progress is null || done == _last || (!force && done - _last < total / 200 && done < total)) return;
            _last = done;
            progress.Report(new DownloadProgress(done, total));
        }
    }
}
