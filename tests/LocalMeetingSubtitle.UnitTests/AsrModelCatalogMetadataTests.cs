using LocalMeetingSubtitle.Asr;
using LocalMeetingSubtitle.Core.Models;

namespace LocalMeetingSubtitle.UnitTests;

/// <summary>
/// Guards the catalog metadata the rest of the app depends on: which models may be selected for the
/// live path, which ones may claim model-level hotwords, and the pinned sizes that make download
/// progress (and installed-file verification) meaningful.
/// </summary>
public sealed class AsrModelCatalogMetadataTests
{
    [Fact]
    public void Streaming_models_are_the_live_selectable_set()
    {
        Assert.NotEmpty(AsrModelCatalog.StreamingModels);
        Assert.All(AsrModelCatalog.StreamingModels,
            m => Assert.Equal(AsrModelKind.StreamingTransducer, m.Kind));

        Assert.Contains(AsrModelCatalog.StreamingZipformerZh14M, AsrModelCatalog.StreamingModels);
        Assert.Contains(AsrModelCatalog.StreamingZipformerBilingualZhEn, AsrModelCatalog.StreamingModels);
        // The live offline path still emits zero-duration segments, so offline models stay out.
        Assert.DoesNotContain(AsrModelCatalog.SenseVoiceSmall, AsrModelCatalog.StreamingModels);
    }

    [Fact]
    public void The_default_model_is_selectable_and_installed_first()
    {
        Assert.Equal(AsrModelCatalog.StreamingZipformerZh14M, AsrModelCatalog.StreamingModels[0]);
        Assert.True(AsrModelCatalog.StreamingZipformerZh14M.SupportsChinese);
        Assert.False(AsrModelCatalog.StreamingZipformerZh14M.SupportsEnglish);
    }

    [Fact]
    public void The_bilingual_model_never_claims_model_level_hotwords()
    {
        // Measured on sherpa-onnx 1.13.8 (docs/MODEL_SELECTION.md): the output was byte-identical at
        // hotwords-score 1.5/4.0/20 on this model, and its token table holds no whole-word English
        // tokens. Claiming support would make the UI promise boosting that never happens.
        Assert.False(AsrModelCatalog.StreamingZipformerBilingualZhEn.SupportsHotwords);
        Assert.True(AsrModelCatalog.StreamingZipformerBilingualZhEn.SupportsEnglish);
        Assert.True(AsrModelCatalog.StreamingZipformerBilingualZhEn.SupportsChinese);

        Assert.True(AsrModelCatalog.StreamingZipformerZh14M.SupportsHotwords);
        Assert.False(AsrModelCatalog.SenseVoiceSmall.SupportsHotwords);
    }

    [Fact]
    public void Every_required_file_pins_a_size()
    {
        // HttpModelManager computes progress as done/total where total sums SizeBytes: with no sizes
        // the bar would jump 0 -> 1 and a truncated download could still look installed.
        Assert.All(AsrModelCatalog.All, descriptor =>
            Assert.All(descriptor.Files.Where(f => f.Required),
                f => Assert.True(f.SizeBytes is > 0, $"{descriptor.Id}/{f.RelativePath} has no pinned size")));
    }

    [Fact]
    public void Declared_size_equals_the_sum_of_the_required_files()
    {
        Assert.All(AsrModelCatalog.All, descriptor =>
            Assert.Equal(
                descriptor.Files.Where(f => f.Required).Sum(f => f.SizeBytes ?? 0),
                descriptor.ApproxSizeBytes));
    }

    [Fact]
    public void Every_required_file_has_a_download_source()
    {
        Assert.All(AsrModelCatalog.All, descriptor =>
            Assert.All(descriptor.Files.Where(f => f.Required),
                f => Assert.False(string.IsNullOrWhiteSpace(f.SourceUrl), $"{descriptor.Id}/{f.RelativePath}")));
    }
}
