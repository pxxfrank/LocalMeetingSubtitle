using System.Text;
using LocalMeetingSubtitle.Asr;
using Xunit.Abstractions;

namespace LocalMeetingSubtitle.IntegrationTests;

public sealed class HotwordWiringTests
{
    private readonly ITestOutputHelper _output;

    public HotwordWiringTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task AsrOptionsFactory_WithHotwordsFile_MakesEngineUseModifiedBeamSearch()
    {
        var modelsRoot = ModelLocator.FindModelsRoot();
        var modelDirectory = modelsRoot is null
            ? null
            : Path.Combine(modelsRoot, AsrModelCatalog.StreamingZipformerZh14M.DirectoryName);
        Skip.If(modelDirectory is null || !Directory.Exists(modelDirectory),
            "Streaming zipformer zh-14M model not present under models/.", _output);
        if (modelDirectory is null || !Directory.Exists(modelDirectory))
        {
            return;
        }

        var directory = Path.Combine(Path.GetTempPath(), "lms-hotwords-it-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var hotwordsFile = Path.Combine(directory, "hotwords.txt");
        await File.WriteAllTextAsync(hotwordsFile, "深 度 求 索\nAtlas 800I\n", new UTF8Encoding(false));

        try
        {
            // With a hotwords file, a streaming transducer must switch to modified_beam_search.
            var withHotwords = AsrOptionsFactory.FromDescriptor(
                AsrModelCatalog.StreamingZipformerZh14M, modelsRoot!, hotwordsFile);
            using (var engine = new SherpaOnnxAsrEngine())
            {
                var init = await engine.InitializeAsync(withHotwords);
                _output.WriteLine("with hotwords: " + init.Message);
                Assert.True(init.Ok, init.Message);
                Assert.Contains("modified_beam_search", engine.Capabilities.Description);
                Assert.Contains("hotwords=on", engine.Capabilities.Description);
            }

            // Without a hotwords file, the default greedy_search decoding stays in effect.
            var withoutHotwords = AsrOptionsFactory.FromDescriptor(
                AsrModelCatalog.StreamingZipformerZh14M, modelsRoot!);
            using (var engine = new SherpaOnnxAsrEngine())
            {
                var init = await engine.InitializeAsync(withoutHotwords);
                _output.WriteLine("without hotwords: " + init.Message);
                Assert.True(init.Ok, init.Message);
                Assert.Contains("greedy_search", engine.Capabilities.Description);
                Assert.Contains("hotwords=off", engine.Capabilities.Description);
            }
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); }
            catch (IOException) { /* best effort */ }
        }
    }
}
