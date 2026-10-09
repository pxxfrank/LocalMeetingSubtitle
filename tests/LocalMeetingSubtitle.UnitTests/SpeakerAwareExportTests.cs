using LocalMeetingSubtitle.Export;

namespace LocalMeetingSubtitle.UnitTests;

/// <summary>
/// Verifies the speaker-aware export path: when a segment carries a <c>SpeakerName</c> the CSV /
/// TXT / SRT / Markdown formatters render it, and a plain transcript is left untouched.
/// </summary>
public sealed class SpeakerAwareExportTests
{
    [Fact]
    public void Csv_HasExactHeader_AndQuotesCommaFields()
    {
        var session = TestData.ExportSession();
        var segment = TestData.ExportSegment(0, "hello, world", 0, 1000);
        segment.SpeakerName = "发言人 A";

        var csv = new CsvTranscriptFormatter().FormatTranscript(session, new[] { segment });

        Assert.StartsWith("index,start,end,speaker,text\n", csv);
        Assert.Contains("\"hello, world\"", csv);
        Assert.Contains("发言人 A", csv);
    }

    [Fact]
    public void Csv_LeavesSpeakerColumnEmpty_WhenNoSpeakerName()
    {
        var session = TestData.ExportSession();
        var segment = TestData.ExportSegment(0, "no speaker", 0, 1000);

        var csv = new CsvTranscriptFormatter().FormatTranscript(session, new[] { segment });

        Assert.Contains(",no speaker\n", csv);
    }

    [Fact]
    public void Txt_PrependsSpeakerName()
    {
        var session = TestData.ExportSession();
        var segment = TestData.ExportSegment(0, "你好", 0, 1000);
        segment.SpeakerName = "发言人 A";

        var text = new TxtTranscriptFormatter().FormatTranscript(session, new[] { segment });

        Assert.Contains("发言人 A: 你好", text);
    }

    [Fact]
    public void Srt_PrependsSpeakerName()
    {
        var session = TestData.ExportSession();
        var segment = TestData.ExportSegment(0, "hi", 0, 1000);
        segment.SpeakerName = "发言人 A";

        var srt = new SrtTranscriptFormatter().FormatTranscript(session, new[] { segment });

        Assert.Contains("发言人 A: hi", srt);
    }

    [Fact]
    public void Markdown_WithSpeakerName_AddsBySpeakerSection()
    {
        var session = TestData.ExportSession();
        var segment = TestData.ExportSegment(0, "hello", 0, 1000);
        segment.SpeakerName = "发言人 A";

        var markdown = new MarkdownTranscriptFormatter().FormatTranscript(session, new[] { segment });

        Assert.Contains("## 按发言人整理 / By speaker", markdown);
        Assert.Contains("### 发言人 A", markdown);
    }

    [Fact]
    public void Markdown_WithoutSpeakerName_IsUnchanged()
    {
        var session = TestData.ExportSession();
        var segments = new[] { TestData.ExportSegment(0, "hello", 0, 1000) };

        var markdown = new MarkdownTranscriptFormatter().FormatTranscript(session, segments);

        Assert.DoesNotContain("按发言人", markdown);
        Assert.DoesNotContain("参与发言人", markdown);
        Assert.Contains("- **00:00:00** hello", markdown);
    }
}
