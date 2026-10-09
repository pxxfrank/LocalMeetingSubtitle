namespace LocalMeetingSubtitle.Core.Models;

/// <summary>A recording session (one meeting).</summary>
public sealed class MeetingSession
{
    public string SessionId { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "Untitled meeting";
    public DateTimeOffset StartTime { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? EndTime { get; set; }
    public SessionStatus Status { get; set; } = SessionStatus.Recording;
    public string ModelId { get; set; } = "";
    public string AudioDeviceId { get; set; } = "";
    public string AudioDeviceName { get; set; } = "";
}

/// <summary>A persisted subtitle line (final only; partials are never persisted).</summary>
public sealed class SubtitleSegment
{
    public long SegmentId { get; set; }
    public string SessionId { get; set; } = "";
    public int SequenceNumber { get; set; }
    public TimeSpan StartOffset { get; set; }
    public TimeSpan EndOffset { get; set; }
    public string OriginalText { get; set; } = "";
    public string CorrectedText { get; set; } = "";
    public bool IsEdited { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    /// <summary>
    /// Speaker display name for this line. Populated only for speaker-aware export (never read from or
    /// written to the database — the repositories use explicit column lists).
    /// </summary>
    public string? SpeakerName { get; set; }

    /// <summary>Text shown to the user: the corrected text, unless the user edited it.</summary>
    public string DisplayText => IsEdited ? CorrectedText : (string.IsNullOrEmpty(CorrectedText) ? OriginalText : CorrectedText);
}

public sealed class HotwordGroup
{
    public long GroupId { get; set; }
    public string Name { get; set; } = "Default";
    public bool Enabled { get; set; } = true;
    public int SortOrder { get; set; }
}

public sealed class Hotword
{
    public long Id { get; set; }
    public string Text { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public long? GroupId { get; set; }
    public float Score { get; set; } = 1.5f;
}

/// <summary>A deterministic, bounded text correction rule (not a global blind replace).</summary>
public sealed class TextCorrectionRule
{
    public long Id { get; set; }
    /// <summary>Text (or regex when <see cref="IsRegex"/>) to look for.</summary>
    public string Pattern { get; set; } = "";
    public string Replacement { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public bool IsRegex { get; set; }
    /// <summary>Higher runs first. Equal priority keeps insertion order.</summary>
    public int Priority { get; set; }
    /// <summary>Only correct when the whole normalized token equals pattern (safer default).</summary>
    public bool WholeTokenOnly { get; set; } = true;
    public bool CaseSensitive { get; set; }

    public static TextCorrectionRule Create(string pattern, string replacement, bool wholeTokenOnly = true) =>
        new() { Pattern = pattern, Replacement = replacement, WholeTokenOnly = wholeTokenOnly };
}

public sealed class AppSettings
{
    public string? LastAudioDeviceId { get; set; }
    public string? LastModelId { get; set; }
    public bool AutoScrollEnabled { get; set; } = true;
    public bool FloatingSubtitleVisible { get; set; }
    public double FloatingFontSize { get; set; } = 28;
    public double FloatingBackgroundOpacity { get; set; } = 0.75;
    public bool FloatingClickThrough { get; set; }
    public bool MinimizeToTrayOnClose { get; set; } = true;
    public string? SubtitleFontFamily { get; set; } = "Microsoft YaHei UI";
    public double SubtitleFontSize { get; set; } = 16;
    public int AsrNumThreads { get; set; }
    public bool ExportIncludeTimestamps { get; set; } = true;
    public bool EnableVadSegmenting { get; set; } = true;
    public string Theme { get; set; } = "Light";

    /// <summary>Expected number of speakers for post-meeting diarization; 0 means automatic.</summary>
    public int DiarizationSpeakerCount { get; set; }

    /// <summary>Diarization clustering threshold (higher merges more aggressively → fewer speakers).</summary>
    public double DiarizationClusteringThreshold { get; set; } = 0.5;

    /// <summary>
    /// Opt-in post-meeting recording. Defaults to <see cref="RecordingMode.None"/>: no audio is
    /// recorded and nothing is ever persisted unless the user explicitly chooses otherwise in settings.
    /// </summary>
    public RecordingMode RecordingMode { get; set; } = RecordingMode.None;

    /// <summary>Merge the built-in Huawei-domain lexicon into the hotwords/correction rules (default on).</summary>
    public bool UseBuiltInLexicon { get; set; } = true;
}

public sealed class PerformanceMetric
{
    public long Id { get; set; }
    public string SessionId { get; set; } = "";
    public DateTimeOffset Timestamp { get; set; }
    public double CpuPercent { get; set; }
    public double WorkingSetMb { get; set; }
    public double Rtf { get; set; }
    public int QueueLength { get; set; }
    public int MaxQueueLength { get; set; }
    public double DroppedAudioSeconds { get; set; }
    public double EndToEndLatencyMs { get; set; }
}
