namespace LocalMeetingSubtitle.Core.Models;

/// <summary>How the number of speakers is decided for a diarization run.</summary>
public enum SpeakerCountMode
{
    /// <summary>The engine decides (cluster threshold only).</summary>
    Auto = 0,
    /// <summary>The user states how many speakers there are.</summary>
    Manual = 1
}

public enum DiarizationRunStatus
{
    Running = 0,
    Succeeded = 1,
    Failed = 2,
    Cancelled = 3
}

/// <summary>How a subtitle's speaker was determined.</summary>
public enum SpeakerAssignmentSource
{
    /// <summary>Produced by the alignment step; may be overwritten by a later run.</summary>
    Auto = 0,
    /// <summary>Set by the user; never overwritten by re-analysis.</summary>
    Manual = 1
}

public enum AudioAssetKind
{
    /// <summary>A file the user imported.</summary>
    Imported = 0,
    /// <summary>A recording kept only long enough to run diarization once.</summary>
    TempRecording = 1,
    /// <summary>A recording the user asked to keep for later re-analysis.</summary>
    RetainedRecording = 2
}

/// <summary>Whether (and for how long) the app records the captured audio for post-meeting use.</summary>
public enum RecordingMode
{
    /// <summary>No recording; no audio is ever persisted. The default.</summary>
    None = 0,
    /// <summary>Record locally and delete automatically after one diarization run (or on next start).</summary>
    Temporary = 1,
    /// <summary>Record locally and keep the file for later re-analysis.</summary>
    Retain = 2
}

/// <summary>
/// An anonymous speaker within one session: "A", "B", ... Voiceprint clustering cannot
/// identify a person, so a speaker is only ever an anonymous label the user may rename.
/// </summary>
public sealed class Speaker
{
    public string SpeakerId { get; set; } = Guid.NewGuid().ToString("N");
    public string SessionId { get; set; } = "";
    /// <summary>Stable anonymous label assigned on creation ("A", "B", ...).</summary>
    public string Label { get; set; } = "";
    /// <summary>User-chosen name; empty while the speaker is still anonymous.</summary>
    public string DisplayName { get; set; } = "";
    public int ColorArgb { get; set; }
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    /// <summary>True when this speaker was merged into <see cref="MergedIntoSpeakerId"/>.</summary>
    public bool IsMerged { get; set; }
    public string? MergedIntoSpeakerId { get; set; }

    /// <summary>The name to show: the user's name if set, otherwise the anonymous label.</summary>
    public string EffectiveName => string.IsNullOrWhiteSpace(DisplayName) ? Label : DisplayName;
}

/// <summary>One raw diarization interval: a (possibly anonymous) speaker active from Start to End.</summary>
public sealed class SpeakerInterval
{
    public long SpeakerSegmentId { get; set; }
    public string RunId { get; set; } = "";
    public string SessionId { get; set; } = "";
    public TimeSpan Start { get; set; }
    public TimeSpan End { get; set; }
    /// <summary>The engine's cluster index (not stable across runs).</summary>
    public int RawSpeakerIndex { get; set; }
    /// <summary>Model-provided confidence, when the model produces one; never fabricated.</summary>
    public float? Confidence { get; set; }
}

/// <summary>The speaker assigned to one subtitle segment. One row per segment (the active assignment).</summary>
public sealed class SpeakerAssignment
{
    public long AssignmentId { get; set; }
    public string SessionId { get; set; } = "";
    public long SegmentId { get; set; }
    public string? RunId { get; set; }
    /// <summary>The assigned speaker; <c>null</c> means "unknown speaker".</summary>
    public string? SpeakerId { get; set; }
    public SpeakerAssignmentSource Source { get; set; }
    /// <summary>Overlap-weighted model confidence; <c>null</c> when the model produced none.</summary>
    public float? Confidence { get; set; }
    /// <summary>True when the alignment was ambiguous and a human should confirm it.</summary>
    public bool NeedsConfirmation { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
}

/// <summary>One execution of the diarization pipeline over a session (enables re-analysis + history).</summary>
public sealed class DiarizationRun
{
    public string RunId { get; set; } = Guid.NewGuid().ToString("N");
    public string SessionId { get; set; } = "";
    public string? AudioAssetId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DiarizationRunStatus Status { get; set; } = DiarizationRunStatus.Running;
    /// <summary>The number of speakers the user requested; 0 means automatic.</summary>
    public int RequestedSpeakerCount { get; set; }
    public int ResolvedSpeakerCount { get; set; }
    public double ClusteringThreshold { get; set; }
    public double MinDurationOn { get; set; }
    public double MinDurationOff { get; set; }
    public string SegmentationModelId { get; set; } = "";
    public string EmbeddingModelId { get; set; } = "";
    /// <summary>Duration of the analyzed audio.</summary>
    public long DurationMs { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>A local audio file used for (or produced during) analysis. Never leaves the machine.</summary>
public sealed class AudioAsset
{
    public string AudioAssetId { get; set; } = Guid.NewGuid().ToString("N");
    public string? SessionId { get; set; }
    public string Path { get; set; } = "";
    public AudioAssetKind Kind { get; set; }
    public int SampleRate { get; set; }
    public int Channels { get; set; }
    public long DurationMs { get; set; }
    public long SizeBytes { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    /// <summary>When set, a temporary asset may be deleted after this instant.</summary>
    public DateTimeOffset? DeleteAfterUtc { get; set; }
    public bool IsTemporary { get; set; }
}

/// <summary>Configures the low-level diarization engine.</summary>
public sealed class DiarizationEngineOptions
{
    public string SegmentationModelPath { get; set; } = "";
    public string EmbeddingModelPath { get; set; } = "";
    public int NumThreads { get; set; }
    public string Provider { get; set; } = "cpu";
    /// <summary>Expected speaker count; 0 lets the engine decide.</summary>
    public int NumClusters { get; set; }
    public float ClusteringThreshold { get; set; } = 0.5f;
    public float MinDurationOn { get; set; } = 0.3f;
    public float MinDurationOff { get; set; } = 0.5f;
    public bool ComputeConfidence { get; set; } = true;
}

public sealed record DiarizationInitResult(bool Ok, string? Message = null);

public readonly record struct DiarizationProgress(int ProcessedChunks, int TotalChunks)
{
    public double Fraction => TotalChunks <= 0 ? 0d : Math.Clamp((double)ProcessedChunks / TotalChunks, 0d, 1d);
}

/// <summary>An engine-produced speaker interval on the analyzed audio timeline (raw, unlabeled).</summary>
public readonly record struct DiarizationInterval(TimeSpan Start, TimeSpan End, int RawSpeakerIndex, float? Confidence);

public sealed record DiarizationRequest(
    string SessionId,
    string AudioFilePath,
    SpeakerCountMode CountMode = SpeakerCountMode.Auto,
    int ManualSpeakerCount = 0,
    double ClusteringThreshold = 0.5,
    string? AudioAssetId = null);

public sealed record DiarizationResult(
    string RunId,
    bool Success,
    int SpeakerCount,
    int AssignedSegments,
    int NeedsConfirmation,
    string? Error);

/// <summary>Thresholds for mapping diarization intervals onto subtitle segments.</summary>
public sealed record AlignmentOptions(
    /// <summary>Minimum share of a segment's duration the top speaker must own to be assigned outright.</summary>
    double MinOverlapFraction = 0.5,
    /// <summary>How far the top speaker must beat the runner-up (as a share of duration) to avoid ambiguity.</summary>
    double ConfirmMargin = 0.15,
    /// <summary>Overlaps shorter than this are ignored.</summary>
    double MinOverlapMs = 200);

/// <summary>The alignment decision for one subtitle segment. Never mutates the segment.</summary>
public sealed record AlignmentOutcome(
    long SegmentId,
    string? SpeakerId,
    int RawSpeakerIndex,
    float? Confidence,
    double OverlapFraction,
    bool NeedsConfirmation);
