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

        var ordered = Ordered(segments).ToList();
        var hasSpeakers = ordered.Any(s => !string.IsNullOrEmpty(s.SpeakerName));

        var builder = new StringBuilder();
        builder.Append("# ").Append(session.Title).Append('\n');
        builder.Append('\n');
        builder.Append('_').Append(FormatDate(session.StartTime)).Append('_').Append('\n');
        builder.Append('\n');

        // Speaker sections appear only when diarization has run, so a plain transcript is unchanged.
        if (hasSpeakers)
        {
            builder.Append("## 参与发言人 / Speakers").Append('\n');
            builder.Append('\n');
            foreach (var name in ordered
                         .Where(s => !string.IsNullOrEmpty(s.SpeakerName))
                         .Select(s => s.SpeakerName!)
                         .Distinct())
            {
                builder.Append("- ").Append(name).Append('\n');
            }

            builder.Append('\n');
            builder.Append("## 完整会议字幕 / Full transcript").Append('\n');
            builder.Append('\n');
        }

        foreach (var segment in ordered)
        {
            builder.Append("- **").Append(FormatClock(segment.StartOffset)).Append("** ");
            if (!string.IsNullOrEmpty(segment.SpeakerName))
            {
                builder.Append("**").Append(segment.SpeakerName).Append("** ");
            }

            builder.Append(segment.DisplayText).Append('\n');
        }

        if (hasSpeakers)
        {
            builder.Append('\n');
            builder.Append("## 按发言人整理 / By speaker").Append('\n');
            foreach (var group in ordered
                         .Where(s => !string.IsNullOrEmpty(s.SpeakerName))
                         .GroupBy(s => s.SpeakerName!))
            {
                builder.Append('\n');
                builder.Append("### ").Append(group.Key).Append('\n');
                builder.Append('\n');
                foreach (var segment in group)
                {
                    builder.Append(FormatClock(segment.StartOffset)).Append(' ')
                        .Append(segment.DisplayText).Append('\n');
                }
            }
        }

        return builder.ToString();
    }
}
