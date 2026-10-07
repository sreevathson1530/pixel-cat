namespace Pixel.Core.Input;

/// <summary>
/// Detects petting: the pointer moving back and forth over the cat's head.
/// Three direction reversals within 1.5 s start purring, which lasts 2 s after the last stroke.
/// </summary>
public sealed class PettingDetector
{
    private const double Window = 1.5;
    private const double Linger = 2.0;
    private const double MinTravel = 3.0;
    private const int StrokesNeeded = 3;

    private readonly Queue<double> _reversals = new();
    private double? _anchorX;
    private int _direction;
    private double _purrUntil = double.NegativeInfinity;

    public void OnPointer(double x, double t)
    {
        if (_anchorX is not { } anchor)
        {
            _anchorX = x;
            return;
        }

        var delta = x - anchor;
        if (Math.Abs(delta) < MinTravel) return;

        var dir = Math.Sign(delta);
        if (_direction != 0 && dir != _direction)
        {
            _reversals.Enqueue(t);
            while (_reversals.Count > 0 && _reversals.Peek() < t - Window) _reversals.Dequeue();
            if (_reversals.Count >= StrokesNeeded) _purrUntil = t + Linger;
            else if (_purrUntil >= t) _purrUntil = t + Linger; // keep purring while petting continues
        }
        _direction = dir;
        _anchorX = x;
    }

    public bool IsPurring(double t) => t <= _purrUntil;

    /// <summary>Forget the stroke in progress (pointer left the head).</summary>
    public void Leave()
    {
        _anchorX = null;
        _direction = 0;
    }
}
