using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Pixel.Core.Brain;

namespace Pixel.Core.Tests.Brain;

public sealed class ModelDownloaderTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("pixel-download-").FullName;
    public void Dispose() => Directory.Delete(_dir, true);

    private static readonly byte[] ProjectorBytes = Enumerable.Range(0, 3000).Select(i => (byte)(i * 7)).ToArray();
    private static readonly byte[] ModelBytes = Enumerable.Range(0, 5000).Select(i => (byte)(i * 13)).ToArray();

    private static string Sha(byte[] b) => Convert.ToHexStringLower(SHA256.HashData(b));

    private static ModelPack Pack(byte[]? modelBytes = null) => new("Test brain", "owner/repo", "abc123",
    [
        new("mmproj-Test-Q8_0.gguf", ProjectorBytes.Length, Sha(ProjectorBytes)),
        new("Test-Q4_K_M.gguf", ModelBytes.Length, Sha(modelBytes ?? ModelBytes)),
    ]);

    /// <summary>Serves the two files, honouring Range requests; records what was asked.</summary>
    private sealed class FakeHub(bool honourRange = true, byte[]? model = null) : HttpMessageHandler
    {
        public List<(string Path, long? From)> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var path = r.RequestUri!.AbsolutePath;
            var from = r.Headers.Range?.Ranges.First().From;
            Requests.Add((path, from));
            var body = path.EndsWith("mmproj-Test-Q8_0.gguf") ? ProjectorBytes : model ?? ModelBytes;
            if (from is { } start && honourRange)
            {
                var part = new ByteArrayContent(body[(int)start..]);
                part.Headers.ContentRange = new ContentRangeHeaderValue(start, body.Length - 1, body.Length);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = part });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
        }
    }

    private sealed class Recorder : IProgress<DownloadProgress>
    {
        public List<DownloadProgress> Seen { get; } = [];
        public void Report(DownloadProgress value) => Seen.Add(value);
    }

    [Fact]
    public async Task Downloads_every_file_from_the_pinned_revision_and_returns_the_model()
    {
        var hub = new FakeHub();
        var path = await new ModelDownloader(new HttpClient(hub)).DownloadAsync(Pack(), _dir, null, CancellationToken.None);

        Assert.Equal(Path.Combine(_dir, "Test-Q4_K_M.gguf"), path);
        Assert.Equal(ModelBytes, File.ReadAllBytes(path));
        Assert.Equal(ProjectorBytes, File.ReadAllBytes(Path.Combine(_dir, "mmproj-Test-Q8_0.gguf")));
        Assert.All(hub.Requests, r => Assert.StartsWith("/owner/repo/resolve/abc123/", r.Path));
        Assert.Empty(Directory.EnumerateFiles(_dir, "*" + ModelDownloader.PartialSuffix));
    }

    [Fact]
    public async Task Projector_comes_first_so_a_finished_model_always_has_its_eyes()
    {
        var hub = new FakeHub();
        await new ModelDownloader(new HttpClient(hub)).DownloadAsync(Pack(), _dir, null, CancellationToken.None);
        Assert.EndsWith("mmproj-Test-Q8_0.gguf", hub.Requests[0].Path);
    }

    [Fact]
    public async Task A_partial_download_resumes_where_it_stopped()
    {
        File.WriteAllBytes(Path.Combine(_dir, "mmproj-Test-Q8_0.gguf"), ProjectorBytes);
        File.WriteAllBytes(Path.Combine(_dir, "Test-Q4_K_M.gguf" + ModelDownloader.PartialSuffix), ModelBytes[..1200]);
        var hub = new FakeHub();

        var path = await new ModelDownloader(new HttpClient(hub)).DownloadAsync(Pack(), _dir, null, CancellationToken.None);

        Assert.Equal(ModelBytes, File.ReadAllBytes(path));
        var only = Assert.Single(hub.Requests); // the finished projector isn't fetched again
        Assert.Equal(1200, only.From);
    }

    [Fact]
    public async Task A_server_that_ignores_the_resume_request_still_gives_a_correct_file()
    {
        File.WriteAllBytes(Path.Combine(_dir, "Test-Q4_K_M.gguf" + ModelDownloader.PartialSuffix), ModelBytes[..1200]);
        var path = await new ModelDownloader(new HttpClient(new FakeHub(honourRange: false))).DownloadAsync(Pack(), _dir, null, CancellationToken.None);
        Assert.Equal(ModelBytes, File.ReadAllBytes(path));
    }

    [Fact]
    public async Task A_file_with_the_wrong_fingerprint_is_deleted_and_never_used()
    {
        var tampered = (byte[])ModelBytes.Clone();
        tampered[100] ^= 0xFF;
        var downloader = new ModelDownloader(new HttpClient(new FakeHub(model: tampered)));

        var e = await Assert.ThrowsAsync<ModelDownloadException>(() => downloader.DownloadAsync(Pack(), _dir, null, CancellationToken.None));

        Assert.Contains("damaged", e.Message);
        Assert.False(File.Exists(Path.Combine(_dir, "Test-Q4_K_M.gguf")));
        Assert.False(File.Exists(Path.Combine(_dir, "Test-Q4_K_M.gguf" + ModelDownloader.PartialSuffix)));
    }

    [Fact]
    public async Task Progress_counts_up_to_the_whole_pack()
    {
        var progress = new Recorder();
        await new ModelDownloader(new HttpClient(new FakeHub())).DownloadAsync(Pack(), _dir, progress, CancellationToken.None);

        Assert.NotEmpty(progress.Seen);
        Assert.Equal(ProjectorBytes.Length + ModelBytes.Length, progress.Seen[^1].Done);
        Assert.Equal(progress.Seen.Select(p => p.Done).Order(), progress.Seen.Select(p => p.Done));
        Assert.All(progress.Seen, p => Assert.Equal(ProjectorBytes.Length + ModelBytes.Length, p.Total));
    }

    [Fact]
    public async Task Cancelling_keeps_what_was_downloaded_for_next_time()
    {
        using var cts = new CancellationTokenSource();
        var progress = new CancelAfterFirst(cts);
        var downloader = new ModelDownloader(new HttpClient(new FakeHub()), bufferSize: 512);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => downloader.DownloadAsync(Pack(), _dir, progress, cts.Token));

        Assert.True(ModelDownloader.BytesPresent(Pack(), _dir) > 0);
    }

    private sealed class CancelAfterFirst(CancellationTokenSource cts) : IProgress<DownloadProgress>
    {
        public void Report(DownloadProgress value) => cts.Cancel();
    }

    [Fact]
    public void Bytes_present_counts_finished_and_partial_files()
    {
        Assert.Equal(0, ModelDownloader.BytesPresent(Pack(), _dir));
        File.WriteAllBytes(Path.Combine(_dir, "mmproj-Test-Q8_0.gguf"), ProjectorBytes);
        File.WriteAllBytes(Path.Combine(_dir, "Test-Q4_K_M.gguf" + ModelDownloader.PartialSuffix), new byte[700]);
        Assert.Equal(ProjectorBytes.Length + 700, ModelDownloader.BytesPresent(Pack(), _dir));
    }

    [Fact]
    public void Default_pack_is_the_vision_brain_pinned_to_one_revision()
    {
        var pack = ModelPack.VisionDefault;
        Assert.Equal(2, pack.Files.Count);
        Assert.True(ModelLocator.IsProjector(pack.Files[0].Name));
        Assert.Equal("Qwen3VL-2B-Instruct-Q4_K_M.gguf", pack.Model.Name);
        Assert.Matches("^[0-9a-f]{40}$", pack.Revision);
        Assert.All(pack.Files, f => Assert.Matches("^[0-9a-f]{64}$", f.Sha256));
        Assert.StartsWith("https://huggingface.co/Qwen/Qwen3-VL-2B-Instruct-GGUF/resolve/", pack.UrlFor(pack.Model).AbsoluteUri);
        Assert.InRange(pack.TotalBytes, 1_000_000_000, 2_000_000_000); // small enough to download and keep loaded
    }

    [Fact]
    public void A_partial_download_is_not_mistaken_for_a_browser_download_in_progress() =>
        Assert.IsType<ModelStatus.Missing>(PartialOnly());

    private ModelStatus PartialOnly()
    {
        File.WriteAllBytes(Path.Combine(_dir, "Test-Q4_K_M.gguf" + ModelDownloader.PartialSuffix), new byte[10]);
        return ModelLocator.Find(_dir, null);
    }
}
