using System.Text;
using LocalMeetingSubtitle.Core.Models;

namespace LocalMeetingSubtitle.Export;

/// <summary>
/// Plain-text transcript: a title/date header, a blank line, then one
/// <c>[HH:MM:SS] text</c> line per segment.
/// </summary>
public sealed class TxtTranscriptFormatter : TranscriptFormatterBase
{
    public override ExportFormat Format => ExportFormat.Txt;

    /// <summary>Plain text is written UTF-8 with a BOM (Windows Notepad friendly).</summary>
    public override bool WriteUtf8Bom => true;

    public override string FormatTranscript(MeetingSession session, IReadOnlyList<SubtitleSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(segments);

        var builder = new StringBuilder();
        builder.Append("Meeting: ").Append(session.Title).Append('\n');
        builder.Append("Date: ").Append(FormatDate(session.StartTime)).Append('\n');
        builder.Append('\n');

        foreach (var segment in Ordered(segments))
        {
            builder.Append('[').Append(FormatClock(segment.StartOffset)).Append("] ")
                .Append(segment.DisplayText).Append('\n');
        }

        return builder.ToString();
    }
}
