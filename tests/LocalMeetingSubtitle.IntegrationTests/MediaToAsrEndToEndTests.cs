using LocalMeetingSubtitle.Asr;
using LocalMeetingSubtitle.Core.Abstractions;
using LocalMeetingSubtitle.Core.Models;
using LocalMeetingSubtitle.Media;
using Xunit.Abstractions;

namespace LocalMeetingSubtitle.IntegrationTests;

/// <summary>
/// V0.5 "first link": a real media file -> FFmpeg PCM -> sherpa-onnx offline recognition -> Chinese text.
/// Skipped (never faked) when FFmpeg, the test media, or the ASR model are absent.
/// </summary>
public sealed class MediaToAsrEndToEndTests
{
    private readonly ITestOutputHelper _output;

    public MediaToAsrEndToEndTests(ITestOutputHelper output) => _output = output;

    [Theory]
    [InlineData("0.mp4")]  // video container: audio track extracted from the video
    [InlineData("0.mkv")]
    [InlineData("0.ogg")]
    [InlineData("0.mp3")]
    public async Task Real_media_decodes_and_transcribes_to_chinese(string fileName)
    {
        var mediaPath = FindTestMedia(fileName);
        var modelsRoot = ModelLocator.FindModelsRoot();
        var modelDirectory = modelsRoot is null
            ? null
            : Path.Combine(modelsRoot, AsrModelCatalog.StreamingZipformerZh14M.DirectoryName);

        Skip.If(mediaPath is null, $"testmedia/{fileName} not present (run tools/fetch-ffmpeg.ps1 + generate media).", _output);
        Skip.If(modelDirectory is null || !Directory.Exists(modelDirectory), "streaming zh-14M model not present.", _output);
        if (mediaPath is null || modelDirectory is null || !Directory.Exists(modelDirectory))
        {
            return;
        }

        MediaToolPaths tools;
        try
        {
            tools = FFmpegLocator.Resolve();
        }
        catch (MediaDecodeException ex)
        {
            Skip.If(true, "FFmpeg not available: " + ex.Message, _output);
            return;
        }

        var decoder = new FFmpegMediaDecodeService(tools);
        var info = await decoder.ProbeAsync(mediaPath);
        _output.WriteLine($"probe: kind={info.Kind} container={info.ContainerFormat} duration={info.Duration.TotalSeconds:F2}s audioStreams={info.AudioStreams.Count}");

        var samples = new List<float>();
        await foreach (var block in decoder.DecodeAsync(new MediaDecodeRequest(mediaPath)))
        {
            samples.AddRange(block.Samples);
        }

        _output.WriteLine($"decoded {samples.Count} samples = {samples.Count / 16000.0:F2}s");
        Assert.True(samples.Count > 16000 * 4, "expected several seconds of decoded audio");

        var options = AsrOptionsFactory.FromDescriptor(AsrModelCatalog.StreamingZipformerZh14M, modelsRoot!);
        using var engine = new SherpaOnnxAsrEngine();
        var init = await engine.InitializeAsync(options);
        Assert.True(init.Ok, init.Message);

        using var session = engine.CreateSession();
        var pcm = samples.ToArray();
        const int chunkSize = 1600; // 100 ms
        var lastNonEmpty = "";
        for (var offset = 0; offset < pcm.Length; offset += chunkSize)
        {
            var length = Math.Min(chunkSize, pcm.Length - offset);
            session.AcceptWaveform(pcm.AsSpan(offset, length), 16000);
            while (session.IsReady())
            {
                session.Decode();
            }

            var partial = session.GetResult();
            if (!string.IsNullOrWhiteSpace(partial.Text))
            {
                lastNonEmpty = partial.Text;
            }

            if (partial.IsEndpoint)
            {
                session.Reset();
            }
        }

        session.InputFinished();
        while (session.IsReady())
        {
            session.Decode();
        }

        var finalResult = session.GetResult();
        var text = !string.IsNullOrWhiteSpace(finalResult.Text) ? finalResult.Text : lastNonEmpty;
        _output.WriteLine($"text: \"{text}\"");

        Assert.False(string.IsNullOrWhiteSpace(text), "decoded audio produced no text");
        Assert.Contains(text, IsCjk);
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
}
