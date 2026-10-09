namespace Pixel.App.Chat;

/// <summary>
/// Cancels a turn when the brain makes no progress (no status, progress or token) for a while.
/// Unlike a plain timeout, long answers keep going as long as words keep arriving.
/// </summary>
internal sealed class StallWatchdog : IDisposable
{
    private readonly CancellationTokenSource _turn;
    private readonly TimeSpan _limit;
    private readonly Timer _timer;
    private int _fired;

    public StallWatchdog(CancellationTokenSource turn, TimeSpan limit)
    {
        _turn = turn;
        _limit = limit;
        _timer = new Timer(_ => Fire(), null, limit, Timeout.InfiniteTimeSpan);
    }

    public bool Fired => Volatile.Read(ref _fired) == 1;

    public void Pet() => _timer.Change(_limit, Timeout.InfiniteTimeSpan);

    private void Fire()
    {
        Interlocked.Exchange(ref _fired, 1);
        try
        {
            _turn.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The turn already finished.
        }
    }

    public void Dispose() => _timer.Dispose();
}
