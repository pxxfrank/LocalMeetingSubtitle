using LocalMeetingSubtitle.Core.Abstractions;

namespace LocalMeetingSubtitle.Core.Models;

/// <summary>Lifecycle of a file-transcription job.</summary>
public enum TranscriptionJobStatus
{
    Queued = 0,
    Running = 1,
    /// <summary>Stopped by the user; resumable from the last committed segment.</summary>
    Paused = 2,
    Succeeded = 3,
    Failed = 4,
    Cancelled = 5,
    /// <summary>Left running when the process ended; the next start marks it this way so it can be resumed.</summary>
    Interrupted = 6
}

/// <summary>One imported media file (probe result), persisted so a job can be re-run without the UI.</summary>
public sealed class MediaFileRecord
{
    public string MediaFileId { get; set; } = Guid.NewGuid().ToString("N");
    public string Path { get; set; } = "";
    public string FileName { get; set; } = "";
    public MediaKind Kind { get; set; } = MediaKind.Unknown;
    public string ContainerFormat { get; set; } = "";
    public TimeSpan Duration { get; set; }
    public long SizeBytes { get; set; }
    public int? AudioStreamIndex { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
}

/// <summary>
/// One file-transcription job: everything needed to rebuild the run (mode, model, diarization
/// options) plus its progress and outcome.
///
/// There is deliberately <b>no</b> stored "processed offset": the resume cursor is derived from the
/// committed <c>segments</c> rows (max end / max sequence), so it can never drift ahead of the data
/// that actually exists. <see cref="Processed"/> here is only a display snapshot.
/// </summary>
public sealed class TranscriptionJob
{
    public string JobId { get; set; } = Guid.NewGuid().ToString("N");
    public string MediaFileId { get; set; } = "";
    /// <summary>Set once the first attempt has created the transcript session.</summary>
    public string? SessionId { get; set; }
    public string Title { get; set; } = "";
    public TranscriptionMode Mode { get; set; }
    public string ModelId { get; set; } = "";
    public int? AudioStreamIndex { get; set; }
    public bool RunDiarization { get; set; } = true;
    public SpeakerCountMode DiarizationCountMode { get; set; }
    public int ManualSpeakerCount { get; set; }
    public double ClusteringThreshold { get; set; } = 0.5;

    public TranscriptionJobStatus Status { get; set; } = TranscriptionJobStatus.Queued;
    public FileTranscriptionPhase Phase { get; set; }
    public TimeSpan Processed { get; set; }
    public TimeSpan Total { get; set; }
    public int SegmentsEmitted { get; set; }

    public DateTimeOffset QueuedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public int Attempts { get; set; }
    public int ResumeCount { get; set; }
    public string? Error { get; set; }
    public string? Warning { get; set; }

    public double Progress => Total > TimeSpan.Zero ? Math.Clamp(Processed / Total, 0.0, 1.0) : 0.0;

    public bool IsFinished =>
        Status is TranscriptionJobStatus.Succeeded or TranscriptionJobStatus.Failed or TranscriptionJobStatus.Cancelled;

    /// <summary>True when the job should be picked up by the worker.</summary>
    public bool IsRunnable => Status is TranscriptionJobStatus.Queued or TranscriptionJobStatus.Interrupted;
}

/// <summary>
/// Enqueue spec: the media plus everything a job needs except the recognizer (which the composition
/// root builds per attempt, so the job survives a restart). Mirrors the persisted job columns.
/// </summary>
public sealed record TranscriptionJobRequest(
    string InputPath,
    TranscriptionMode Mode,
    string ModelId,
    int? AudioStreamIndex = null,
    bool RunDiarization = true,
    string? Title = null,
    SpeakerCountMode DiarizationCountMode = SpeakerCountMode.Auto,
    int ManualSpeakerCount = 0,
    double ClusteringThreshold = 0.5);
