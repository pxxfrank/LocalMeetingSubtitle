using System.Globalization;
using System.Text;
using LocalMeetingSubtitle.Core.Models;

namespace LocalMeetingSubtitle.Export;

/// <summary>
/// SubRip (.srt) transcript. Start/end come straight from the segment offsets; a segment whose
/// end is not after its start is given a one-second minimum duration. No word-level timings are
/// ever fabricated.
/// </summary>
public sealed class SrtTranscriptFormatter : TranscriptFormatterBase
{
    private static readonly TimeSpan MinimumDuration = TimeSpan.FromSeconds(1);

    public override ExportFormat Format => ExportFormat.Srt;

    public override string FormatTranscript(MeetingSession session, IReadOnlyList<SubtitleSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(segments);

        var builder = new StringBuilder();
        var index = 1;
        foreach (var segment in Ordered(segments))
        {
            var start = segment.StartOffset < TimeSpan.Zero ? TimeSpan.Zero : segment.StartOffset;
            var end = segment.EndOffset;
            if (end <= start)
            {
                end = start + MinimumDuration;
            }

            builder.Append(index.ToString(CultureInfo.InvariantCulture)).Append('\n');
            builder.Append(FormatTimestamp(start)).Append(" --> ").Append(FormatTimestamp(end)).Append('\n');
            builder.Append(string.IsNullOrEmpty(segment.SpeakerName)
                ? segment.DisplayText
                : segment.SpeakerName + ": " + segment.DisplayText).Append('\n');
            builder.Append('\n');
            index++;
        }

        return builder.ToString();
    }

    /// <summary>Formats an offset as <c>HH:MM:SS,mmm</c> (hours are not wrapped at 24).</summary>
    private static string FormatTimestamp(TimeSpan offset)
    {
        if (offset < TimeSpan.Zero)
        {
            offset = TimeSpan.Zero;
        }

        return $"{(int)offset.TotalHours:00}:{offset.Minutes:00}:{offset.Seconds:00},{offset.Milliseconds:000}";
    }
}
