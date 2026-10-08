namespace LocalMeetingSubtitle.Core.Audio;

/// <summary>
/// Streaming mono sample-rate converter using a windowed-sinc (Blackman) lowpass kernel
/// with unity DC gain. Works for both up- and down-sampling and keeps phase continuity
/// across blocks, so no samples are lost or duplicated at buffer boundaries.
/// </summary>
public sealed class StreamingResampler
{
    private const int HalfTaps = 16; // kernel spans 32 source samples

    private readonly int _inRate;
    private readonly int _outRate;
    private readonly double _step;      // source samples advanced per output sample
    private readonly double _fc;        // normalized cutoff (fraction of source Nyquist)
    private readonly List<float> _buffer = new();
    private double _pos;                // fractional source position of the next output sample

    public StreamingResampler(int inputRate, int outputRate)
    {
        if (inputRate <= 0) throw new ArgumentOutOfRangeException(nameof(inputRate));
        if (outputRate <= 0) throw new ArgumentOutOfRangeException(nameof(outputRate));
        _inRate = inputRate;
        _outRate = outputRate;
        _step = (double)inputRate / outputRate;
        // Anti-alias only when downsampling.
        _fc = Math.Min(1.0, (double)outputRate / inputRate);
    }

    public int InputRate => _inRate;
    public int OutputRate => _outRate;
    public bool IsPassthrough => _inRate == _outRate;

    public int LatencySamples => HalfTaps * 2;

    /// <summary>Feeds a block of mono samples and returns the produced output samples.</summary>
    public float[] Process(ReadOnlySpan<float> input)
    {
        if (IsPassthrough)
        {
            return input.ToArray();
        }

        for (int i = 0; i < input.Length; i++) _buffer.Add(input[i]);

        var output = new List<float>((int)(input.Length * _outRate / _inRate) + 8);
        int needed = HalfTaps + 1;

        while (true)
        {
            int center = (int)Math.Floor(_pos);
            if (center - needed < 0) break;
            if (center + needed >= _buffer.Count) break;

            double acc = 0;
            double weightSum = 0;
            for (int j = center - HalfTaps + 1; j <= center + HalfTaps; j++)
            {
                double x = _pos - j;
                double w = Kernel(x);
                acc += _buffer[j] * w;
                weightSum += w;
            }
            output.Add(weightSum != 0 ? (float)(acc / weightSum) : 0f);
            _pos += _step;
        }

        // Drop source samples that can no longer influence future outputs.
        int discard = (int)Math.Floor(_pos) - HalfTaps;
        if (discard > 0)
        {
            if (discard >= _buffer.Count)
            {
                discard = _buffer.Count;
            }
            _buffer.RemoveRange(0, discard);
            _pos -= discard;
        }

        return output.ToArray();
    }

    /// <summary>Flushes the remaining buffered samples (zero-padded tail).</summary>
    public float[] Flush()
    {
        if (IsPassthrough || _buffer.Count == 0)
        {
            _buffer.Clear();
            _pos = 0;
            return Array.Empty<float>();
        }

        // Zero-pad so the tail can be produced, then consume everything.
        var tail = new float[HalfTaps * 2 + (int)Math.Ceiling(_step) + 2];
        var output = Process(tail);
        _buffer.Clear();
        _pos = 0;
        return output;
    }

    public void Reset()
    {
        _buffer.Clear();
        _pos = 0;
    }

    private double Kernel(double x)
    {
        if (Math.Abs(x) >= HalfTaps) return 0;
        double sinc = Sinc(_fc * x) * _fc;
        // Blackman window over [-HalfTaps, HalfTaps]
        double n = x / HalfTaps; // -1..1
        double window = 0.42 + 0.5 * Math.Cos(Math.PI * n) + 0.08 * Math.Cos(2 * Math.PI * n);
        return sinc * window;
    }

    private static double Sinc(double x)
    {
        if (Math.Abs(x) < 1e-9) return 1.0;
        double pix = Math.PI * x;
        return Math.Sin(pix) / pix;
    }
}
