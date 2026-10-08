namespace LocalMeetingSubtitle.Diagnostics;

/// <summary>
/// Fixed-size moving-window average used to smooth noisy per-sample metrics such as CPU usage.
/// </summary>
public sealed class RollingAverage
{
    private readonly double[] _values;
    private int _next;
    private int _count;
    private double _sum;

    public RollingAverage(int capacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _values = new double[capacity];
    }

    public int Capacity => _values.Length;

    public int Count => _count;

    public void Add(double value)
    {
        if (_count == _values.Length)
        {
            _sum -= _values[_next];
        }
        else
        {
            _count++;
        }

        _values[_next] = value;
        _sum += value;
        _next = (_next + 1) % _values.Length;
    }

    public double Average => _count == 0 ? 0.0 : _sum / _count;

    public void Reset()
    {
        Array.Clear(_values, 0, _values.Length);
        _next = 0;
        _count = 0;
        _sum = 0.0;
    }
}
