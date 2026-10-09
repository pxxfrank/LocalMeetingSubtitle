using LocalMeetingSubtitle.Core.Models;

namespace LocalMeetingSubtitle.Core.Abstractions;

/// <summary>
/// Wraps the native offline speaker-diarization model. One blocking whole-file call, so it is an
/// engine (not a streaming session). Implementations must surface failures, never throw from Dispose.
/// </summary>
public interface ISpeakerDiarizationEngine : IDisposable
{
    string Id { get; }
    bool IsInitialized { get; }
    /// <summary>The audio sample rate the engine expects (16 000 Hz).</summary>
    int SampleRate { get; }

    /// <summary>Loads the models. Returns a failure result rather than throwing on a bad config.</summary>
    DiarizationInitResult Initialize(DiarizationEngineOptions options);

    /// <summary>
    /// Runs diarization over 16 kHz mono samples. Honors <paramref name="cancellationToken"/>
    /// cooperatively via the progress callback.
    /// </summary>
    IReadOnlyList<DiarizationInterval> Process(
        float[] samples,
        IProgress<DiarizationProgress>? progress,
        CancellationToken cancellationToken);
}

/// <summary>Decodes a local audio file to the mono float32 samples the engine consumes.</summary>
public interface IAudioFileLoader
{
    bool IsSupported(string path);
    TimeSpan GetDuration(string path);
    /// <summary>Decodes + downmixes + resamples <paramref name="path"/> to mono at the target rate.</summary>
    float[] LoadMono(string path, int targetSampleRate, out int sampleRate);
}

/// <summary>
/// Maps diarization intervals onto subtitle segments. Pure and side-effect free: it computes
/// decisions but never persists them, and never edits segment text.
/// </summary>
public interface ISpeakerAlignmentService
{
    IReadOnlyList<AlignmentOutcome> Align(
        IReadOnlyList<SubtitleSegment> segments,
        IReadOnlyList<DiarizationInterval> intervals,
        IReadOnlyDictionary<int, string> speakerIdByRawIndex,
        AlignmentOptions? options = null);
}

/// <summary>Persists diarization runs, speakers, raw intervals and per-segment assignments.</summary>
public interface ISpeakerRepository : IAsyncDisposable
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task SaveRunAsync(DiarizationRun run, CancellationToken cancellationToken = default);
    Task<DiarizationRun?> GetLatestRunAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>Replaces the raw intervals of a run (re-analysis overwrites the previous set).</summary>
    Task ReplaceIntervalsAsync(string runId, IReadOnlyList<SpeakerInterval> intervals, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SpeakerInterval>> GetIntervalsAsync(string runId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Speaker>> GetSpeakersAsync(string sessionId, CancellationToken cancellationToken = default);
    Task UpsertSpeakerAsync(Speaker speaker, CancellationToken cancellationToken = default);
    /// <summary>
    /// Re-points every assignment from one speaker to another and marks the source merged.
    /// Returns the affected segment ids so the merge can be undone.
    /// </summary>
    Task<IReadOnlyList<long>> MergeSpeakersAsync(string sessionId, string fromSpeakerId, string intoSpeakerId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SpeakerAssignment>> GetAssignmentsAsync(string sessionId, CancellationToken cancellationToken = default);
    /// <summary>
    /// Upserts assignments. Rows whose current source is <see cref="SpeakerAssignmentSource.Manual"/>
    /// are preserved unless <paramref name="overwriteManual"/> is true.
    /// </summary>
    Task UpsertAssignmentsAsync(IReadOnlyList<SpeakerAssignment> assignments, bool overwriteManual, CancellationToken cancellationToken = default);
    /// <summary>Sets the speaker of one segment (the manual-reassignment path).</summary>
    Task SetAssignmentSpeakerAsync(string sessionId, long segmentId, string? speakerId, SpeakerAssignmentSource source, CancellationToken cancellationToken = default);
}

/// <summary>Registry of local audio assets (imported files + optional recordings) and their retention.</summary>
public interface IAudioAssetRepository : IAsyncDisposable
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task AddAsync(AudioAsset asset, CancellationToken cancellationToken = default);
    Task<AudioAsset?> GetAsync(string audioAssetId, CancellationToken cancellationToken = default);
    /// <summary>Temporary assets whose <see cref="AudioAsset.DeleteAfterUtc"/> has passed.</summary>
    Task<IReadOnlyList<AudioAsset>> GetExpiredTemporaryAsync(DateTimeOffset now, CancellationToken cancellationToken = default);
    Task DeleteAsync(string audioAssetId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Orchestrates post-meeting diarization: load audio → run the engine → label speakers → align to
/// the session's subtitle segments → persist, on a dedicated below-normal-priority thread.
/// </summary>
public interface ISpeakerDiarizationService
{
    /// <summary>True while a run is in flight (a single run at a time).</summary>
    bool IsBusy { get; }

    Task<DiarizationResult> RunAsync(
        DiarizationRequest request,
        IProgress<DiarizationProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
