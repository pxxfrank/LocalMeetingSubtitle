namespace LocalMeetingSubtitle.Core.Models;

public enum TranscriptEventKind
{
    None,
    /// <summary>In-progress text for the current utterance; may be revised.</summary>
    Partial,
    /// <summary>Committed text; persisted and exported.</summary>
    Final,
    /// <summary>Current utterance was cleared (e.g. pause/stop) with no committed text.</summary>
    Cleared
}

/// <summary>An update produced by the transcription accumulator.</summary>
public sealed record TranscriptEvent(
    TranscriptEventKind Kind,
    string Text,
    string CorrectedText,
    int SequenceNumber,
    TimeSpan StartOffset,
    TimeSpan EndOffset)
{
    public static readonly TranscriptEvent None = new(TranscriptEventKind.None, "", "", 0, TimeSpan.Zero, TimeSpan.Zero);
}

/// <summary>Runtime status surfaced to the UI.</summary>
public sealed record TranscriptionStatus(
    TranscriptionState State,
    string Message,
    HotwordMode HotwordMode = HotwordMode.TextCorrectionOnly,
    string? HotwordDetail = null)
{
    public static readonly TranscriptionStatus Idle = new(TranscriptionState.Idle, "Idle");
}
