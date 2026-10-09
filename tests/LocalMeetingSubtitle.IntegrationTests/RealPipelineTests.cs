using System.Text;
using LocalMeetingSubtitle.Asr;
using LocalMeetingSubtitle.Core.Abstractions;
using LocalMeetingSubtitle.Core.Audio;
using LocalMeetingSubtitle.Core.Hotwords;
using LocalMeetingSubtitle.Core.Models;
using LocalMeetingSubtitle.Core.Transcription;
using LocalMeetingSubtitle.Diagnostics;
using Xunit.Abstractions;

namespace LocalMeetingSubtitle.IntegrationTests;

/// <summary>
/// Drives the real sherpa-onnx engine through the real pipeline. The rest of the suite uses
/// scripted engines/doubles, so this is the closest coverage of what the shipped app runs.
/// </summary>
public sealed class RealPipelineTests
{
    private static AudioFormat Mono16k => AudioFormat.Float32(16000, 1);

    private readonly ITestOutputHelper _output;

    public RealPipelineTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task StreamingModel_ThroughPipeline_EmitsFinalsAndNoRecognitionErrors()
        => await RunAsync(hotwordFile: null);

    [Fact]
    public async Task StreamingModel_WithModelLevelHotwords_EmitsFinalsAndNoRecognitionErrors()
    {
        var path = Path.Combine(Path.GetTempPath(), "lms-hotwords-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(path, ModelHotwordFile.Build(new[] { "会议", "字幕" }), new UTF8Encoding(false));
        try
        {
            await RunAsync(hotwordFile: path);
        }
        finally
        {
            try { File.Delete(path); } catch { /* best effort */ }
        }
    }

    private async Task RunAsync(string? hotwordFile)
    {
        var modelsRoot = ModelLocator.FindModelsRoot();
        var wavPath = modelsRoot is null
            ? null
            : Path.Combine(modelsRoot, AsrModelCatalog.StreamingZipformerZh14M.DirectoryName, "test_wavs", "0.wav");

        Skip.If(wavPath is null || !File.Exists(wavPath),
            "test_wavs/0.wav not present for the streaming zh-14M model.", _output);
        if (wavPath is null || !File.Exists(wavPath))
        {
            return;
        }

        var (samples, sampleRate) = WavReader.ReadMono16BitPcm(wavPath);
        Assert.Equal(16000, sampleRate);

        await using var temp = await TempSqlite.CreateAsync();
        var repository = temp.CreateSubtitleRepository();
        var session = new MeetingSession
        {
            SessionId = Guid.NewGuid().ToString("N"),
            Title = "real-pipeline",
            AudioDeviceName = "fake"
        };
        await repository.CreateSessionAsync(session);

        var options = AsrOptionsFactory.FromDescriptor(
            AsrModelCatalog.StreamingZipformerZh14M, modelsRoot!, hotwordFile);
        using var engine = new SherpaOnnxAsrEngine();
        var init = await engine.InitializeAsync(options);
        Assert.True(init.Ok, init.Message);
        if (hotwordFile is not null)
        {
            Assert.True(engine.Capabilities.ModelLevelHotwords);
        }

        var capture = new FakeAudioCaptureService();
        var hotwords = new DefaultHotwordService(temp.CreateHotwordRepository());
        await hotwords.ReloadAsync();
        hotwords.SetEngineCapability(supportsModelHotwords: true, modelCanBeRebuiltWithoutDataLoss: true);

        using var performance = new ProcessPerformanceMonitor();
        var log = new RecordingLogger();

        var errors = new List<string>();
        var finals = new List<TranscriptEvent>();
        var gate = new object();

        await using var pipeline = new TranscriptionPipeline(
            capture, new DefaultAudioPreprocessor(16000), engine, hotwords, repository, performance, log);
        pipeline.ErrorOccurred += (_, message) => { lock (gate) errors.Add(message); };
        pipeline.TranscriptUpdated += (_, e) =>
        {
            if (e.Kind == TranscriptEventKind.Final)
            {
                lock (gate) finals.Add(e);
            }
        };

        await pipeline.StartAsync("fake", session);

        // Feed the whole file in 100 ms chunks, the same buffer size the capture service uses.
        const int chunkSize = 1600;
        for (int offset = 0; offset < samples.Length; offset += chunkSize)
        {
            int length = Math.Min(chunkSize, samples.Length - offset);
            capture.RaiseFrames(samples[offset..(offset + length)], Mono16k);
            await Task.Delay(2);
        }

        await Task.Delay(750); // let the ASR loop drain the queue
        await pipeline.StopAsync();

        foreach (var entry in log.Entries)
        {
            _output.WriteLine(entry);
        }

        foreach (var error in errors)
        {
            _output.WriteLine("pipeline error: " + error);
        }

        Assert.Empty(errors);
        Assert.NotEmpty(finals);
    }
}

/// <summary>Logger that records every entry (with the exception) so tests can surface stack traces.</summary>
internal sealed class RecordingLogger : IAppLogger
{
    private readonly object _gate = new();
    private readonly List<string> _entries = new();

    public IReadOnlyList<string> Entries
    {
        get { lock (_gate) return _entries.ToArray(); }
    }

    public void Debug(string message) => Add("DEBUG", message, null);
    public void Info(string message) => Add("INFO", message, null);
    public void Warn(string message) => Add("WARN", message, null);
    public void Error(string message, Exception? exception = null) => Add("ERROR", message, exception);

    private void Add(string level, string message, Exception? exception)
    {
        var text = $"[{level}] {message}";
        if (exception is not null)
        {
            text += $" | {exception.GetType().Name}: {exception.Message}";
            if (!string.IsNullOrWhiteSpace(exception.StackTrace))
            {
                text += Environment.NewLine + exception.StackTrace;
            }
        }

        lock (_gate)
        {
            _entries.Add(text);
        }
    }
}
