using LocalMeetingSubtitle.Asr;
using LocalMeetingSubtitle.Core.Models;
using LocalMeetingSubtitle.Core.Transcription;

namespace LocalMeetingSubtitle.IntegrationTests;

public sealed class PipelineIntegrationTests
{
    private static AudioFormat Mono16k => AudioFormat.Float32(16000, 1);

    private static MeetingSession NewSession() => new()
    {
        SessionId = Guid.NewGuid().ToString("N"),
        Title = "integration",
        AudioDeviceName = "fake"
    };

    [Fact]
    public async Task Pipeline_PersistsFinalsInOrder_AndNeverPartials()
    {
        await using var temp = await TempSqlite.CreateAsync();
        var repository = temp.CreateSubtitleRepository();
        var session = NewSession();
        await repository.CreateSessionAsync(session);

        var capture = new FakeAudioCaptureService();
        var preprocessor = new FakePreprocessor(16000);
        var hotwords = new FakeHotwordService();

        var script = new[]
        {
            new AsrDecodeResult("今天", false),
            new AsrDecodeResult("今天天气", false),
            new AsrDecodeResult("今天天气不错", true), // final 1
            new AsrDecodeResult("欢迎", false),
            new AsrDecodeResult("欢迎参加", true),      // final 2
            new AsrDecodeResult("欢迎参加", true),      // duplicate -> suppressed
            new AsrDecodeResult("谢谢", true)           // final 3
        };
        var engine = new ScriptedStreamingEngine(script);

        var finals = new List<TranscriptEvent>();
        var partials = new List<TranscriptEvent>();
        var gate = new object();

        await using var pipeline = new TranscriptionPipeline(capture, preprocessor, engine, hotwords, repository);
        pipeline.TranscriptUpdated += (_, e) =>
        {
            lock (gate)
            {
                if (e.Kind == TranscriptEventKind.Final) finals.Add(e);
                else if (e.Kind == TranscriptEventKind.Partial) partials.Add(e);
            }
        };

        await pipeline.StartAsync("fake", session);
        foreach (var _ in script)
        {
            capture.RaiseFrames(new float[1600], Mono16k);
        }

        await pipeline.StopAsync();

        var persisted = await repository.GetSegmentsAsync(session.SessionId);

        Assert.Equal(new[] { "今天天气不错", "欢迎参加", "谢谢" }, persisted.Select(s => s.OriginalText));
        Assert.Equal(new[] { 1, 2, 3 }, persisted.Select(s => s.SequenceNumber));
        Assert.All(persisted, s => Assert.False(string.IsNullOrEmpty(s.OriginalText)));

        // Partials were surfaced to the UI but must never be persisted.
        Assert.Contains(partials, p => p.Text == "今天");
        Assert.DoesNotContain(persisted, s => s.OriginalText == "今天");
        Assert.DoesNotContain(persisted, s => s.OriginalText == "今天天气");

        Assert.Equal(3, finals.Count);
    }

    [Fact]
    public async Task Pipeline_WithMockAsrEngineOffline_PersistsEachScriptedSegmentInOrder()
    {
        await using var temp = await TempSqlite.CreateAsync();
        var repository = temp.CreateSubtitleRepository();
        var session = NewSession();
        await repository.CreateSessionAsync(session);

        var capture = new FakeAudioCaptureService();
        var preprocessor = new FakePreprocessor(16000);
        var hotwords = new FakeHotwordService();

        var script = new[]
        {
            new AsrDecodeResult("第一句", true),
            new AsrDecodeResult("第二句", true),
            new AsrDecodeResult("第三句", true)
        };
        var engine = new MockAsrEngine(script, streaming: false);

        await using var pipeline = new TranscriptionPipeline(capture, preprocessor, engine, hotwords, repository);
        await pipeline.StartAsync("fake", session);

        // Each chunk is 0.5 s speech + 0.7 s silence, which yields exactly one segment.
        for (int i = 0; i < 3; i++)
        {
            capture.RaiseFrames(TestAudio.Concat(TestAudio.Speech(0.5), TestAudio.Silence(0.7)), Mono16k);
        }

        await pipeline.StopAsync();

        var persisted = await repository.GetSegmentsAsync(session.SessionId);
        Assert.Equal(new[] { "第一句", "第二句", "第三句" }, persisted.Select(s => s.OriginalText));
        Assert.Equal(new[] { 1, 2, 3 }, persisted.Select(s => s.SequenceNumber));
    }

    [Fact]
    public async Task Pipeline_FlushesOpenPartial_OnStop()
    {
        await using var temp = await TempSqlite.CreateAsync();
        var repository = temp.CreateSubtitleRepository();
        var session = NewSession();
        await repository.CreateSessionAsync(session);

        var capture = new FakeAudioCaptureService();
        var preprocessor = new FakePreprocessor(16000);
        var hotwords = new FakeHotwordService();

        var script = new[] { new AsrDecodeResult("未完成的句子", false) }; // partial only, no endpoint
        var engine = new ScriptedStreamingEngine(script);

        var partialSeen = new TaskCompletionSource();
        await using var pipeline = new TranscriptionPipeline(capture, preprocessor, engine, hotwords, repository);
        pipeline.TranscriptUpdated += (_, e) =>
        {
            if (e.Kind == TranscriptEventKind.Partial) partialSeen.TrySetResult();
        };

        await pipeline.StartAsync("fake", session);
        capture.RaiseFrames(new float[1600], Mono16k);

        await partialSeen.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await pipeline.StopAsync();

        var persisted = await repository.GetSegmentsAsync(session.SessionId);
        var segment = Assert.Single(persisted);
        Assert.Equal("未完成的句子", segment.OriginalText);
        Assert.Equal(1, segment.SequenceNumber);
    }

    [Fact]
    public async Task Pipeline_Overloads_WhenConsumerIsSlowAndQueueIsSmall()
    {
        await using var temp = await TempSqlite.CreateAsync();
        var repository = temp.CreateSubtitleRepository();
        var session = NewSession();
        await repository.CreateSessionAsync(session);

        var capture = new FakeAudioCaptureService();
        var preprocessor = new FakePreprocessor(16000);
        var hotwords = new FakeHotwordService();
        var performance = new FakePerformanceMonitor();

        // The consumer takes ~50 ms per buffer; the queue only holds 0.5 s (8000 samples).
        var script = Enumerable.Range(0, 4096).Select(i => new AsrDecodeResult($"s{i}", false)).ToArray();
        var engine = new ScriptedStreamingEngine(script, decodeDelay: TimeSpan.FromMilliseconds(50));

        int overloadCount = 0;
        double maxDropped = 0;
        var options = new PipelineOptions { QueueCapacitySeconds = 0.5, IdlePollMs = 1 };
        await using var pipeline = new TranscriptionPipeline(capture, preprocessor, engine, hotwords,
            repository, performance, log: null, options: options);
        pipeline.Overload += (_, e) =>
        {
            Interlocked.Increment(ref overloadCount);
            maxDropped = Math.Max(maxDropped, e.DroppedSeconds);
        };

        await pipeline.StartAsync("fake", session);

        for (int i = 0; i < 60; i++)
        {
            capture.RaiseFrames(new float[1600], Mono16k);
        }

        await Task.Delay(300); // let the slow consumer fall behind

        for (int i = 0; i < 60; i++)
        {
            capture.RaiseFrames(new float[1600], Mono16k);
        }

        await pipeline.StopAsync();

        Assert.True(overloadCount >= 1, "Overload event never fired.");
        Assert.True(maxDropped > 0, "Overload reported no dropped audio.");
        Assert.True(performance.MaxReportedDroppedSeconds > 0);
        Assert.True(performance.MaxReportedQueueSamples <= 0.5 * 16000,
            $"queue high-water mark {performance.MaxReportedQueueSamples} exceeded capacity.");
    }

    [Fact]
    public async Task Pipeline_PauseAndResume_UpdateCaptureState()
    {
        await using var temp = await TempSqlite.CreateAsync();
        var repository = temp.CreateSubtitleRepository();
        var session = NewSession();
        await repository.CreateSessionAsync(session);

        var capture = new FakeAudioCaptureService();
        var engine = new ScriptedStreamingEngine(Array.Empty<AsrDecodeResult>());

        await using var pipeline = new TranscriptionPipeline(capture, new FakePreprocessor(), engine,
            new FakeHotwordService(), repository);

        await pipeline.StartAsync("fake", session);
        Assert.Equal(TranscriptionState.Transcribing, pipeline.State);

        await pipeline.PauseAsync();
        Assert.Equal(TranscriptionState.Paused, pipeline.State);
        Assert.Equal(CaptureState.Paused, capture.State);

        await pipeline.ResumeAsync();
        Assert.Equal(TranscriptionState.Transcribing, pipeline.State);
        Assert.Equal(CaptureState.Running, capture.State);

        await pipeline.StopAsync();
        Assert.Equal(CaptureState.Stopped, capture.State);
    }
}
