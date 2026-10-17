using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Pixel.Core.Brain;
using Pixel.Core.Storage;

namespace Pixel.Core.Tests.Brain;

public sealed class DownloadRetryTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("pixel-retry-").FullName;
    public void Dispose() => Directory.Delete(_dir, true);

    private static readonly byte[] Body = Enumerable.Range(0, 6000).Select(i => (byte)(i * 31)).ToArray();

    private static ModelPack Pack => new("Test brain", "owner/repo", "abc123",
        [new("Test-Q4_K_M.gguf", Body.Length, Convert.ToHexStringLower(SHA256.HashData(Body)))]);

    /// <summary>A stream that delivers some bytes, then fails like a connection the other side cut.</summary>
    private sealed class CutStream(byte[] data, int cutAfter) : Stream
    {
        private int _pos;
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_pos >= cutAfter) throw new IOException("Unable to read data from the transport connection: An existing connection was forcibly closed by the remote host.");
            var n = Math.Min(count, Math.Min(cutAfter, data.Length) - _pos);
            Array.Copy(data, _pos, buffer, offset, n);
            _pos += n;
            return n;
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _pos; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>Plays back a script of responses: a cut after N bytes, an HTTP status, or the full (ranged) file.</summary>
    private sealed class FlakyHub(params object[] script) : HttpMessageHandler
    {
        public List<long?> RangeStarts { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var from = r.Headers.Range?.Ranges.First().From;
            RangeStarts.Add(from);
            var step = RangeStarts.Count <= script.Length ? script[RangeStarts.Count - 1] : "ok";
            if (step is HttpStatusCode code) return Task.FromResult(new HttpResponseMessage(code));
            var start = (int)(from ?? 0);
            var rest = Body[start..];
            HttpContent content = step is int cut ? new StreamContent(new CutStream(rest, cut)) : new ByteArrayContent(rest);
            var response = new HttpResponseMessage(from is null ? HttpStatusCode.OK : HttpStatusCode.PartialContent) { Content = content };
            if (from is not null) content.Headers.ContentRange = new ContentRangeHeaderValue(start, Body.Length - 1, Body.Length);
            return Task.FromResult(response);
        }
    }

    private ModelDownloader Downloader(FlakyHub hub) =>
        new(new HttpClient(hub), bufferSize: 1000, retryDelay: _ => TimeSpan.Zero);

    private sealed class Recorder : IProgress<DownloadProgress>
    {
        public List<DownloadProgress> Seen { get; } = [];
        public void Report(DownloadProgress value) => Seen.Add(value);
    }

    [Fact]
    public async Task A_cut_connection_resumes_by_itself_where_it_stopped()
    {
        var hub = new FlakyHub(2000);
        var path = await Downloader(hub).DownloadAsync(Pack, _dir, null, CancellationToken.None);

        Assert.Equal(Body, File.ReadAllBytes(path));
        Assert.Equal([null, 2000L], hub.RangeStarts);
    }

    [Fact]
    public async Task Several_cuts_in_a_row_still_finish()
    {
        var hub = new FlakyHub(1000, 1000, 1000, 1000);
        var path = await Downloader(hub).DownloadAsync(Pack, _dir, null, CancellationToken.None);
        Assert.Equal(Body, File.ReadAllBytes(path));
        Assert.Equal(5, hub.RangeStarts.Count);
    }

    [Fact]
    public async Task Reconnecting_is_shown_in_the_progress()
    {
        var progress = new Recorder();
        await Downloader(new FlakyHub(2000)).DownloadAsync(Pack, _dir, progress, CancellationToken.None);
        Assert.Contains(progress.Seen, p => p.Reconnecting);
        Assert.False(progress.Seen[^1].Reconnecting);
    }

    [Fact]
    public async Task A_connection_that_keeps_dropping_gives_up_with_a_network_message_not_a_disk_one()
    {
        var hub = new FlakyHub(Enumerable.Repeat<object>(0, ModelDownloader.MaxAttempts + 2).ToArray());
        var e = await Assert.ThrowsAsync<ModelDownloadException>(() => Downloader(hub).DownloadAsync(Pack, _dir, null, CancellationToken.None));

        Assert.Equal(ModelDownloader.MaxAttempts, hub.RangeStarts.Count);
        Assert.Contains("connection", e.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("free space", e.Message);
        Assert.Contains("pick up where I left off", e.Message);
    }

    [Fact]
    public async Task A_busy_server_is_retried()
    {
        var hub = new FlakyHub(HttpStatusCode.ServiceUnavailable, HttpStatusCode.TooManyRequests);
        var path = await Downloader(hub).DownloadAsync(Pack, _dir, null, CancellationToken.None);
        Assert.Equal(Body, File.ReadAllBytes(path));
    }

    [Fact]
    public async Task A_missing_file_is_not_retried()
    {
        var hub = new FlakyHub(HttpStatusCode.NotFound);
        await Assert.ThrowsAsync<ModelDownloadException>(() => Downloader(hub).DownloadAsync(Pack, _dir, null, CancellationToken.None));
        Assert.Single(hub.RangeStarts);
    }

    [Fact]
    public async Task Stopping_during_a_reconnect_wait_stops_at_once()
    {
        using var cts = new CancellationTokenSource();
        var downloader = new ModelDownloader(new HttpClient(new FlakyHub(2000)), bufferSize: 1000,
            retryDelay: _ => { cts.Cancel(); return TimeSpan.FromMinutes(5); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => downloader.DownloadAsync(Pack, _dir, null, cts.Token));
    }
}

public sealed class ModelFolderTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("pixel-folders-").FullName;
    public void Dispose() => Directory.Delete(_root, true);

    [Fact]
    public void The_brain_lives_outside_documents_so_onedrive_never_syncs_it()
    {
        var dir = new Settings().ModelDirectory;
        Assert.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), dir);
        Assert.DoesNotContain(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), dir);
    }

    [Fact]
    public void A_brain_in_an_older_folder_is_still_found()
    {
        var primary = Path.Combine(_root, "models");
        var legacy = Directory.CreateDirectory(Path.Combine(_root, "Documents", "pixel")).FullName;
        var model = Path.Combine(legacy, "Qwen3VL-2B-Instruct-Q4_K_M.gguf");
        File.WriteAllText(model, "m");

        Assert.Equal(model, Assert.IsType<ModelStatus.Found>(ModelLocator.Find(primary, null, [legacy])).Path);
    }

    [Fact]
    public void Without_any_brain_the_download_goes_to_the_main_folder()
    {
        var primary = Path.Combine(_root, "models");
        var legacy = Directory.CreateDirectory(Path.Combine(_root, "Documents", "pixel")).FullName;
        Assert.Equal(primary, Assert.IsType<ModelStatus.Missing>(ModelLocator.Find(primary, null, [legacy])).Directory);
    }

    [Fact]
    public void Older_folders_include_documents_pixel() =>
        Assert.Contains(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "pixel"), Settings.OlderModelFolders);
}
