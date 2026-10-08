namespace LocalMeetingSubtitle.Core.Audio;

/// <summary>
/// Energy-based segmentation for <b>offline</b> (non-streaming) engines such as SenseVoice,
/// which cannot emit incremental output. Splits the stream on silence and caps segment length.
/// Segment boundaries are approximate; the final text is still produced by the model.
/// </summary>
public sealed class AudioSegmenter
{
    private readonly int _sampleRate;
    private readonly double _silenceRms;
    private readonly int _minSilenceSamples;
    private readonly int _minSpeechSamples;
    private readonly int _maxSegmentSamples;

    private readonly List<float> _buffer = new();
    private int _silenceRun;
    private int _speechSamples;

    public AudioSegmenter(
        int sampleRate = 16000,
        double silenceRms = 0.010,
        double minSilenceSeconds = 0.6,
        double maxSegmentSeconds = 15.0,
        double minSpeechSeconds = 0.35)
    {
        _sampleRate = sampleRate;
        _silenceRms = silenceRms;
        _minSilenceSamples = (int)(minSilenceSeconds * sampleRate);
        _minSpeechSamples = (int)(minSpeechSeconds * sampleRate);
        _maxSegmentSamples = (int)(maxSegmentSeconds * sampleRate);
    }

    /// <summary>Feeds samples and returns any completed segments.</summary>
    public IReadOnlyList<float[]> Push(ReadOnlySpan<float> samples)
    {
        List<float[]>? completed = null;

        // Process in 20 ms frames to make the silence decision stable.
        int frameSize = Math.Max(1, _sampleRate / 50);
        int offset = 0;
        while (offset < samples.Length)
        {
            int n = Math.Min(frameSize, samples.Length - offset);
            var frame = samples.Slice(offset, n);
            offset += n;

            double rms = AudioMath.Rms(frame);
            _buffer.AddRange(frame.ToArray());

            if (rms < _silenceRms)
            {
                _silenceRun += n;
            }
            else
            {
                _silenceRun = 0;
                _speechSamples += n;
            }

            bool cut = false;
            if (_silenceRun >= _minSilenceSamples && _speechSamples >= _minSpeechSamples)
            {
                cut = true;
            }
            else if (_buffer.Count >= _maxSegmentSamples)
            {
                cut = true;
            }

            if (cut)
            {
                var segment = EmitSegment(trimTrailingSilence: true);
                if (segment != null)
                {
                    (completed ??= new List<float[]>()).Add(segment);
                }
            }
        }

        return (IReadOnlyList<float[]>?)completed ?? Array.Empty<float[]>();
    }

    /// <summary>Flushes any trailing speech (call on stop/pause).</summary>
    public float[]? Flush()
    {
        if (_speechSamples < _minSpeechSamples)
        {
            Reset();
            return null;
        }
        return EmitSegment(trimTrailingSilence: true);
    }

    public void Reset()
    {
        _buffer.Clear();
        _silenceRun = 0;
        _speechSamples = 0;
    }

    private float[]? EmitSegment(bool trimTrailingSilence)
    {
        if (_buffer.Count == 0)
        {
            Reset();
            return null;
        }

        int end = _buffer.Count;
        if (trimTrailingSilence)
        {
            end = Math.Max(0, end - _silenceRun);
        }

        var result = _buffer.GetRange(0, end).ToArray();
        Reset();
        return result.Length > 0 ? result : null;
    }
}
