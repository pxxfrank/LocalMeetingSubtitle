using LocalMeetingSubtitle.Core.Models;

namespace LocalMeetingSubtitle.Core.Abstractions;

/// <summary>Persists the media files imported for file transcription.</summary>
public interface IMediaFileRepository : IAsyncDisposable
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task AddAsync(MediaFileRecord record, CancellationToken cancellationToken = default);
    Task<MediaFileRecord?> GetAsync(string mediaFileId, CancellationToken cancellationToken = default);
}

/// <summary>Persists file-transcription jobs (queue, progress, outcome).</summary>
public interface ITranscriptionJobRepository : IAsyncDisposable
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task AddAsync(TranscriptionJob job, CancellationToken cancellationToken = default);
    /// <summary>Writes the mutable columns of an existing job (status, progress, timings, outcome).</summary>
    Task UpdateAsync(TranscriptionJob job, CancellationToken cancellationToken = default);

    Task<TranscriptionJob?> GetAsync(string jobId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TranscriptionJob>> GetJobsAsync(
        TranscriptionJobStatus? status = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Builds the runnable request for a job: resolves the transcription mode and creates + initializes
/// the recognizer. Injected by the composition root because Core must not depend on the ASR project.
/// The returned request's <see cref="FileTranscriptionRequest.Engine"/> is disposed by the job
/// service after the attempt.
/// </summary>
public delegate Task<FileTranscriptionRequest> TranscriptionJobResolver(
    TranscriptionJob job,
    CancellationToken cancellationToken);

/// <summary>
/// Serializes file-transcription jobs: one FIFO worker, one job at a time, never on the live path.
/// Enqueuing only persists; the stored job is enough to (re)build the run, so a job survives a
/// restart and an interrupted job can be resumed from its last committed segment.
/// </summary>
public interface ITranscriptionJobService : IAsyncDisposable
{
    /// <summary>Raised whenever a job changes (queued, progress, finished). Handlers must be quick.</summary>
    event EventHandler<TranscriptionJob>? JobChanged;

    bool IsProcessing { get; }
    int QueueLength { get; }

    Task<TranscriptionJob> EnqueueAsync(
        TranscriptionJobRequest request,
        MediaInfo media,
        CancellationToken cancellationToken = default);

    Task<TranscriptionJob?> GetJobAsync(string jobId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TranscriptionJob>> GetJobsAsync(CancellationToken cancellationToken = default);

    /// <summary>Requests cancellation of a queued or running job. Returns false when the job is unknown or finished.</summary>
    Task<bool> CancelAsync(string jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-queues a cancelled, failed or interrupted job. It resumes from its last committed segment,
    /// so an unfinished transcript is never lost or duplicated.
    /// </summary>
    Task<bool> ResumeAsync(string jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks jobs left <see cref="TranscriptionJobStatus.Running"/> as
    /// <see cref="TranscriptionJobStatus.Interrupted"/> so a previous crash is visible and resumable.
    /// Returns the number of jobs affected.
    /// </summary>
    Task<int> RecoverUnfinishedAsync(CancellationToken cancellationToken = default);

    /// <summary>Starts the background worker (idempotent).</summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>Stops the worker after the in-flight job finishes or is cancelled.</summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Processes every currently queued/interrupted job to completion on the calling thread, without
    /// the background worker. Used by tests and the CLI.
    /// </summary>
    Task<int> DrainAsync(CancellationToken cancellationToken = default);
}
