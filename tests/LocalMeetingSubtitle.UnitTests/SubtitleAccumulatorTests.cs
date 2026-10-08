using LocalMeetingSubtitle.Core.Models;
using LocalMeetingSubtitle.Core.Transcription;

namespace LocalMeetingSubtitle.UnitTests;

public sealed class SubtitleAccumulatorTests
{
    private static readonly TimeSpan T0 = TimeSpan.Zero;

    [Fact]
    public void PartialUpdates_DoNotAdvanceSequence()
    {
        var acc = new SubtitleAccumulator();

        var first = acc.Update("hello", isEndpoint: false, T0);
        var second = acc.Update("hello world", isEndpoint: false, T0 + TimeSpan.FromSeconds(1));

        Assert.Equal(TranscriptEventKind.Partial, first.Kind);
        Assert.Equal(0, first.SequenceNumber);
        Assert.Equal(TranscriptEventKind.Partial, second.Kind);
        Assert.Equal("hello world", second.Text);
        Assert.Equal(0, acc.Sequence);
        Assert.Equal("hello world", acc.CurrentPartial);
    }

    [Fact]
    public void Endpoint_CommitsExactlyOneFinal()
    {
        var acc = new SubtitleAccumulator();
        acc.Update("hello", isEndpoint: false, T0);

        var final = acc.Update("hello world", isEndpoint: true, T0 + TimeSpan.FromSeconds(1));

        Assert.Equal(TranscriptEventKind.Final, final.Kind);
        Assert.Equal(1, final.SequenceNumber);
        Assert.Equal("hello world", final.Text);
        Assert.Equal(1, acc.Sequence);
        Assert.Equal("", acc.CurrentPartial);

        // A second endpoint with no new text and no open partial commits nothing.
        var none = acc.Update("", isEndpoint: true, T0 + TimeSpan.FromSeconds(2));
        Assert.Equal(TranscriptEventKind.None, none.Kind);
        Assert.Equal(1, acc.Sequence);
    }

    [Fact]
    public void Endpoint_WithEmptyText_UsesLastPartial()
    {
        var acc = new SubtitleAccumulator();
        acc.Update("draft text", isEndpoint: false, T0);

        var final = acc.Update("", isEndpoint: true, T0 + TimeSpan.FromSeconds(1));

        Assert.Equal(TranscriptEventKind.Final, final.Kind);
        Assert.Equal("draft text", final.Text);
        Assert.Equal(1, final.SequenceNumber);
    }

    [Fact]
    public void ConsecutiveIdenticalFinals_AreSuppressed()
    {
        var acc = new SubtitleAccumulator();

        var first = acc.Update("same words", isEndpoint: true, T0);
        var second = acc.Update("same words", isEndpoint: true, T0 + TimeSpan.FromSeconds(1));

        Assert.Equal(TranscriptEventKind.Final, first.Kind);
        Assert.Equal(1, first.SequenceNumber);
        Assert.Equal(TranscriptEventKind.None, second.Kind);
        Assert.Equal(1, acc.Sequence);
    }

    [Fact]
    public void IdenticalFinals_DifferingOnlyByCaseOrPunctuation_AreSuppressed()
    {
        var acc = new SubtitleAccumulator();

        acc.Update("Hello, world!", isEndpoint: true, T0);
        var second = acc.Update("hello world", isEndpoint: true, T0 + TimeSpan.FromSeconds(1));

        Assert.Equal(TranscriptEventKind.None, second.Kind);
        Assert.Equal(1, acc.Sequence);
    }

    [Fact]
    public void GenuineRepeat_AfterMoreThanTwoSeconds_IsAllowed()
    {
        var acc = new SubtitleAccumulator();

        var first = acc.Update("same words", isEndpoint: true, T0);
        var second = acc.Update("same words", isEndpoint: true, T0 + TimeSpan.FromSeconds(3));

        Assert.Equal(1, first.SequenceNumber);
        Assert.Equal(TranscriptEventKind.Final, second.Kind);
        Assert.Equal(2, second.SequenceNumber);
        Assert.Equal(2, acc.Sequence);
    }

    [Fact]
    public void Flush_CommitsOpenPartialOnce_ThenIsIdempotent()
    {
        var acc = new SubtitleAccumulator();
        acc.Update("unfinished thought", isEndpoint: false, T0);

        var flushed = acc.Flush(T0 + TimeSpan.FromSeconds(1));

        Assert.Equal(TranscriptEventKind.Final, flushed.Kind);
        Assert.Equal(1, flushed.SequenceNumber);
        Assert.Equal("unfinished thought", flushed.Text);

        var again = acc.Flush(T0 + TimeSpan.FromSeconds(2));
        Assert.Equal(TranscriptEventKind.None, again.Kind);
        Assert.Equal(1, acc.Sequence);
    }

    [Fact]
    public void Flush_SuppressesDuplicateOfRecentFinal()
    {
        var acc = new SubtitleAccumulator();
        acc.Update("repeat me", isEndpoint: true, T0);
        acc.Update("repeat me", isEndpoint: false, T0 + TimeSpan.FromSeconds(1));

        var flushed = acc.Flush(T0 + TimeSpan.FromSeconds(1.5));

        Assert.Equal(TranscriptEventKind.None, flushed.Kind);
        Assert.Equal(1, acc.Sequence);
    }

    [Theory]
    [InlineData("  hello   world  ", "hello world")]
    [InlineData("a\t\tb\n c", "a b c")]
    [InlineData("line1\r\nline2", "line1 line2")]
    public void Whitespace_IsNormalized(string input, string expected)
    {
        var acc = new SubtitleAccumulator();

        var evt = acc.Update(input, isEndpoint: false, T0);

        Assert.Equal(expected, evt.Text);
    }

    [Fact]
    public void CorrectionDelegate_IsAppliedToEmittedText()
    {
        var acc = new SubtitleAccumulator(s => s.Replace("MCP", "MCP-Corrected"));

        var evt = acc.Update("MCP rocks", isEndpoint: false, T0);

        Assert.Equal("MCP rocks", evt.Text);
        Assert.Equal("MCP-Corrected rocks", evt.CorrectedText);
    }

    [Fact]
    public void Reset_ClearsSequenceAndPartial()
    {
        var acc = new SubtitleAccumulator();
        acc.Update("x", isEndpoint: true, T0);
        acc.Update("y", isEndpoint: false, T0 + TimeSpan.FromSeconds(1));

        acc.Reset();

        Assert.Equal(0, acc.Sequence);
        Assert.Equal("", acc.CurrentPartial);
    }
}
