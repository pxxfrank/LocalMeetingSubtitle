using System.Diagnostics;
using LocalMeetingSubtitle.Core.Abstractions;
using LocalMeetingSubtitle.Core.Audio;
using LocalMeetingSubtitle.Core.Models;

namespace LocalMeetingSubtitle.Core.Transcription;

/// <summary>
/// Turns a stream of PCM blocks (a decoded file) into transcript segments that carry their real
/// position on the media timeline.
///
/// Unlike the live pipeline, this recognizes a file one VAD segment at a time (streaming or offline
/// session alike): speech is isolated by <see cref="AudioSegmenter"/>, long stretches are capped and
/// continued with a context overlap, the duplicated overlap text is trimmed, and every segment is
/// stamped with the absolute time derived from the source blocks.
/// </summary>
public sealed class OfflineTranscriptionEngine
{
    private readonly IAsrEngine _engine;
    private readonly OfflineTranscriptionOptions _options;
    private readonly Func<string, string>? _correct;
    private readonly IAppLogger _log;

    public OfflineTranscriptionEngine(
        IAsrEngine engine,
        OfflineTranscriptionOptions options,
        Func<string, string>? correct = null,
        IAppLogger? log = null)
    {
        _engine = engine;
        _options = options;
        _correct = correct;
        _log = log ?? NullLogger.Instance;
    }

    /// <summary>
    /// Consumes <paramref name="blocks"/> in order and returns every recognized stretch of speech.
    /// The enumeration is never buffered, so arbitrarily long files can be processed.
    /// </summary>
    /// <param name="totalDuration">Media duration when known; only used to fill in progress reports.</param>
    /// <param name="onSegment">
    /// Awaited as each segment is produced, so the caller can persist it before more audio is
    /// consumed (an interrupted job then keeps everything up to the last committed segment).
    /// </param>
    public async Task<OfflineTranscriptionResult> TranscribeAsync(
        IAsyncEnumerable<PcmBlock> blocks,
        IProgress<OfflineTranscriptionProgress>? progress = null,
        TimeSpan? totalDuration = null,
        CancellationToken cancellationToken = default,
        Func<OfflineTranscriptSegment, CancellationToken, Task>? onSegment = null)
    {
        if (!_engine.IsInitialized)
        {
            throw new InvalidOperationException("The ASR engine must be initialized before transcription.");
        }

        var stopwatch = Stopwatch.StartNew();
        var segments = new List<OfflineTranscriptSegment>();
        var segmenter = new AudioSegmenter(
            _options.SampleRate,
            _options.SilenceRms,
            _options.MinSilenceSeconds,
            _options.MaxSegmentSeconds,
            _options.MinSpeechSeconds,
            _options.OverlapSeconds);

        var session = _engine.CreateSession();
        var state = new DecodeState(segments, totalDuration ?? TimeSpan.Zero);

        bool cancelled = false;
        try
        {
            await foreach (var block in blocks.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                state.Anchor(block);
                state.SamplesFed += block.Samples.Length;

                foreach (var speech in segmenter.PushSegments(block.Samples))
                {
                    await DecodeSegmentAsync(state, session, speech, onSegment, cancellationToken).ConfigureAwait(false);
                    progress?.Report(state.Progress());
                }

                progress?.Report(state.Progress());
            }

            var tail = segmenter.FlushSegment();
            if (tail != null)
            {
                await DecodeSegmentAsync(state, session, tail.Value, onSegment, cancellationToken).ConfigureAwait(false);
                progress?.Report(state.Progress());
            }
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }
        catch (MediaDecodeException ex) when (ex.Kind == MediaErrorKind.Cancelled)
        {
            cancelled = true;
        }
        finally
        {
            session.Dispose();
        }

        if (cancelled)
        {
            _log.Info($"File transcription cancelled after {segments.Count} segment(s).");
        }

        stopwatch.Stop();
        return new OfflineTranscriptionResult
        {
            Segments = segments,
            AudioDuration = state.AudioDuration,
            Elapsed = stopwatch.Elapsed,
            Completed = !cancelled,
            Cancelled = cancelled
        };
    }

    private async Task DecodeSegmentAsync(
        DecodeState state,
        IAsrSession session,
        SpeechSegment speech,
        Func<OfflineTranscriptSegment, CancellationToken, Task>? onSegment,
        CancellationToken cancellationToken)
    {
        // The VAD keeps the silence that precedes speech (it is part of the same buffer). Dropping it
        // here makes the reported start time point at the speech itself, which is what a transcript needs.
        var trimmed = TrimLeadingSilence(speech);
        if (trimmed.Samples.Length == 0)
        {
            return;
        }

        // Reset first: the session is reused across segments, and a fresh Reset guarantees a segment
        // can never be concatenated onto the previous one's audio.
        session.Reset();
        session.AcceptWaveform(trimmed.Samples, _options.SampleRate);
        session.InputFinished();

        // A streaming session needs repeated Decode() calls until it has consumed every frame; an
        // offline session reports IsReady() exactly once. The same loop is correct for both.
        while (session.IsReady())
        {
            session.Decode();
        }

        string raw = session.GetResult().Text?.Trim() ?? "";
        state.ChunkId++;

        if (raw.Length == 0)
        {
            return;
        }

        string text = raw;
        if (speech.HasOverlapPrefix && state.LastRawText.Length > 0)
        {
            var dedup = OverlapTextDeduplicator.Apply(state.LastRawText, text, _options.MinOverlapChars);
            if (dedup.Dropped)
            {
                return;
            }

            text = dedup.Text;
        }

        if (text.Length == 0)
        {
            return;
        }

        state.LastRawText = raw;

        string final = _correct?.Invoke(text) ?? text;
        if (_options.AppendTerminalPunctuation)
        {
            final = EnsureTerminalPunctuation(final);
        }

        var segment = new OfflineTranscriptSegment(
            state.ReportedStart(trimmed),
            state.TimeOf(trimmed.EndSample),
            final,
            state.ChunkId,
            _options.ModelId);

        state.Segments.Add(segment);

        if (onSegment is not null)
        {
            await onSegment(segment, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Advances a segment past any leading silence so its start time is the speech onset.</summary>
    private SpeechSegment TrimLeadingSilence(SpeechSegment speech)
    {
        int frame = Math.Max(1, _options.SampleRate / 50);
        int start = 0;
        while (start < speech.Samples.Length)
        {
            int n = Math.Min(frame, speech.Samples.Length - start);
            if (AudioMath.Rms(speech.Samples.AsSpan(start, n)) >= _options.SilenceRms)
            {
                break;
            }

            start += n;
        }

        if (start == 0)
        {
            return speech;
        }

        return start >= speech.Samples.Length
            ? new SpeechSegment(Array.Empty<float>(), speech.EndSample, speech.EndSample, speech.HasOverlapPrefix)
            : new SpeechSegment(speech.Samples[start..], speech.StartSample + start, speech.EndSample, speech.HasOverlapPrefix);
    }

    private string EnsureTerminalPunctuation(string text)
    {
        if (text.Length == 0)
        {
            return text;
        }

        char last = text[^1];
        return last is '。' or '！' or '？' or '…' or '!' or '?'
            ? text
            : text + _options.TerminalPunctuation;
    }

    /// <summary>Mutable per-call decode state, kept out of the public surface.</summary>
    private sealed class DecodeState
    {
        private readonly TimeSpan _totalDuration;
        private TimeSpan _origin;
        private int _rate = 16000;
        private bool _anchored;

        public DecodeState(List<OfflineTranscriptSegment> segments, TimeSpan totalDuration)
        {
            Segments = segments;
            _totalDuration = totalDuration;
        }

        public List<OfflineTranscriptSegment> Segments { get; }
        public long SamplesFed { get; set; }
        public int ChunkId { get; set; }
        public string LastRawText { get; set; } = "";
        /// <summary>End of the last emitted segment, so a carried-over start never overlaps it.</summary>
        public TimeSpan LastEnd { get; private set; } = TimeSpan.MinValue;

        public TimeSpan AudioDuration => TimeSpan.FromSeconds(SamplesFed / (double)_rate);

        /// <summary>Uses the first block's position and rate as the origin of the media timeline.</summary>
        public void Anchor(PcmBlock block)
        {
            if (_anchored)
            {
                return;
            }

            _origin = block.Start;
            _rate = block.SampleRate > 0 ? block.SampleRate : 16000;
            _anchored = true;
        }

        public TimeSpan TimeOf(long sampleIndex) => _origin + TimeSpan.FromSeconds(sampleIndex / (double)_rate);

        /// <summary>
        /// Start of the newly covered audio: for a segment that repeats the tail of the previous one,
        /// report from where the previous segment ended so the timeline stays non-overlapping.
        /// </summary>
        public TimeSpan ReportedStart(SpeechSegment speech)
        {
            var start = TimeOf(speech.StartSample);
            var end = TimeOf(speech.EndSample);
            if (speech.HasOverlapPrefix && LastEnd > start && LastEnd < end)
            {
                start = LastEnd;
            }

            LastEnd = end;
            return start;
        }

        public OfflineTranscriptionProgress Progress() => new(AudioDuration, _totalDuration, Segments.Count);
    }
}
