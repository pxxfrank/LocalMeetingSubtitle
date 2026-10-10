using LocalMeetingSubtitle.Core.Transcription;

namespace LocalMeetingSubtitle.UnitTests;

/// <summary>
/// V0.5 Phase 2: adjacent segments that share audio (a max-length cut) make the recognizer emit the
/// shared words twice. Only the later segment is trimmed, and never to the point of losing speech.
/// </summary>
public sealed class OverlapTextDeduplicatorTests
{
    [Fact]
    public void NoPreviousText_LeavesTheTextAlone()
    {
        var result = OverlapTextDeduplicator.Apply(null, "今天讨论预算", 3);
        Assert.Equal("今天讨论预算", result.Text);
        Assert.Equal(0, result.TrimmedChars);
        Assert.False(result.Dropped);
    }

    [Fact]
    public void SharedRunAtOrAboveThreshold_IsTrimmed()
    {
        // "我们开始今天讨论" / "今天讨论预算" share the folded run "今天讨论" (4 chars).
        var result = OverlapTextDeduplicator.Apply("我们开始今天讨论", "今天讨论预算", 3);
        Assert.Equal("预算", result.Text);
        Assert.Equal(4, result.TrimmedChars);
        Assert.False(result.Dropped);
    }

    [Fact]
    public void SharedRunBelowThreshold_IsLeftAlone()
    {
        // Only "你好" (2 chars) is shared, below the 3-char guard: a coincidence, not an overlap.
        var result = OverlapTextDeduplicator.Apply("你好", "你好世界", 3);
        Assert.Equal("你好世界", result.Text);
        Assert.Equal(0, result.TrimmedChars);
    }

    [Fact]
    public void PunctuationDoesNotHideTheSharedRun()
    {
        // Folded: prev "我们讨论了预算问题", cur "预算、问题需要解决" -> shared "预算问题" (4).
        var result = OverlapTextDeduplicator.Apply("我们讨论了预算问题", "预算、问题需要解决", 3);
        Assert.Equal("需要解决", result.Text);
        Assert.Equal(4, result.TrimmedChars);
    }

    [Fact]
    public void WholeSegmentRepeat_IsDropped()
    {
        var result = OverlapTextDeduplicator.Apply("今天讨论预算", "今天讨论预算", 3);
        Assert.True(result.Dropped);
        Assert.Equal("", result.Text);
    }

    [Fact]
    public void WholeSegmentRepeat_IsDroppedAcrossPunctuation()
    {
        var result = OverlapTextDeduplicator.Apply("今天讨论预算。", "今天讨论预算", 3);
        Assert.True(result.Dropped);
    }

    [Fact]
    public void UnrelatedText_IsLeftAlone()
    {
        var result = OverlapTextDeduplicator.Apply("完全不同的内容", "另一个话题", 3);
        Assert.Equal("另一个话题", result.Text);
        Assert.False(result.Dropped);
    }

    [Fact]
    public void Trim_KeepsTheOriginalCharactersOfTheTail()
    {
        var result = OverlapTextDeduplicator.Apply("我们今天讨论预算", "讨论预算Remaining", 2);
        Assert.Equal("Remaining", result.Text);
    }
}
