using System.Globalization;
using System.Text;
using LocalMeetingSubtitle.Core.Models;

namespace LocalMeetingSubtitle.Export;

/// <summary>
/// CSV transcript (<c>index,start,end,speaker,text</c>). The speaker column is empty until
/// diarization has run. Fields containing a comma, quote or newline are quoted per RFC 4180.
/// </summary>
public sealed class CsvTranscriptFormatter : TranscriptFormatterBase
{
    public override ExportFormat Format => ExportFormat.Csv;

    /// <summary>A UTF-8 BOM makes Excel open Chinese text correctly.</summary>
    public override bool WriteUtf8Bom => true;

    public override string FormatTranscript(MeetingSession session, IReadOnlyList<SubtitleSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(segments);

        var builder = new StringBuilder();
        builder.Append("index,start,end,speaker,text\n");

        var index = 1;
        foreach (var segment in Ordered(segments))
        {
            builder.Append(index.ToString(CultureInfo.InvariantCulture)).Append(',');
            builder.Append(FormatClock(segment.StartOffset)).Append(',');
            builder.Append(FormatClock(segment.EndOffset)).Append(',');
            builder.Append(Escape(segment.SpeakerName ?? string.Empty)).Append(',');
            builder.Append(Escape(segment.DisplayText)).Append('\n');
            index++;
        }

        return builder.ToString();
    }

    private static string Escape(string value) =>
        value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;
}
