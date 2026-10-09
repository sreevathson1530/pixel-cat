using System.Drawing;
using System.Text;
using Pixel.App.Native;
using Pixel.App.Shell;
using Pixel.Core.Screen;

namespace Pixel.App.Screen;

/// <summary>
/// Reads the screen every few seconds while Pixel is visible: captures, skips OCR if nothing changed, and keeps
/// the latest <see cref="ScreenSnapshot"/> in memory only. Pixel's own windows are excluded from capture.
/// </summary>
internal sealed class ScreenWatcher(ScreenReader reader, Func<IntPtr, bool> isOwnWindow) : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(3);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _loop;
    private byte[]? _lastThumb;
    private IntPtr _lastExternalForeground;
    private IntPtr _snapshotForeground;

    public ScreenSnapshot? Latest { get; private set; }

    /// <summary>Raised (on a worker thread) with each new snapshot and the lines inside the active window.</summary>
    public event Action<ScreenSnapshot, IReadOnlyList<OcrLine>>? Updated;

    public bool IsRunning => _loop is not null;

    public void Start()
    {
        if (_loop is not null) return;
        var cts = _loop = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                try
                {
                    await ReadAsync(force: false);
                    await Task.Delay(Interval, cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    // Locked PC / secure desktop: nothing to read. Try again a little later, quietly.
                    await Task.Delay(Interval * 5, CancellationToken.None);
                }
                catch (Exception e)
                {
                    Log.Error("screen watcher tick", e);
                    await Task.Delay(Interval * 3, CancellationToken.None);
                }
            }
        });
    }

    /// <summary>Stops watching and forgets everything that was read.</summary>
    public void Stop()
    {
        _loop?.Cancel();
        _loop = null;
        Latest = null;
        _lastThumb = null;
    }

    /// <summary>Reads the screen right now (used when the chat opens).</summary>
    public Task<ScreenSnapshot?> CaptureNowAsync() => ReadAsync(force: true);

    private async Task<ScreenSnapshot?> ReadAsync(bool force)
    {
        await _gate.WaitAsync();
        try
        {
            var fg = CurrentExternalForeground();
            var frame = ScreenReader.Capture();
            var thumb = ChangeDetector.Thumbnail(frame.Bgra, frame.Width, frame.Height);
            var changed = ChangeDetector.HasChanged(_lastThumb, thumb);
            _lastThumb = thumb;
            var (title, process) = ScreenReader.Describe(fg);
            // Re-read when pixels changed or you switched windows (not on title animations like spinners).
            if (!force && !changed && Latest is not null && fg == _snapshotForeground) return Latest;
            _snapshotForeground = fg;

            var lines = await reader.RecognizeAsync(frame);
            var fgRect = fg == IntPtr.Zero ? Rectangle.Empty : ToRectangle(Win32.FrameBounds(fg));
            var inside = new List<OcrLine>();
            var fgText = new StringBuilder();
            var otherText = new StringBuilder();
            foreach (var line in lines.OrderBy(l => l.Bounds.Top).ThenBy(l => l.Bounds.Left))
            {
                var c = new Point(line.Bounds.Left + line.Bounds.Width / 2, line.Bounds.Top + line.Bounds.Height / 2);
                if (fgRect.Contains(c))
                {
                    inside.Add(line);
                    fgText.AppendLine(line.Text);
                }
                else
                {
                    otherText.AppendLine(line.Text);
                }
            }

            var snapshot = new ScreenSnapshot(DateTimeOffset.Now, title, process, fgText.ToString(), otherText.ToString());
            if (DebugOcr)
                Log.Info($"[debug-ocr] {process} '{title}' lines={lines.Count} fg={snapshot.ForegroundText.Length} other={snapshot.OtherText.Length} :: {Preview(snapshot.ForegroundText)}");
            Latest = snapshot;
            Updated?.Invoke(snapshot, inside);
            return snapshot;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>The active window, ignoring Pixel's own windows (the chat panel takes focus while you type).</summary>
    private IntPtr CurrentExternalForeground()
    {
        var fg = Win32.GetForegroundWindow();
        if (fg != IntPtr.Zero && !isOwnWindow(fg)) _lastExternalForeground = fg;
        return _lastExternalForeground;
    }

    /// <summary>Developer switch (PIXEL_DEBUG_OCR=1) — the only way screen text ever reaches the log.</summary>
    private static readonly bool DebugOcr = Environment.GetEnvironmentVariable("PIXEL_DEBUG_OCR") == "1";

    private static string Preview(string text) => text.Length <= 240 ? text.ReplaceLineEndings(" | ") : text[..240].ReplaceLineEndings(" | ");

    private static Rectangle ToRectangle(Win32.RECT r) => Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);

    public void Dispose() => Stop();
}
