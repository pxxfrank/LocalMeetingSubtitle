using LocalMeetingSubtitle.Asr;
using LocalMeetingSubtitle.Core.Abstractions;
using LocalMeetingSubtitle.Core.Models;
using LocalMeetingSubtitle.Core.Transcription;
using LocalMeetingSubtitle.Media;
using Xunit.Abstractions;

namespace LocalMeetingSubtitle.IntegrationTests;

/// <summary>
/// V0.5 Phase 2: a real media file decoded by FFmpeg, segmented by the VAD, recognized by a real
/// sherpa-onnx model, and returned as segments carrying their true position on the media timeline.
/// Skipped (never faked) when FFmpeg, the media, or the model is absent.
/// </summary>
public sealed class FileTranscriptionTests
{
    private readonly ITestOutputHelper _output;

    public FileTranscriptionTests(ITestOutputHelper output) => _output = output;

    [Theory]
    [InlineData("0.mp4", TranscriptionMode.Fast)]
    [InlineData("0.mp3", TranscriptionMode.Fast)]
    [InlineData("0.mkv", TranscriptionMode.Standard)]
    [InlineData("0.ogg", TranscriptionMode.Fast)]
    public async Task Real_file_yields_segments_with_global_timestamps(string fileName, TranscriptionMode mode)
    {
        var run = await RunAsync(fileName, mode);
        if (run is null)
        {
            return;
        }

        var (result, info) = run.Value;
        foreach (var segment in result.Segments)
        {
            _output.WriteLine($"[{segment.Start:hh\\:mm\\:ss\\.fff} - {segment.End:hh\\:mm\\:ss\\.fff}] "
                + $"#{segment.SourceChunkId} {segment.Text}");
        }

        Assert.True(result.Completed, "transcription must complete for a readable file");
        Assert.NotEmpty(result.Segments);

        var duration = info.Duration;
        for (int i = 0; i < result.Segments.Count; i++)
        {
            var segment = result.Segments[i];
            Assert.True(segment.End > segment.Start, $"segment {i} must have a positive duration");
            Assert.True(segment.Start >= TimeSpan.Zero, $"segment {i} must not start before the media");
            Assert.True(segment.End <= duration + TimeSpan.FromSeconds(1), $"segment {i} must end within the media");
            Assert.Equal(mode == TranscriptionMode.Fast || mode == TranscriptionMode.Standard
                ? AsrModelCatalog.StreamingZipformerZh14M.Id
                : AsrModelCatalog.SenseVoiceSmall.Id, segment.ModelId);

            if (i > 0)
            {
                Assert.True(segment.Start >= result.Segments[i - 1].End - TimeSpan.FromMilliseconds(1),
                    $"segment {i} must not start before segment {i - 1} ended");
            }
        }

        Assert.Contains(string.Concat(result.Segments.Select(s => s.Text)), IsCjk);
    }

    [Fact]
    public async Task High_accuracy_mode_produces_punctuated_chinese()
    {
        var run = await RunAsync("0.mp4", TranscriptionMode.HighAccuracy);
        if (run is null)
        {
            return;
        }

        var text = string.Concat(run.Value.Result.Segments.Select(s => s.Text));
        _output.WriteLine($"text: \"{text}\"");

        Assert.Contains(text, IsCjk);
        // SenseVoice runs with inverse text normalization, which yields sentence punctuation.
        Assert.Contains(text, c => c is '。' or '，' or '！' or '？' or ',');
    }

    [Fact]
    public async Task Long_file_is_split_into_multiple_monotonic_segments()
    {
        var run = await RunAsync("long-gaps.wav", TranscriptionMode.HighAccuracy);
        if (run is null)
        {
            return;
        }

        var (result, _) = run.Value;
        foreach (var segment in result.Segments)
        {
            _output.WriteLine($"[{segment.Start:hh\\:mm\\:ss\\.fff} - {segment.End:hh\\:mm\\:ss\\.fff}] {segment.Text}");
        }

        Assert.True(result.Segments.Count >= 3, $"expected the VAD to find several utterances, got {result.Segments.Count}.");
        Assert.True(result.AudioDuration > TimeSpan.FromSeconds(20), "the fixture must be long audio.");
        for (int i = 1; i < result.Segments.Count; i++)
        {
            Assert.True(result.Segments[i].Start >= result.Segments[i - 1].End - TimeSpan.FromMilliseconds(1),
                $"segment {i} must not start before segment {i - 1} ended.");
        }

        Assert.All(result.Segments, s => Assert.True(s.End > s.Start));
    }

    [Fact]
    public async Task Continuous_speech_past_the_cap_is_split_with_a_non_overlapping_timeline()
    {
        // 56 s of unbroken speech: the 30 s cap must fire, carry an overlap, and still report a
        // timeline whose segments do not overlap and whose text is not duplicated.
        var run = await RunAsync("long-continuous.wav", TranscriptionMode.HighAccuracy);
        if (run is null)
        {
            return;
        }

        var (result, _) = run.Value;
        foreach (var segment in result.Segments)
        {
            _output.WriteLine($"[{segment.Start:hh\\:mm\\:ss\\.fff} - {segment.End:hh\\:mm\\:ss\\.fff}] {segment.Text}");
        }

        Assert.True(result.Segments.Count >= 2, "a 56 s continuous clip must be split at the 30 s cap.");
        for (int i = 1; i < result.Segments.Count; i++)
        {
            Assert.True(result.Segments[i].Start >= result.Segments[i - 1].End - TimeSpan.FromMilliseconds(1),
                $"the overlap must not make segment {i} start before segment {i - 1} ended.");
        }

        var joined = string.Concat(result.Segments.Select(s => s.Text));
        Assert.Contains(joined, IsCjk);
    }

    [Fact]
    public async Task Missing_file_is_reported_as_not_found()
    {
        var tools = TryResolveTools();
        if (tools is null)
        {
            return;
        }

        var media = new FFmpegMediaDecodeService(tools);
        var ex = await Assert.ThrowsAsync<MediaDecodeException>(
            () => media.ProbeAsync(Path.Combine(Path.GetTempPath(), "subtitlejun-missing-" + Guid.NewGuid().ToString("N") + ".mp4")));
        Assert.Equal(MediaErrorKind.FileNotFound, ex.Kind);
    }

    // ---- helpers -------------------------------------------------------------------------------

    private async Task<(OfflineTranscriptionResult Result, MediaInfo Info)?> RunAsync(
        string fileName,
        TranscriptionMode mode)
    {
        var mediaPath = FindTestMedia(fileName);
        Skip.If(mediaPath is null, $"testmedia/{fileName} not present (run tools/fetch-ffmpeg.ps1 + generate media).", _output);
        if (mediaPath is null)
        {
            return null;
        }

        var modelsRoot = ModelLocator.FindModelsRoot();
        Skip.If(modelsRoot is null, "models/ directory not present.", _output);
        if (modelsRoot is null)
        {
            return null;
        }

        var tools = TryResolveTools();
        if (tools is null)
        {
            return null;
        }

        var modelManager = new DiskModelManager(modelsRoot);
        var resolved = TranscriptionModeCatalog.Resolve(mode, modelManager);
        Skip.If(!resolved.IsAvailable, resolved.UnavailableReason ?? "model not installed", _output);
        if (!resolved.IsAvailable)
        {
            return null;
        }

        var media = new FFmpegMediaDecodeService(tools);
        var info = await media.ProbeAsync(mediaPath);

        var options = resolved.TranscriptionOptions;

        using var engine = new SherpaOnnxAsrEngine();
        var init = await engine.InitializeAsync(resolved.EngineOptions);
        Assert.True(init.Ok, init.Message);

        var transcriber = new OfflineTranscriptionEngine(engine, options);
        var result = await transcriber.TranscribeAsync(
            media.DecodeAsync(new MediaDecodeRequest(mediaPath)),
            totalDuration: info.Duration);

        _output.WriteLine($"mode={resolved.DisplayName} model={resolved.Descriptor.Id} "
            + $"audio={result.AudioDuration.TotalSeconds:F2}s elapsed={result.Elapsed.TotalSeconds:F2}s rtf={result.Rtf:F4}");
        return (result, info);
    }

    private MediaToolPaths? TryResolveTools()
    {
        try
        {
            return FFmpegLocator.Resolve();
        }
        catch (MediaDecodeException ex)
        {
            Skip.If(true, "FFmpeg not available: " + ex.Message, _output);
            return null;
        }
    }

    private static string? FindTestMedia(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "testmedia", fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        return null;
    }

    private static bool IsCjk(char c) =>
        (c >= 0x4E00 && c <= 0x9FFF) ||
        (c >= 0x3400 && c <= 0x4DBF) ||
        (c >= 0xF900 && c <= 0xFAFF);

    /// <summary>Filesystem-backed model manager so the test does not need the download assembly.</summary>
    private sealed class DiskModelManager : IModelManager
    {
        public DiskModelManager(string modelsRoot) => ModelsRoot = modelsRoot;

        public string ModelsRoot { get; }
        public IReadOnlyList<ModelDescriptor> Catalog => AsrModelCatalog.All;

        public ModelDescriptor? FindById(string id) => AsrModelCatalog.All.FirstOrDefault(d => d.Id == id);

        public bool IsInstalled(ModelDescriptor descriptor) => MissingFiles(descriptor).Count == 0;

        public IReadOnlyList<ModelFileSpec> MissingFiles(ModelDescriptor descriptor) =>
            descriptor.Files
                .Where(f => f.Required && !File.Exists(Path.Combine(GetModelDirectory(descriptor), f.RelativePath)))
                .ToList();

        public string GetModelDirectory(ModelDescriptor descriptor) => Path.Combine(ModelsRoot, descriptor.DirectoryName);

        public Task<ModelInstallResult> EnsureInstalledAsync(
            ModelDescriptor descriptor, IProgress<double>? progress = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ModelInstallResult(false, "not supported in tests"));
    }
}
