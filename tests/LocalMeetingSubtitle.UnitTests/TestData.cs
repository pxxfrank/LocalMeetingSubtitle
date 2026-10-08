using LocalMeetingSubtitle.Core.Models;

namespace LocalMeetingSubtitle.UnitTests;

/// <summary>Convenience builders shared by the storage and export tests.</summary>
internal static class TestData
{
    public static readonly DateTimeOffset FixedStart = new(2026, 3, 4, 9, 5, 0, TimeSpan.Zero);

    public static MeetingSession NewSession(
        string? sessionId = null,
        string title = "Weekly Sync",
        SessionStatus status = SessionStatus.Recording) => new()
    {
        SessionId = sessionId ?? Guid.NewGuid().ToString("N"),
        Title = title,
        StartTime = FixedStart,
        Status = status
    };

    public static SubtitleSegment NewSegment(
        string sessionId,
        int sequence,
        string text,
        double startMs = 0,
        double endMs = 1000) => new()
    {
        SessionId = sessionId,
        SequenceNumber = sequence,
        StartOffset = TimeSpan.FromMilliseconds(startMs),
        EndOffset = TimeSpan.FromMilliseconds(endMs),
        OriginalText = text,
        CorrectedText = text,
        CreatedAt = FixedStart
    };

    /// <summary>An export-side segment that is not tied to the storage session id.</summary>
    public static SubtitleSegment ExportSegment(
        int sequence,
        string text,
        double startMs,
        double endMs) => new()
    {
        SessionId = "export-session",
        SequenceNumber = sequence,
        StartOffset = TimeSpan.FromMilliseconds(startMs),
        EndOffset = TimeSpan.FromMilliseconds(endMs),
        OriginalText = text,
        CorrectedText = text
    };

    public static MeetingSession ExportSession(string title = "Weekly Sync") => new()
    {
        SessionId = "export-session",
        Title = title,
        StartTime = FixedStart
    };
}
