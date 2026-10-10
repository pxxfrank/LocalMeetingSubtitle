namespace LocalMeetingSubtitle.Core.Audio;

/// <summary>
/// Energy-based segmentation for <b>offline</b> (non-streaming) engines such as SenseVoice,
/// which cannot emit incremental output. Splits the stream on silence and caps segment length.
/// Segment boundaries are approximate; the final text is still produced by the model.
///
/// Use <see cref="PushSegments"/> when the position of each region on the audio timeline matters
/// (file transcription): it reports absolute sample indices rather than bare buffers. The older
/// <see cref="Push"/>/<see cref="Flush"/> API returns the same audio as before.
/// </summary>
public sealed class AudioSegmenter
{
    private readonly int _sampleRate;
    private readonly double _silenceRms;
    private readonly int _minSilenceSamples;
    private readonly int _minSpeechSamples;
    private readonly int _maxSegmentSamples;
    private readonly int _overlapSamples;

    private readonly List<float> _buffer = new();
    private int _silenceRun;
    private int _speechSamples;

    /// <summary>Absolute index just past the last sample fed since the last <see cref="Reset"/>.</summary>
    private long _nextSampleIndex;
    /// <summary>Absolute index of <c>_buffer[0]</c>.</summary>
    private long _bufferStartIndex;
    /// <summary>The buffered audio was carried over from the previous max-length cut.</summary>
    private bool _bufferHasOverlapPrefix;

    public AudioSegmenter(
        int sampleRate = 16000,
        double silenceRms = 0.010,
        double minSilenceSeconds = 0.6,
        double maxSegmentSeconds = 15.0,
        double minSpeechSeconds = 0.35,
        double overlapSeconds = 0.0)
    {
        _sampleRate = sampleRate;
        _silenceRms = silenceRms;
        _minSilenceSamples = (int)(minSilenceSeconds * sampleRate);
        _minSpeechSamples = (int)(minSpeechSeconds * sampleRate);
        _maxSegmentSamples = (int)(maxSegmentSeconds * sampleRate);
        // Overlap is re-decoded as the head of the following segment. Keeping it under half the cap
        // guarantees forward progress: each cut still consumes new audio.
        _overlapSamples = Math.Clamp((int)(overlapSeconds * sampleRate), 0, _maxSegmentSamples / 2);
    }

    /// <summary>Feeds samples and returns any completed segments.</summary>
    public IReadOnlyList<float[]> Push(ReadOnlySpan<float> samples)
    {
        var segments = PushSegments(samples);
        if (segments.Count == 0)
        {
            return Array.Empty<float[]>();
        }

        var result = new float[segments.Count][];
        for (int i = 0; i < segments.Count; i++)
        {
            result[i] = segments[i].Samples;
        }

        return result;
    }

    /// <summary>Feeds samples and returns completed segments with their absolute sample positions.</summary>
    public IReadOnlyList<SpeechSegment> PushSegments(ReadOnlySpan<float> samples)
    {
        List<SpeechSegment>? completed = null;

        // Process in 20 ms frames to make the silence decision stable.
        int frameSize = Math.Max(1, _sampleRate / 50);
        int offset = 0;
        while (offset < samples.Length)
        {
            int n = Math.Min(frameSize, samples.Length - offset);
            var frame = samples.Slice(offset, n);
            offset += n;

            if (_buffer.Count == 0)
            {
                _bufferStartIndex = _nextSampleIndex;
            }

            _nextSampleIndex += n;
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
            bool cutByLength = false;
            if (_silenceRun >= _minSilenceSamples && _speechSamples >= _minSpeechSamples)
            {
                cut = true;
            }
            else if (_buffer.Count >= _maxSegmentSamples)
            {
                cut = true;
                cutByLength = true;
            }

            if (cut)
            {
                var segment = EmitSegment(trimTrailingSilence: true, carryOverlap: cutByLength);
                if (segment != null)
                {
                    (completed ??= new List<SpeechSegment>()).Add(segment.Value);
                }
            }
        }

        return (IReadOnlyList<SpeechSegment>?)completed ?? Array.Empty<SpeechSegment>();
    }

    /// <summary>Flushes any trailing speech (call on stop/pause).</summary>
    public float[]? Flush() => FlushSegment()?.Samples;

    /// <summary>Flushes any trailing speech, keeping its absolute sample position.</summary>
    public SpeechSegment? FlushSegment()
    {
        if (_speechSamples < _minSpeechSamples)
        {
            ResetBuffer();
            return null;
        }

        return EmitSegment(trimTrailingSilence: true, carryOverlap: false);
    }

    /// <summary>Clears all buffered audio and restarts the absolute sample timeline at zero.</summary>
    public void Reset()
    {
        ResetBuffer();
        _nextSampleIndex = 0;
        _bufferStartIndex = 0;
    }

    private void ResetBuffer()
    {
        _buffer.Clear();
        _silenceRun = 0;
        _speechSamples = 0;
        _bufferHasOverlapPrefix = false;
    }

    private SpeechSegment? EmitSegment(bool trimTrailingSilence, bool carryOverlap)
    {
        if (_buffer.Count == 0)
        {
            ResetBuffer();
            return null;
        }

        int end = _buffer.Count;
        if (trimTrailingSilence)
        {
            end = Math.Max(0, end - _silenceRun);
        }

        if (end <= 0)
        {
            ResetBuffer();
            return null;
        }

        long startSample = _bufferStartIndex;
        long endSample = _bufferStartIndex + end;
        bool hasOverlapPrefix = _bufferHasOverlapPrefix;

        var result = _buffer.GetRange(0, end).ToArray();

        // On a length cut the next segment restarts a little before this one ended so the model
        // keeps the sentence context; the caller strips the duplicated text.
        int retain = carryOverlap && _overlapSamples > 0 && end > _overlapSamples ? _overlapSamples : 0;
        float[] retained = retain > 0 ? _buffer.GetRange(end - retain, retain).ToArray() : Array.Empty<float>();

        _buffer.Clear();
        _buffer.AddRange(retained);
        _bufferStartIndex = endSample - retained.Length;
        _bufferHasOverlapPrefix = retained.Length > 0;
        _silenceRun = 0;
        _speechSamples = retained.Length;

        return new SpeechSegment(result, startSample, endSample, hasOverlapPrefix);
    }
}
