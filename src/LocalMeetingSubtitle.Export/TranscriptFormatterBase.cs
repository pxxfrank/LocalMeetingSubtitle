using System.Globalization;
using LocalMeetingSubtitle.Core.Abstractions;
using LocalMeetingSubtitle.Core.Models;

namespace LocalMeetingSubtitle.Export;

/// <summary>
/// Shared plumbing for the built-in transcript formatters. The formatters return text only;
/// <see cref="WriteUtf8Bom"/> tells <see cref="SubtitleExportService"/> how to encode the file.
/// </summary>
public abstract class TranscriptFormatterBase : ITranscriptFormatter
{
    public abstract ExportFormat Format { get; }

    /// <summary>Whether the exported file should carry a UTF-8 byte-order mark.</summary>
    public virtual bool WriteUtf8Bom => false;

    public abstract string FormatTranscript(MeetingSession session, IReadOnlyList<SubtitleSegment> segments);

    /// <summary>Segments in playback order.</summary>
    protected static IEnumerable<SubtitleSegment> Ordered(IReadOnlyList<SubtitleSegment> segments) =>
        segments.OrderBy(s => s.SequenceNumber);

    /// <summary>Formats an offset as <c>[HH:MM:SS]</c> (hours are not wrapped at 24).</summary>
    protected static string FormatClock(TimeSpan offset)
    {
        if (offset < TimeSpan.Zero)
        {
            offset = TimeSpan.Zero;
        }

        return $"{(int)offset.TotalHours:00}:{offset.Minutes:00}:{offset.Seconds:00}";
    }

    protected static string FormatDate(DateTimeOffset value) =>
        value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
}
