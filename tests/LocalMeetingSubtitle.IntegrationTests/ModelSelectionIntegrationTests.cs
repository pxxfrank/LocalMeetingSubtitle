using LocalMeetingSubtitle.Asr;
using Xunit.Abstractions;

namespace LocalMeetingSubtitle.IntegrationTests;

/// <summary>
/// End-to-end checks for the selectable recognition models: the pinned sizes must match the files on
/// disk (otherwise a fully downloaded model would still report "not installed"), and the bilingual
/// descriptor must build a real recognizer with model-level hotwords off.
/// </summary>
public sealed class ModelSelectionIntegrationTests
{
    private readonly ITestOutputHelper _output;

    public ModelSelectionIntegrationTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Pinned_sizes_match_the_files_on_disk()
    {
        var modelsRoot = ModelLocator.FindModelsRoot();
        Skip.If(modelsRoot is null, "models/ directory not present.", _output);
        if (modelsRoot is null) return;

        foreach (var descriptor in AsrModelCatalog.All)
        {
            var directory = Path.Combine(modelsRoot, descriptor.DirectoryName);
            Skip.If(!Directory.Exists(directory), $"{descriptor.Id} is not installed locally.", _output);
            if (!Directory.Exists(directory)) continue;

            foreach (var file in descriptor.Files.Where(f => f.Required))
            {
                var path = Path.Combine(directory, file.RelativePath);
                Assert.True(File.Exists(path), $"{descriptor.Id}/{file.RelativePath} is missing");
                Assert.Equal(file.SizeBytes!.Value, new FileInfo(path).Length);
            }
        }
    }

    [Fact]
    public async Task Bilingual_model_builds_with_model_level_hotwords_off()
    {
        var modelsRoot = ModelLocator.FindModelsRoot();
        var modelDirectory = modelsRoot is null
            ? null
            : Path.Combine(modelsRoot, AsrModelCatalog.StreamingZipformerBilingualZhEn.DirectoryName);
        Skip.If(modelDirectory is null || !Directory.Exists(modelDirectory),
            "Bilingual zh-en model not present under models/.", _output);
        if (modelsRoot is null || modelDirectory is null || !Directory.Exists(modelDirectory)) return;

        // The app passes no hotwords file here because WriteModelHotwordFile returns null for a
        // descriptor with SupportsHotwords == false; the engine must then stay on greedy_search.
        var options = AsrOptionsFactory.FromDescriptor(
            AsrModelCatalog.StreamingZipformerBilingualZhEn, modelsRoot, hotwordsFile: null);
        using var engine = new SherpaOnnxAsrEngine();
        var init = await engine.InitializeAsync(options);

        _output.WriteLine(init.Message);
        Assert.True(init.Ok, init.Message);
        Assert.True(engine.Capabilities.Streaming);
        Assert.True(engine.Capabilities.English);
        Assert.Contains("greedy_search", engine.Capabilities.Description);
        Assert.Contains("hotwords=off", engine.Capabilities.Description);
    }
}
