using LocalMeetingSubtitle.Core.Models;
using LocalMeetingSubtitle.Export;

namespace LocalMeetingSubtitle.UnitTests;

public sealed class TranscriptFormatterTests
{
    [Fact]
    public void Txt_IncludesHeaderAndTimestampedLines()
    {
        var session = TestData.ExportSession("Weekly Sync");
        var segments = new[]
        {
            TestData.ExportSegment(0, "hello", 0, 1000),
            TestData.ExportSegment(1, "world", 65_000, 66_000)
        };

        var text = new TxtTranscriptFormatter().FormatTranscript(session, segments);

        Assert.Contains("Meeting: Weekly Sync", text);
        Assert.Contains("Date: 2026-03-04 09:05:00", text);
        Assert.Contains("[00:00:00] hello", text);
        Assert.Contains("[00:01:05] world", text);
        Assert.True(text.IndexOf("[00:00:00] hello", StringComparison.Ordinal)
                    < text.IndexOf("[00:01:05] world", StringComparison.Ordinal));
    }

    [Fact]
    public void Txt_PreservesChineseText()
    {
        var session = TestData.ExportSession("产品评审");
        var segments = new[] { TestData.ExportSegment(0, "你好，世界！今天讨论字幕。", 0, 1200) };

        var text = new TxtTranscriptFormatter().FormatTranscript(session, segments);

        Assert.Contains("产品评审", text);
        Assert.Contains("你好，世界！今天讨论字幕。", text);
    }

    [Fact]
    public void Srt_FormatsValidTimestampLines()
    {
        var session = TestData.ExportSession();
        var segments = new[] { TestData.ExportSegment(0, "hi", 1500, 4000) };

        var srt = new SrtTranscriptFormatter().FormatTranscript(session, segments);

        Assert.StartsWith("1\n", srt);
        Assert.Contains("00:00:01,500 --> 00:00:04,000", srt);
        Assert.EndsWith("hi\n\n", srt);
    }

    [Fact]
    public void Srt_EndNotAfterStart_EnforcesOneSecondMinimum()
    {
        var session = TestData.ExportSession();
        var segments = new[]
        {
            TestData.ExportSegment(0, "before", 2000, 1000), // end before start
            TestData.ExportSegment(1, "equal", 5000, 5000)   // zero length
        };

        var srt = new SrtTranscriptFormatter().FormatTranscript(session, segments);

        Assert.Contains("00:00:02,000 --> 00:00:03,000", srt);
        Assert.Contains("00:00:05,000 --> 00:00:06,000", srt);
    }

    [Fact]
    public void Markdown_HasTitleAndBoldTimestampedBullets()
    {
        var session = TestData.ExportSession("Weekly Sync");
        var segments = new[] { TestData.ExportSegment(0, "hello", 0, 1000) };

        var markdown = new MarkdownTranscriptFormatter().FormatTranscript(session, segments);

        Assert.StartsWith("# Weekly Sync\n", markdown);
        Assert.Contains("- **00:00:00** hello", markdown);
    }

    [Fact]
    public void EmptySession_ProducesHeaderWithoutBody()
    {
        var session = TestData.ExportSession("Empty Meeting");
        var empty = Array.Empty<SubtitleSegment>();

        Assert.Contains("Meeting: Empty Meeting", new TxtTranscriptFormatter().FormatTranscript(session, empty));
        Assert.Equal(string.Empty, new SrtTranscriptFormatter().FormatTranscript(session, empty));
        Assert.StartsWith("# Empty Meeting", new MarkdownTranscriptFormatter().FormatTranscript(session, empty));
    }

    [Fact]
    public void Formatters_PreferCorrectedThenEditedThenOriginalText()
    {
        var session = TestData.ExportSession();

        var corrected = TestData.ExportSegment(0, "raw-a", 0, 1000);
        corrected.CorrectedText = "corrected-a";

        var untouched = TestData.ExportSegment(1, "only-original", 0, 1000);
        untouched.CorrectedText = string.Empty;

        var edited = TestData.ExportSegment(2, "raw-b", 0, 1000);
        edited.CorrectedText = "edited-b";
        edited.IsEdited = true;

        var text = new TxtTranscriptFormatter().FormatTranscript(session, new[] { corrected, untouched, edited });

        Assert.Contains("corrected-a", text);
        Assert.Contains("only-original", text);
        Assert.Contains("edited-b", text);
        Assert.DoesNotContain("raw-a", text);
        Assert.DoesNotContain("raw-b", text);
    }
}
