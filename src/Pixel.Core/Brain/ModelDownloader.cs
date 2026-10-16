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

    /// <summary>Qwen3-VL-4B (Apache-2.0): small enough for most PCs, and it can see the screen.</summary>
    public static readonly ModelPack VisionDefault = new(
        "Qwen3-VL-4B",
        "Qwen/Qwen3-VL-4B-Instruct-GGUF",
        "1cd86afb9a95c410a6038ab3b40d8b578c892266",
        [
            new("mmproj-Qwen3VL-4B-Instruct-Q8_0.gguf", 453_974_304, "30ba2c7dd3127a4561b6cba9d13d0f711c91bdb38742e2f56d73c8cb596bd06d"),
            new("Qwen3VL-4B-Instruct-Q4_K_M.gguf", 2_497_281_664, "66358cb18bb6b3b1b6675aa412c7a88ef01d228f481184d13668e5201c730a0a"),
        ]);
}

public readonly record struct DownloadProgress(long Done, long Total)
{
    public double Fraction => Total > 0 ? Math.Clamp((double)Done / Total, 0, 1) : 0;
}

/// <summary>A download problem worth telling the user about, in words they can act on.</summary>
public sealed class ModelDownloadException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Downloads a <see cref="ModelPack"/> into a folder. Each file goes to "name.gguf.pixel-partial" first, resumes
/// from there after an interruption, and only becomes "name.gguf" once its size and SHA-256 match — so a damaged or
/// tampered file is never loaded.
/// </summary>
public sealed class ModelDownloader(HttpClient http, TimeSpan? stallTimeout = null, int bufferSize = 1 << 20)
{
    /// <summary>Suffix of a file still downloading. Not a pattern <see cref="ModelLocator"/> treats as a browser download.</summary>
    public const string PartialSuffix = ".pixel-partial";

    private readonly TimeSpan _stall = stallTimeout ?? TimeSpan.FromSeconds(60);

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
                await FetchAsync(pack.UrlFor(f), target, f, before, reporter, ct);
            before += f.Size;
            reporter.Report(before, force: true);
        }
        return Path.Combine(dir, pack.Model.Name);
    }

    private async Task FetchAsync(Uri url, string target, ModelFile f, long before, Reporter reporter, CancellationToken ct)
    {
        var part = target + PartialSuffix;
        var have = File.Exists(part) ? new FileInfo(part).Length : 0;
        if (have > f.Size) have = 0;

        if (have < f.Size)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (have > 0) request.Headers.Range = new RangeHeaderValue(have, null);
            using var stall = CancellationTokenSource.CreateLinkedTokenSource(ct);
            stall.CancelAfter(_stall);
            try
            {
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, stall.Token);
                if (!response.IsSuccessStatusCode)
                    throw new ModelDownloadException($"huggingface.co answered {(int)response.StatusCode} for {f.Name}. Try again later.");
                if (have > 0 && response.StatusCode != HttpStatusCode.PartialContent) have = 0; // no resume offered: start over

                await using var source = await response.Content.ReadAsStreamAsync(stall.Token);
                await using var file = new FileStream(part, have > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, bufferSize);
                var buffer = new byte[bufferSize];
                while (true)
                {
                    stall.CancelAfter(_stall); // a live connection keeps pushing the deadline out
                    var n = await source.ReadAsync(buffer, stall.Token);
                    if (n == 0) break;
                    if (have + n > f.Size) throw new ModelDownloadException($"{f.Name} came out bigger than expected, so I stopped. Try again later.");
                    await file.WriteAsync(buffer.AsMemory(0, n), stall.Token);
                    have += n;
                    reporter.Report(before + have);
                }
            }
            catch (OperationCanceledException e) when (!ct.IsCancellationRequested)
            {
                throw new ModelDownloadException("The download stalled — the connection went quiet. Ask me again and I'll pick up where I left off.", e);
            }
            catch (HttpRequestException e)
            {
                throw new ModelDownloadException("I couldn't reach huggingface.co. Check your internet and ask me again — I'll pick up where I left off.", e);
            }
            catch (IOException e) when (e is not FileNotFoundException)
            {
                throw new ModelDownloadException($"Saving {f.Name} failed ({e.Message}). Check there's free space, then ask me again.", e);
            }
        }

        if (have != f.Size)
            throw new ModelDownloadException("The download ended early. Ask me again and I'll pick up where I left off.");
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

        public void Report(long done, bool force = false)
        {
            if (progress is null || done == _last || (!force && done - _last < total / 200 && done < total)) return;
            _last = done;
            progress.Report(new DownloadProgress(done, total));
        }
    }
}
