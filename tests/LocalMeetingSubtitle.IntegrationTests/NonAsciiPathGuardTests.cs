using LocalMeetingSubtitle.Asr;
using LocalMeetingSubtitle.Core.Hotwords;
using LocalMeetingSubtitle.Core.Models;
using Xunit.Abstractions;

namespace LocalMeetingSubtitle.IntegrationTests;

/// <summary>
/// The recognizer must fail loudly (not silently) when a path sherpa-onnx cannot read is supplied.
/// Root cause #3 / this regression: a non-ASCII hotwords path made the engine "initialize" while
/// never becoming ready, so the app captured audio and produced no subtitles at all.
/// </summary>
public sealed class NonAsciiPathGuardTests
{
    private readonly ITestOutputHelper _output;

    public NonAsciiPathGuardTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Engine_rejects_a_non_ascii_hotwords_path()
    {
        var modelsRoot = ModelLocator.FindModelsRoot();
        var modelDirectory = modelsRoot is null
            ? null
            : Path.Combine(modelsRoot, AsrModelCatalog.StreamingZipformerZh14M.DirectoryName);
        Skip.If(modelDirectory is null || !Directory.Exists(modelDirectory),
            "streaming zh-14M model not present under models/.", _output);
        if (modelDirectory is null || !Directory.Exists(modelDirectory)) return;

        var options = AsrOptionsFactory.FromDescriptor(AsrModelCatalog.StreamingZipformerZh14M, modelsRoot!);
        var bad = Path.Combine(Path.GetTempPath(), "字幕君", "hotwords.txt");
        var withBadHotwords = new AsrEngineOptions
        {
            ModelDirectory = options.ModelDirectory,
            Kind = options.Kind,
            EncoderFileName = options.EncoderFileName,
            DecoderFileName = options.DecoderFileName,
            JoinerFileName = options.JoinerFileName,
            TokensFileName = options.TokensFileName,
            HotwordsFile = bad,
            NumThreads = options.NumThreads
        };

        using var engine = new SherpaOnnxAsrEngine();
        var init = await engine.InitializeAsync(withBadHotwords);

        _output.WriteLine($"status={init.Status} message={init.Message}");
        Assert.False(init.Ok, "the engine must refuse a non-ASCII hotwords path");
        Assert.Equal(AsrInitStatus.Failed, init.Status);
        Assert.Contains("ASCII", init.Message);
    }

    [Fact]
    public void The_shipped_hotwords_temp_path_is_ascii()
    {
        Assert.True(ModelHotwordFile.IsAsciiPath(ModelHotwordFile.DefaultTempPath));
    }
}
