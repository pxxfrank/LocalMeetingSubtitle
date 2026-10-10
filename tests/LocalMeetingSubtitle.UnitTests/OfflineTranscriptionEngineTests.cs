using System.Runtime.CompilerServices;
using LocalMeetingSubtitle.Core.Abstractions;
using LocalMeetingSubtitle.Core.Models;
using LocalMeetingSubtitle.Core.Transcription;

namespace LocalMeetingSubtitle.UnitTests;

/// <summary>
/// V0.5 Phase 2: the file transcription engine must emit segments whose times are real positions on
/// the media timeline (never the zero-duration, chunk-end values the live offline path produces).
/// Driven by a scripted engine so no model is needed.
/// </summary>
public sealed class OfflineTranscriptionEngineTests
{
    private const int Rate = 16000;
    private const string ModelId = "test-model";

    private static readonly PcmBlock[] TwoUtterances =
    [
        Block(Silence(1.0), 0.0),
        Block(Speech(2.0), 1.0),
        Block(Silence(1.0), 3.0),
        Block(Speech(2.0), 4.0),
        Block(Silence(1.0), 6.0)
    ];

    [Fact]
    public async Task ProducesSegmentsWithGlobalTimestamps()
    {
        var engine = new ScriptedSegmentsAsrEngine("你好世界", "再见");
        var transcriber = new OfflineTranscriptionEngine(engine, Options());

        var result = await transcriber.TranscribeAsync(Blocks(TwoUtterances));

        Assert.True(result.Completed);
        Assert.False(result.Cancelled);
        Assert.Equal(2, result.Segments.Count);

        var first = result.Segments[0];
        var second = result.Segments[1];

        Assert.Equal("你好世界", first.Text);
        Assert.Equal("再见", second.Text);

        // Real, non-zero-duration, non-decreasing times derived from the block positions.
        Assert.InRange(first.Start.TotalSeconds, 0.98, 1.02);
        Assert.InRange(first.End.TotalSeconds, 2.98, 3.02);
        Assert.InRange(second.Start.TotalSeconds, 3.98, 4.02);
        Assert.InRange(second.End.TotalSeconds, 5.98, 6.02);
        Assert.True(first.End > first.Start, "every segment must have a positive duration.");
        Assert.True(second.Start >= first.End, "times must be non-decreasing.");
        Assert.True(second.End <= result.AudioDuration, "times must stay inside the audio.");

        Assert.Equal(ModelId, first.ModelId);
        Assert.Equal(1, first.SourceChunkId);
        Assert.Equal(2, second.SourceChunkId);
        Assert.InRange(result.AudioDuration.TotalSeconds, 6.99, 7.01);
    }

    [Fact]
    public async Task NonZeroFirstBlockStart_ShiftsEveryTimestamp()
    {
        var engine = new ScriptedSegmentsAsrEngine("你好世界");
        var transcriber = new OfflineTranscriptionEngine(engine, Options());

        var result = await transcriber.TranscribeAsync(Blocks(
        [
            Block(Silence(1.0), 10.0),
            Block(Speech(2.0), 11.0),
            Block(Silence(1.0), 13.0)
        ]));

        var segment = Assert.Single(result.Segments);
        Assert.InRange(segment.Start.TotalSeconds, 10.98, 11.02);
        Assert.InRange(segment.End.TotalSeconds, 12.98, 13.02);
    }

    [Fact]
    public async Task OverlapText_IsTrimmedAndTheTimelineStaysNonOverlapping()
    {
        var options = Options() with { MaxSegmentSeconds = 1.0, OverlapSeconds = 0.4 };
        var engine = new ScriptedSegmentsAsrEngine("我们先讨论预算问题", "预算问题需要尽快解决", "解决之后再谈其他");
        var transcriber = new OfflineTranscriptionEngine(engine, options);

        var result = await transcriber.TranscribeAsync(Blocks([Block(Speech(3.0), 0.0)]));

        Assert.Equal(3, result.Segments.Count);
        Assert.Equal("我们先讨论预算问题", result.Segments[0].Text);
        Assert.Equal("需要尽快解决", result.Segments[1].Text);
        Assert.Equal("解决之后再谈其他", result.Segments[2].Text);

        for (int i = 1; i < result.Segments.Count; i++)
        {
            Assert.True(result.Segments[i].Start >= result.Segments[i - 1].End - TimeSpan.FromMilliseconds(1),
                $"segment {i} must not start before segment {i - 1} ended.");
        }
    }

    [Fact]
    public async Task RepeatedOverlapText_IsDroppedCompletely()
    {
        var options = Options() with { MaxSegmentSeconds = 1.0, OverlapSeconds = 0.4 };
        var engine = new ScriptedSegmentsAsrEngine(
            "我们开始今天讨论", "我们开始今天讨论", "我们开始今天讨论", "我们开始今天讨论", "我们开始今天讨论");
        var transcriber = new OfflineTranscriptionEngine(engine, options);

        var result = await transcriber.TranscribeAsync(Blocks([Block(Speech(3.0), 0.0)]));

        var segment = Assert.Single(result.Segments);
        Assert.Equal("我们开始今天讨论", segment.Text);
    }

    [Fact]
    public async Task AppendsTerminalPunctuation_WhenRequested()
    {
        var options = Options() with { AppendTerminalPunctuation = true };
        var engine = new ScriptedSegmentsAsrEngine("你好世界", "已经结束。");
        var transcriber = new OfflineTranscriptionEngine(engine, options);

        var result = await transcriber.TranscribeAsync(Blocks(TwoUtterances));

        Assert.Equal("你好世界。", result.Segments[0].Text);
        Assert.Equal("已经结束。", result.Segments[1].Text);
    }

    [Fact]
    public async Task CorrectionFunction_IsApplied()
    {
        var engine = new ScriptedSegmentsAsrEngine("云山");
        var transcriber = new OfflineTranscriptionEngine(engine, Options(), text => text.Replace("云山", "云杉"));

        var result = await transcriber.TranscribeAsync(Blocks(
        [
            Block(Silence(1.0), 0.0),
            Block(Speech(2.0), 1.0),
            Block(Silence(1.0), 3.0)
        ]));

        Assert.Equal("云杉", Assert.Single(result.Segments).Text);
    }

    [Fact]
    public async Task ReportsProgress()
    {
        var engine = new ScriptedSegmentsAsrEngine("你好世界", "再见");
        var transcriber = new OfflineTranscriptionEngine(engine, Options());
        var reports = new List<OfflineTranscriptionProgress>();
        var progress = new SynchronousProgress<OfflineTranscriptionProgress>(reports.Add);

        await transcriber.TranscribeAsync(
            Blocks(TwoUtterances),
            progress,
            totalDuration: TimeSpan.FromSeconds(7));

        Assert.NotEmpty(reports);
        Assert.Equal(2, reports[^1].SegmentsEmitted);
        Assert.Equal(TimeSpan.FromSeconds(7), reports[^1].Total);
    }

    [Fact]
    public async Task Cancellation_StopsAndReportsCancelled()
    {
        var engine = new ScriptedSegmentsAsrEngine("你好世界");
        var transcriber = new OfflineTranscriptionEngine(engine, Options());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await transcriber.TranscribeAsync(
            Blocks(TwoUtterances),
            cancellationToken: cts.Token);

        Assert.True(result.Cancelled);
        Assert.False(result.Completed);
        Assert.Empty(result.Segments);
    }

    [Fact]
    public async Task UninitializedEngine_IsRejected()
    {
        var engine = new ScriptedSegmentsAsrEngine("x") { Initialized = false };
        var transcriber = new OfflineTranscriptionEngine(engine, Options());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            transcriber.TranscribeAsync(Blocks([Block(Speech(2.0), 0.0)])));
    }

    // ---- helpers -------------------------------------------------------------------------------

    private static OfflineTranscriptionOptions Options() => new() { ModelId = ModelId };

    private static PcmBlock Block(float[] samples, double startSeconds) =>
        new(samples, TimeSpan.FromSeconds(startSeconds), Rate);

    private static float[] Speech(double seconds)
    {
        int count = (int)(seconds * Rate);
        var data = new float[count];
        for (int i = 0; i < count; i++)
        {
            data[i] = (float)(0.3 * Math.Sin(2.0 * Math.PI * 440.0 * i / Rate));
        }

        return data;
    }

    private static float[] Silence(double seconds) => new float[(int)(seconds * Rate)];

    private static async IAsyncEnumerable<PcmBlock> Blocks(
        PcmBlock[] blocks, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var block in blocks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return block;
            await Task.Yield();
        }
    }
}
