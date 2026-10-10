using LocalMeetingSubtitle.Asr;
using LocalMeetingSubtitle.Core.Abstractions;
using LocalMeetingSubtitle.Core.Models;

namespace LocalMeetingSubtitle.UnitTests;

/// <summary>
/// V0.5 Phase 2: the three modes must map to genuinely different, verifiable models or inference
/// configurations — never to the same configuration behind a different label.
/// </summary>
public sealed class TranscriptionModeCatalogTests
{
    private const string Zh14M = "streaming-zipformer-zh-14M";
    private const string SenseVoice = "sense-voice-small-int8";

    [Fact]
    public void EveryMode_ResolvesToACataloguedModelAndRecordsItOnTheSegments()
    {
        var manager = new FakeModelManager(Zh14M, SenseVoice);

        foreach (var mode in TranscriptionModeCatalog.All)
        {
            var resolved = TranscriptionModeCatalog.Resolve(mode, manager);

            Assert.True(resolved.IsAvailable);
            Assert.Null(resolved.UnavailableReason);
            Assert.Contains(resolved.Descriptor, AsrModelCatalog.All);
            Assert.Equal(resolved.Descriptor.Id, resolved.TranscriptionOptions.ModelId);
            Assert.False(string.IsNullOrWhiteSpace(resolved.DisplayName));
        }
    }

    [Fact]
    public void FastAndStandard_DifferInDecodingMethod()
    {
        var manager = new FakeModelManager(Zh14M);

        var fast = TranscriptionModeCatalog.Resolve(TranscriptionMode.Fast, manager);
        var standard = TranscriptionModeCatalog.Resolve(TranscriptionMode.Standard, manager, hotwordsFile: "hotwords.txt");

        Assert.Equal("greedy_search", fast.EngineOptions.DecodingMethod);
        Assert.Equal("modified_beam_search", standard.EngineOptions.DecodingMethod);
        Assert.True(fast.EngineOptions.HotwordsFile is null);
        Assert.Equal("hotwords.txt", standard.EngineOptions.HotwordsFile);
    }

    [Fact]
    public void HighAccuracy_UsesTheOfflineModelWithAnOverlapAndNoModelHotwords()
    {
        var manager = new FakeModelManager(Zh14M, SenseVoice);

        var high = TranscriptionModeCatalog.Resolve(TranscriptionMode.HighAccuracy, manager, hotwordsFile: "hotwords.txt");

        Assert.Equal(AsrModelKind.Offline, high.EngineOptions.Kind);
        Assert.Equal(SenseVoice, high.Descriptor.Id);
        Assert.True(high.TranscriptionOptions.OverlapSeconds > 0, "the offline mode relies on the overlap to keep sentences whole.");
        Assert.True(high.EngineOptions.UseInverseTextNormalization);
        Assert.False(high.TranscriptionOptions.AppendTerminalPunctuation, "SenseVoice already emits punctuation.");

        // sense_voice implements only greedy_search, so model-level hotwords are impossible and must
        // not be passed to the recognizer.
        Assert.False(high.Descriptor.SupportsHotwords);
        Assert.Null(high.EngineOptions.HotwordsFile);
    }

    [Fact]
    public void StreamingModes_AppendTerminalPunctuationBecauseTheTransducerDoesNot()
    {
        var manager = new FakeModelManager(Zh14M);

        Assert.True(TranscriptionModeCatalog.Resolve(TranscriptionMode.Fast, manager).TranscriptionOptions.AppendTerminalPunctuation);
        Assert.True(TranscriptionModeCatalog.Resolve(TranscriptionMode.Standard, manager).TranscriptionOptions.AppendTerminalPunctuation);
    }

    [Fact]
    public void Modes_AreDistinguishableOnModelOrSegmentation()
    {
        var manager = new FakeModelManager(Zh14M, SenseVoice);

        var fast = TranscriptionModeCatalog.Resolve(TranscriptionMode.Fast, manager);
        var standard = TranscriptionModeCatalog.Resolve(TranscriptionMode.Standard, manager);
        var high = TranscriptionModeCatalog.Resolve(TranscriptionMode.HighAccuracy, manager);

        Assert.NotEqual(fast.Descriptor.Id, high.Descriptor.Id);
        Assert.NotEqual(fast.DisplayName, high.DisplayName);
        Assert.NotEqual(standard.DisplayName, high.DisplayName);
        Assert.NotEqual(standard.TranscriptionOptions.MaxSegmentSeconds, high.TranscriptionOptions.MaxSegmentSeconds);
    }

    [Fact]
    public void MissingModel_IsReportedUnavailableInsteadOfThrowing()
    {
        var manager = new FakeModelManager(Zh14M); // SenseVoice not installed

        var high = TranscriptionModeCatalog.Resolve(TranscriptionMode.HighAccuracy, manager);

        Assert.False(high.IsAvailable);
        Assert.False(string.IsNullOrWhiteSpace(high.UnavailableReason));
        Assert.Contains(SenseVoice, high.UnavailableReason);
    }

    [Fact]
    public void SenseVoice_AdvertisesNoModelLevelHotwords()
    {
        Assert.False(AsrModelCatalog.SenseVoiceSmall.SupportsHotwords);
        Assert.True(AsrModelCatalog.StreamingZipformerZh14M.SupportsHotwords);
    }

    private sealed class FakeModelManager : IModelManager
    {
        private readonly HashSet<string> _installed;

        public FakeModelManager(params string[] installed) => _installed = new HashSet<string>(installed);

        public string ModelsRoot => Path.Combine(Path.GetTempPath(), "subtitlejun-tests", "models");

        public IReadOnlyList<ModelDescriptor> Catalog => AsrModelCatalog.All;

        public ModelDescriptor? FindById(string id) => AsrModelCatalog.All.FirstOrDefault(d => d.Id == id);

        public bool IsInstalled(ModelDescriptor descriptor) => _installed.Contains(descriptor.Id);

        public IReadOnlyList<ModelFileSpec> MissingFiles(ModelDescriptor descriptor) =>
            IsInstalled(descriptor) ? Array.Empty<ModelFileSpec>() : descriptor.Files.ToList();

        public string GetModelDirectory(ModelDescriptor descriptor) => Path.Combine(ModelsRoot, descriptor.DirectoryName);

        public Task<ModelInstallResult> EnsureInstalledAsync(
            ModelDescriptor descriptor, IProgress<double>? progress = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ModelInstallResult(false, "not supported in tests"));
    }
}
