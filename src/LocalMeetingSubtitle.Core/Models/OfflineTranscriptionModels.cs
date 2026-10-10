namespace LocalMeetingSubtitle.Core.Models;

/// <summary>Preset used to transcribe an imported file. Each mode maps to a real model/decoding configuration.</summary>
public enum TranscriptionMode
{
    /// <summary>Smallest streaming model, greedy decoding. Lowest latency/CPU, lowest accuracy.</summary>
    Fast,
    /// <summary>Streaming model + beam search + model-level hotword boosting.</summary>
    Standard,
    /// <summary>Offline model (SenseVoice) with overlap segmentation. Best accuracy, slowest.</summary>
    HighAccuracy
}

/// <summary>Tuning for the segmented offline transcription of a file.</summary>
public sealed record OfflineTranscriptionOptions
{
    public int SampleRate { get; init; } = 16000;
    /// <summary>RMS below which a frame counts as silence.</summary>
    public double SilenceRms { get; init; } = 0.010;
    public double MinSilenceSeconds { get; init; } = 0.6;
    /// <summary>Hard cap on one recognizer input; longer speech is cut and continued.</summary>
    public double MaxSegmentSeconds { get; init; } = 15.0;
    /// <summary>Audio carried from a max-length cut into the next segment (context continuity).</summary>
    public double OverlapSeconds { get; init; }
    public double MinSpeechSeconds { get; init; } = 0.35;

    /// <summary>Fewest folded characters shared by two adjacent segments before the overlap is trimmed.</summary>
    public int MinOverlapChars { get; init; } = 3;

    /// <summary>Model id recorded on every produced segment.</summary>
    public string ModelId { get; init; } = "";

    /// <summary>
    /// Appends <see cref="TerminalPunctuation"/> to a segment whose text does not already end with
    /// sentence punctuation. Used for models that emit unpunctuated output (streaming transducers).
    /// </summary>
    public bool AppendTerminalPunctuation { get; init; }
    public string TerminalPunctuation { get; init; } = "。";
}

/// <summary>One recognized stretch of speech, with its real position on the media timeline.</summary>
public readonly record struct OfflineTranscriptSegment(
    TimeSpan Start,
    TimeSpan End,
    string Text,
    int SourceChunkId,
    string ModelId)
{
    public TimeSpan Duration => End - Start;
}

/// <summary>Outcome of transcribing one file.</summary>
public sealed class OfflineTranscriptionResult
{
    public IReadOnlyList<OfflineTranscriptSegment> Segments { get; init; } = Array.Empty<OfflineTranscriptSegment>();
    /// <summary>Length of the decoded audio.</summary>
    public TimeSpan AudioDuration { get; init; }
    /// <summary>Wall-clock time spent decoding + recognizing.</summary>
    public TimeSpan Elapsed { get; init; }
    /// <summary>Real-time factor: wall time per second of audio (lower is faster).</summary>
    public double Rtf => AudioDuration.TotalSeconds > 0 ? Elapsed.TotalSeconds / AudioDuration.TotalSeconds : 0;
    public bool Completed { get; init; }
    public bool Cancelled { get; init; }
    public string? Error { get; init; }
}

/// <summary>Progress report emitted while a file is being transcribed.</summary>
public readonly record struct OfflineTranscriptionProgress(TimeSpan Processed, TimeSpan Total, int SegmentsEmitted);
