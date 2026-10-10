using LocalMeetingSubtitle.Core.Models;

namespace LocalMeetingSubtitle.Core.Abstractions;

/// <summary>Which stage of a file-transcription job a progress report belongs to.</summary>
public enum FileTranscriptionPhase
{
    Decode,
    Transcribe,
    Diarize,
    Assemble
}

public readonly record struct FileTranscriptionProgress(
    FileTranscriptionPhase Phase,
    double Fraction,
    TimeSpan Processed,
    TimeSpan Total,
    int SegmentsEmitted);

/// <summary>
/// One file-transcription job. The caller owns <see cref="Engine"/> (initialized for the chosen
/// mode) and <see cref="TranscriptionOptions"/>: resolving a transcription mode needs the ASR
/// project, which this Core service must not depend on.
/// </summary>
public sealed record FileTranscriptionRequest(
    string InputPath,
    IAsrEngine Engine,
    OfflineTranscriptionOptions TranscriptionOptions,
    int? AudioStreamIndex = null,
    bool RunDiarization = true,
    SpeakerCountMode DiarizationCountMode = SpeakerCountMode.Auto,
    int ManualSpeakerCount = 0,
    double ClusteringThreshold = 0.5,
    DialogueAssemblyOptions? DialogueOptions = null,
    string? Title = null);

public sealed class FileTranscriptionResult
{
    public string SessionId { get; init; } = "";
    public IReadOnlyList<OfflineTranscriptSegment> Segments { get; init; } = Array.Empty<OfflineTranscriptSegment>();
    /// <summary>Speakers merged into turns; always present (all-unknown when diarization did not run).</summary>
    public DialogueTranscript? Dialogue { get; init; }
    public TimeSpan AudioDuration { get; init; }
    public TimeSpan Elapsed { get; init; }
    public double Rtf => AudioDuration.TotalSeconds > 0 ? Elapsed.TotalSeconds / AudioDuration.TotalSeconds : 0;
    public bool Completed { get; init; }
    public bool Cancelled { get; init; }
    /// <summary>True when speaker diarization ran and persisted.</summary>
    public bool Diarized { get; init; }
    /// <summary>Set for a hard failure (probe/decode/cancel).</summary>
    public string? Error { get; init; }
    /// <summary>Set when the transcript succeeded but a later stage degraded (e.g. diarization busy).</summary>
    public string? Warning { get; init; }
}

/// <summary>
/// Runs one file end to end: decode → transcribe → persist → diarize → assemble the role-tagged
/// dialogue. Phase 4 wraps this in a queue; this contract is deliberately single-job.
/// </summary>
public interface IFileTranscriptionService
{
    bool IsBusy { get; }

    Task<FileTranscriptionResult> RunAsync(
        FileTranscriptionRequest request,
        IProgress<FileTranscriptionProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
