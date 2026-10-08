namespace LocalMeetingSubtitle.Core.Audio;

/// <summary>
/// Streaming mono sample-rate converter using a windowed-sinc (Blackman) lowpass kernel
/// with unity DC gain. Works for both up- and down-sampling and keeps phase continuity
/// across blocks, so no samples are lost or duplicated at buffer boundaries.
///
/// The input buffer is primed with <see cref="HalfTaps"/> zero samples and the read position
/// starts at that offset, so the first real sample lands on the kernel center (no leading
/// silence shift) and no negative-index sample is ever required.
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
        Seed();
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

        while (true)
        {
            int center = (int)Math.Floor(_pos);
            if (center + HalfTaps >= _buffer.Count) break; // need more future samples

            double acc = 0;
            double weightSum = 0;
            for (int j = center - HalfTaps + 1; j <= center + HalfTaps; j++)
            {
                double w = Kernel(_pos - j);
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
            if (discard > _buffer.Count) discard = _buffer.Count;
            _buffer.RemoveRange(0, discard);
            _pos -= discard;
        }

        return output.ToArray();
    }

    /// <summary>Flushes the remaining buffered samples (zero-padded tail).</summary>
    public float[] Flush()
    {
        if (IsPassthrough)
        {
            Reset();
            return Array.Empty<float>();
        }

        var tail = new float[HalfTaps + 2];
        var output = Process(tail);
        Reset();
        return output;
    }

    public void Reset()
    {
        _buffer.Clear();
        Seed();
    }

    private void Seed()
    {
        for (int i = 0; i < HalfTaps; i++) _buffer.Add(0f);
        _pos = HalfTaps;
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
