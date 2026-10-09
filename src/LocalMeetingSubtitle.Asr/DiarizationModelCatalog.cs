using LocalMeetingSubtitle.Core.Models;

namespace LocalMeetingSubtitle.Asr;

/// <summary>
/// Declarative catalog of the two models offline speaker diarization needs: a pyannote segmentation
/// model and a speaker-embedding model. Kept separate from <see cref="AsrModelCatalog"/> so a
/// diarization descriptor can never be picked as the live recognizer.
///
/// Sources and licenses verified 2026-10-09:
///  - segmentation: <c>csukuangfj/sherpa-onnx-pyannote-segmentation-3-0</c> (MIT, CNRS), <c>model.onnx</c> 5.72 MB.
///  - embedding:    <c>3dspeaker_speech_eres2net_base_sv_zh-cn_3dspeaker_16k.onnx</c> (Apache-2.0, 3D-Speaker),
///                  39 593 761 bytes, sha256 1a331345f04805badbb495c775a6ddffcdd1a732567d5ec8b3d5749e3c7a5e4b.
/// </summary>
public static class DiarizationModelCatalog
{
    private const string Hf = "https://huggingface.co";
    private const string SherpaRelease = "https://github.com/k2-fsa/sherpa-onnx/releases/download/speaker-recongition-models";

    public static ModelDescriptor Segmentation { get; } = BuildSegmentation();

    public static ModelDescriptor Embedding { get; } = BuildEmbedding();

    public static IReadOnlyList<ModelDescriptor> All { get; } = new[]
    {
        Segmentation,
        Embedding
    };

    private static ModelDescriptor BuildSegmentation()
    {
        const string repo = "csukuangfj/sherpa-onnx-pyannote-segmentation-3-0";
        const string dir = "sherpa-onnx-pyannote-segmentation-3-0";
        string Url(string f) => $"{Hf}/{repo}/resolve/main/{f}";
        return new ModelDescriptor
        {
            Id = "pyannote-segmentation-3-0",
            DisplayName = "Pyannote 说话人分段 3.0",
            Kind = AsrModelKind.Offline,
            License = "MIT (pyannote / CNRS)",
            SourceUrl = $"{Hf}/{repo}",
            DirectoryName = dir,
            SupportsHotwords = false,
            SupportsEnglish = true,
            SupportsChinese = true,
            ApproxSizeBytes = 5_720_000,
            ModelFile = "model.onnx",
            Notes = "Diarization segmentation model (not an ASR model).",
            Files = new[]
            {
                new ModelFileSpec("model.onnx", true, SourceUrl: Url("model.onnx"))
            }
        };
    }

    private static ModelDescriptor BuildEmbedding()
    {
        const string file = "3dspeaker_speech_eres2net_base_sv_zh-cn_3dspeaker_16k.onnx";
        const string dir = "3dspeaker-eres2net-base-zh-16k";
        return new ModelDescriptor
        {
            Id = "3dspeaker-eres2net-base-zh-16k",
            DisplayName = "3D-Speaker ERes2Net 中文声纹 (16k)",
            Kind = AsrModelKind.Offline,
            License = "Apache-2.0 (3D-Speaker / ModelScope)",
            SourceUrl = SherpaRelease,
            DirectoryName = dir,
            SupportsHotwords = false,
            SupportsEnglish = false,
            SupportsChinese = true,
            ApproxSizeBytes = 39_593_761,
            ModelFile = file,
            Notes = "Diarization speaker-embedding model (not an ASR model).",
            Files = new[]
            {
                new ModelFileSpec(file, true, "1a331345f04805badbb495c775a6ddffcdd1a732567d5ec8b3d5749e3c7a5e4b", 39_593_761, $"{SherpaRelease}/{file}")
            }
        };
    }
}
