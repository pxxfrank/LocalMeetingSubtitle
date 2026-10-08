using System.Text;
using LocalMeetingSubtitle.Core.Models;

namespace LocalMeetingSubtitle.Export;

/// <summary>
/// Markdown transcript: an <c># title</c> heading followed by bullet lines with a bold timestamp
/// and the plain segment text.
/// </summary>
public sealed class MarkdownTranscriptFormatter : TranscriptFormatterBase
{
    public override ExportFormat Format => ExportFormat.Markdown;

    /// <summary>Markdown is written UTF-8 without a BOM.</summary>
    public override bool WriteUtf8Bom => false;

    public override string FormatTranscript(MeetingSession session, IReadOnlyList<SubtitleSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(segments);

        var builder = new StringBuilder();
        builder.Append("# ").Append(session.Title).Append('\n');
        builder.Append('\n');
        builder.Append('_').Append(FormatDate(session.StartTime)).Append('_').Append('\n');
        builder.Append('\n');

        foreach (var segment in Ordered(segments))
        {
            builder.Append("- **").Append(FormatClock(segment.StartOffset)).Append("** ")
                .Append(segment.DisplayText).Append('\n');
        }

        return builder.ToString();
    }
}
