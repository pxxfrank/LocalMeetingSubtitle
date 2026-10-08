using System.Diagnostics;
using LocalMeetingSubtitle.Asr;
using LocalMeetingSubtitle.Core.Models;
using LocalMeetingSubtitle.Core.Transcription;
using Xunit.Abstractions;

namespace LocalMeetingSubtitle.PerformanceTests;

[Trait("Category", "Performance")]
public sealed class PipelineLongRunTests
{
    private readonly ITestOutputHelper _output;

    public PipelineLongRunTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Pipeline_TenMinuteRun_BoundedMonotonicAndExceptionFree()
    {
        const int totalSeconds = 600; // 10 minutes
        const double queueCapacitySeconds = 5.0;
        const int expectedSampleRate = 16000;

        var repository = new InMemorySubtitleRepository();
        var capture = new FakeAudioCaptureService();
        var performance = new RecordingPerformanceMonitor();
        var session = new MeetingSession
        {
            SessionId = Guid.NewGuid().ToString("N"),
            Title = "soak",
            AudioDeviceName = "fake"
        };

        // Scripted hypotheses last long enough for one final per audio second.
        var script = Enumerable.Range(0, totalSeconds + 100)
            .Select(i => new AsrDecodeResult($"line{i:D5}", true))
            .ToArray();
        var engine = new MockAsrEngine(script, streaming: false);

        var finals = new List<TranscriptEvent>();
        var errors = new List<string>();
        var gate = new object();

        var options = new PipelineOptions { QueueCapacitySeconds = queueCapacitySeconds };
        await using var pipeline = new TranscriptionPipeline(capture, new FakePreprocessor(expectedSampleRate),
            engine, new FakeHotwordService(), repository, performance, log: null, options: options);
        pipeline.TranscriptUpdated += (_, e) =>
        {
            if (e.Kind == TranscriptEventKind.Final)
            {
                lock (gate) { finals.Add(e); }
            }
        };
        pipeline.ErrorOccurred += (_, message) =>
        {
            lock (gate) { errors.Add(message); }
        };

        await pipeline.StartAsync("fake", session);

        var format = AudioFormat.Float32(expectedSampleRate, 1);
        var oneSecond = TestAudio.Concat(TestAudio.Speech(0.4), TestAudio.Silence(0.6));

        var stopwatch = Stopwatch.StartNew();
        for (int i = 0; i < totalSeconds; i++)
        {
            capture.RaiseFrames(oneSecond, format);
        }
        await pipeline.StopAsync();
        stopwatch.Stop();

        int finalCount = finals.Count;
        long queueCapacitySamples = (long)(queueCapacitySeconds * expectedSampleRate);

        _output.WriteLine($"audio_seconds={totalSeconds} wall_ms={stopwatch.Elapsed.TotalMilliseconds:F0}");
        _output.WriteLine($"finals={finalCount} persisted={repository.SegmentCount} errors={errors.Count}");
        _output.WriteLine($"max_queue_samples={performance.MaxReportedQueueSamples} capacity={queueCapacitySamples}");
        _output.WriteLine($"max_dropped_seconds={performance.MaxReportedDroppedSeconds:F3}");

        Assert.True(errors.Count == 0, "Pipeline reported errors: " + string.Join(" | ", errors));
        Assert.True(finalCount > 0, "No finals were produced.");
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(60),
            $"10-minute run took {stopwatch.Elapsed.TotalSeconds:F1}s (expected well under 60s).");

        // Bounded queue: the high-water mark never exceeds the configured capacity.
        Assert.True(performance.MaxReportedQueueSamples <= queueCapacitySamples,
            $"queue high-water mark {performance.MaxReportedQueueSamples} exceeded capacity {queueCapacitySamples}.");

        // No duplicate finals, monotonic 1-based sequence numbers.
        var sequenceNumbers = finals.Select(f => f.SequenceNumber).ToList();
        Assert.Equal(Enumerable.Range(1, finalCount), sequenceNumbers);
        Assert.Equal(finalCount, finals.Select(f => f.Text).Distinct().Count());

        // Everything that was emitted as a final was persisted.
        Assert.Equal(finalCount, repository.SegmentCount);
    }
}
