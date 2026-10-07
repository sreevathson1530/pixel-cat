namespace Pixel.Core.Input;

/// <summary>
/// Turns raw input timing into the cat's mood signals. Only timestamps are recorded — never which key.
/// All times are in seconds on any monotonic clock.
/// </summary>
public sealed class ActivityMeter
{
    public const double OverheatRate = 8;        // keys per second
    public const double OverheatSustain = 3;     // seconds of fast typing before overheating
    public const double CoolDown = 4;            // seconds below the rate before cooling off
    public const double ScrollLinger = 0.6;      // seconds the "scrolling" state outlives the last wheel event
    private const double SpeedWindow = 0.1;

    private readonly Queue<double> _keys = new();
    private readonly Queue<(double T, double Len, double Start)> _moves = new();
    private (double X, double Y, double T)? _lastMouse;
    private double _lastScroll = double.NegativeInfinity;
    private double? _coolingSince;
    private double _now;

    public double TypingRate { get; private set; }
    public bool IsTyping => TypingRate >= 1;
    public bool IsOverheated { get; private set; }
    public bool IsScrolling => _now - _lastScroll <= ScrollLinger;
    public double MouseSpeed { get; private set; }

    public void OnKeyDown(double t)
    {
        _keys.Enqueue(t);
        Update(t);
    }

    public void OnScroll(double t)
    {
        _lastScroll = t;
        Update(t);
    }

    public void OnMouse(double x, double y, double t)
    {
        if (_lastMouse is (double lx, double ly, double lt) && t > lt)
            _moves.Enqueue((t, Math.Sqrt((x - lx) * (x - lx) + (y - ly) * (y - ly)), lt));
        _lastMouse = (x, y, t);
        Update(t);
    }

    public void Update(double t)
    {
        _now = Math.Max(_now, t);

        while (_keys.Count > 0 && _keys.Peek() <= _now - OverheatSustain) _keys.Dequeue();
        TypingRate = _keys.Count(k => k > _now - 1.0);
        var sustainedRate = _keys.Count / OverheatSustain;

        if (TypingRate >= OverheatRate)
        {
            _coolingSince = null;
            if (sustainedRate >= OverheatRate) IsOverheated = true;
        }
        else if (IsOverheated)
        {
            _coolingSince ??= _now;
            if (_now - _coolingSince >= CoolDown)
            {
                IsOverheated = false;
                _coolingSince = null;
            }
        }

        while (_moves.Count > 0 && _moves.Peek().T <= _now - SpeedWindow) _moves.Dequeue();
        if (_moves.Count == 0)
        {
            MouseSpeed = 0;
        }
        else
        {
            var span = _now - _moves.Peek().Start;
            MouseSpeed = span > 0 ? _moves.Sum(m => m.Len) / span : 0;
        }
    }
}
