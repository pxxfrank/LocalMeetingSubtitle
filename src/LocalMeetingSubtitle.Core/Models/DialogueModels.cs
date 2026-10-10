namespace LocalMeetingSubtitle.Core.Models;

/// <summary>
/// One persisted transcript segment as the dialogue assembler sees it: the database id plus the
/// segment's own position and text. Mirrors <see cref="SubtitleSegment"/> without the persistence
/// concerns, so the assembler stays pure.
/// </summary>
public readonly record struct TranscriptSegmentFact(long SegmentId, TimeSpan Start, TimeSpan End, string Text);

/// <summary>One speaker turn of the dialogue: consecutive segments by the same speaker, merged.</summary>
public sealed class DialogueTurn
{
    /// <summary>Null when the speaker could not be determined.</summary>
    public string? SpeakerId { get; init; }
    public string SpeakerName { get; init; } = "";
    public int SpeakerColorArgb { get; init; }
    public TimeSpan Start { get; init; }
    public TimeSpan End { get; init; }
    public string Text { get; init; } = "";
    /// <summary>Database segment ids this turn was merged from (so an edit can be written back).</summary>
    public IReadOnlyList<long> SegmentIds { get; init; } = Array.Empty<long>();
    /// <summary>True when at least one constituent segment's speaker assignment is low-confidence.</summary>
    public bool NeedsConfirmation { get; init; }

    public TimeSpan Duration => End - Start;
    public bool IsUnknownSpeaker => SpeakerId is null;
}

/// <summary>Aggregate per-speaker statistics for a dialogue.</summary>
public sealed record DialogueParticipant(
    string SpeakerId,
    string Name,
    int ColorArgb,
    TimeSpan SpeakingTime,
    int TurnCount,
    int SegmentCount);

/// <summary>The role-tagged dialogue produced from a transcript plus its speaker assignments.</summary>
public sealed class DialogueTranscript
{
    public string SessionId { get; init; } = "";
    public IReadOnlyList<DialogueTurn> Turns { get; init; } = Array.Empty<DialogueTurn>();
    /// <summary>Known speakers only; unknown-speaker turns are excluded.</summary>
    public IReadOnlyList<DialogueParticipant> Participants { get; init; } = Array.Empty<DialogueParticipant>();
}

/// <summary>How consecutive same-speaker segments are merged into turns.</summary>
public sealed record DialogueAssemblyOptions
{
    /// <summary>Same-speaker segments separated by more than this start a new turn.</summary>
    public TimeSpan MaxGap { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>A turn is closed once its text would exceed this many characters.</summary>
    public int MaxTurnChars { get; init; } = 500;

    public string UnknownSpeakerLabel { get; init; } = "未知发言人";
    public int UnknownSpeakerColorArgb { get; init; } = unchecked((int)0xFF808080);
}
