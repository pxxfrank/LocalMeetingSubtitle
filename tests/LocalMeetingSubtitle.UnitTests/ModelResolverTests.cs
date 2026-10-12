using LocalMeetingSubtitle.Asr;
using LocalMeetingSubtitle.Core.Models;

namespace LocalMeetingSubtitle.UnitTests;

/// <summary>
/// The live path must always end up with a usable descriptor, and a stored choice that cannot be
/// honoured must be reported rather than silently replaced (a missing model used to disable Start
/// with no explanation).
/// </summary>
public sealed class ModelResolverTests
{
    private static readonly ModelDescriptor Zh14M = AsrModelCatalog.StreamingZipformerZh14M;
    private static readonly ModelDescriptor Bilingual = AsrModelCatalog.StreamingZipformerBilingualZhEn;

    private static Func<ModelDescriptor, bool> Installed(params string[] ids) =>
        d => ids.Contains(d.Id, StringComparer.Ordinal);

    [Fact]
    public void Requested_model_is_used_when_installed()
    {
        var result = ModelResolver.Resolve(
            Bilingual.Id, AsrModelCatalog.StreamingModels, Zh14M, Installed(Zh14M.Id, Bilingual.Id));

        Assert.Equal(Bilingual.Id, result.Descriptor.Id);
        Assert.Equal(Bilingual.Id, result.Requested?.Id);
        Assert.Null(result.FallbackReason);
    }

    [Fact]
    public void Uninstalled_requested_model_falls_back_and_explains()
    {
        var result = ModelResolver.Resolve(
            Bilingual.Id, AsrModelCatalog.StreamingModels, Zh14M, Installed(Zh14M.Id));

        Assert.Equal(Zh14M.Id, result.Descriptor.Id);
        Assert.Equal(Bilingual.Id, result.Requested?.Id);
        Assert.NotNull(result.FallbackReason);
        Assert.Contains(Zh14M.DisplayName, result.FallbackReason!, StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_id_behaves_like_no_stored_choice()
    {
        var result = ModelResolver.Resolve(
            "no-such-model", AsrModelCatalog.StreamingModels, Zh14M, Installed(Zh14M.Id));

        Assert.Equal(Zh14M.Id, result.Descriptor.Id);
        Assert.Null(result.Requested);
        Assert.Null(result.FallbackReason);
    }

    [Fact]
    public void First_run_uses_the_first_installed_model_without_a_reason()
    {
        var result = ModelResolver.Resolve(
            null, AsrModelCatalog.StreamingModels, Zh14M, Installed(Bilingual.Id));

        Assert.Equal(Bilingual.Id, result.Descriptor.Id);
        Assert.Null(result.FallbackReason);
    }

    [Fact]
    public void Nothing_installed_still_returns_a_descriptor_and_explains()
    {
        var result = ModelResolver.Resolve(
            null, AsrModelCatalog.StreamingModels, Zh14M, Installed());

        Assert.Equal(Zh14M.Id, result.Descriptor.Id);
        Assert.NotNull(result.FallbackReason);
        Assert.Contains("未安装", result.FallbackReason!, StringComparison.Ordinal);
    }

    [Fact]
    public void Explicit_choice_with_nothing_installed_still_returns_the_default()
    {
        var result = ModelResolver.Resolve(
            Bilingual.Id, AsrModelCatalog.StreamingModels, Zh14M, Installed());

        Assert.Equal(Zh14M.Id, result.Descriptor.Id);
        Assert.Equal(Bilingual.Id, result.Requested?.Id);
        Assert.NotNull(result.FallbackReason);
    }

    [Fact]
    public void Empty_selectable_set_never_returns_a_null_descriptor()
    {
        var result = ModelResolver.Resolve(
            null, Array.Empty<ModelDescriptor>(), Zh14M, Installed());

        Assert.Equal(Zh14M.Id, result.Descriptor.Id);
        Assert.NotNull(result.FallbackReason);
    }

    [Fact]
    public void Id_matching_is_case_insensitive()
    {
        var result = ModelResolver.Resolve(
            Zh14M.Id.ToUpperInvariant(), AsrModelCatalog.StreamingModels, Bilingual, Installed(Zh14M.Id));

        Assert.Equal(Zh14M.Id, result.Descriptor.Id);
        Assert.Null(result.FallbackReason);
    }
}
