using LocalMeetingSubtitle.Core.Models;

namespace LocalMeetingSubtitle.Asr;

/// <summary>
/// Declarative catalog of supported models. Sources verified against the sherpa-onnx
/// Hugging Face repositories (see docs/MODEL_SELECTION.md for the verification log).
/// </summary>
public static class AsrModelCatalog
{
    private const string Hf = "https://huggingface.co";

    public static ModelDescriptor StreamingZipformerZh14M { get; } = BuildZh14M();

    public static ModelDescriptor StreamingZipformerBilingualZhEn { get; } = BuildBilingual();

    public static ModelDescriptor SenseVoiceSmall { get; } = BuildSenseVoice();

    public static IReadOnlyList<ModelDescriptor> All { get; } = new[]
    {
        StreamingZipformerZh14M,
        StreamingZipformerBilingualZhEn,
        SenseVoiceSmall
    };

    private static ModelDescriptor BuildZh14M()
    {
        const string repo = "csukuangfj/sherpa-onnx-streaming-zipformer-zh-14M-2023-02-23";
        const string dir = "sherpa-onnx-streaming-zipformer-zh-14M-2023-02-23";
        string Url(string f) => $"{Hf}/{repo}/resolve/main/{f}";
        return new ModelDescriptor
        {
            Id = "streaming-zipformer-zh-14M",
            DisplayName = "Streaming Zipformer 中文 14M (INT8)",
            Kind = AsrModelKind.StreamingTransducer,
            License = "Apache-2.0",
            SourceUrl = $"{Hf}/{repo}",
            DirectoryName = dir,
            SupportsHotwords = true,
            SupportsEnglish = false,
            SupportsChinese = true,
            ApproxSizeBytes = 25_700_000,
            EncoderFile = "encoder-epoch-99-avg-1.int8.onnx",
            DecoderFile = "decoder-epoch-99-avg-1.int8.onnx",
            JoinerFile = "joiner-epoch-99-avg-1.int8.onnx",
            TokensFile = "tokens.txt",
            Notes = "Native streaming output; hotwords supported via modified_beam_search. Small (14M params).",
            Files = new[]
            {
                new ModelFileSpec("encoder-epoch-99-avg-1.int8.onnx", true, SourceUrl: Url("encoder-epoch-99-avg-1.int8.onnx")),
                new ModelFileSpec("decoder-epoch-99-avg-1.int8.onnx", true, SourceUrl: Url("decoder-epoch-99-avg-1.int8.onnx")),
                new ModelFileSpec("joiner-epoch-99-avg-1.int8.onnx", true, SourceUrl: Url("joiner-epoch-99-avg-1.int8.onnx")),
                new ModelFileSpec("tokens.txt", true, SourceUrl: Url("tokens.txt"))
            }
        };
    }

    private static ModelDescriptor BuildBilingual()
    {
        const string repo = "csukuangfj/sherpa-onnx-streaming-zipformer-bilingual-zh-en-2023-02-20";
        const string dir = "sherpa-onnx-streaming-zipformer-bilingual-zh-en-2023-02-20";
        string Url(string f) => $"{Hf}/{repo}/resolve/main/{f}";
        return new ModelDescriptor
        {
            Id = "streaming-zipformer-bilingual-zh-en",
            DisplayName = "Streaming Zipformer 中英双语 (INT8)",
            Kind = AsrModelKind.StreamingTransducer,
            License = "Apache-2.0",
            SourceUrl = $"{Hf}/{repo}",
            DirectoryName = dir,
            SupportsHotwords = true,
            SupportsEnglish = true,
            SupportsChinese = true,
            ApproxSizeBytes = 330_000_000,
            EncoderFile = "encoder-epoch-99-avg-1.int8.onnx",
            DecoderFile = "decoder-epoch-99-avg-1.int8.onnx",
            JoinerFile = "joiner-epoch-99-avg-1.int8.onnx",
            TokensFile = "tokens.txt",
            Notes = "Streaming bilingual zh/en; better for mixed Chinese/English and technical terms. Larger model.",
            Files = new[]
            {
                new ModelFileSpec("encoder-epoch-99-avg-1.int8.onnx", true, SourceUrl: Url("encoder-epoch-99-avg-1.int8.onnx")),
                new ModelFileSpec("decoder-epoch-99-avg-1.int8.onnx", true, SourceUrl: Url("decoder-epoch-99-avg-1.int8.onnx")),
                new ModelFileSpec("joiner-epoch-99-avg-1.int8.onnx", true, SourceUrl: Url("joiner-epoch-99-avg-1.int8.onnx")),
                new ModelFileSpec("tokens.txt", true, SourceUrl: Url("tokens.txt"))
            }
        };
    }

    private static ModelDescriptor BuildSenseVoice()
    {
        const string repo = "csukuangfj/sherpa-onnx-sense-voice-zh-en-ja-ko-yue-2024-07-17";
        const string dir = "sherpa-onnx-sense-voice-zh-en-ja-ko-yue-2024-07-17";
        string Url(string f) => $"{Hf}/{repo}/resolve/main/{f}";
        return new ModelDescriptor
        {
            Id = "sense-voice-small-int8",
            DisplayName = "SenseVoice Small (INT8, 离线)",
            Kind = AsrModelKind.Offline,
            License = "Apache-2.0 (FunAudioLLM/SenseVoice; sherpa-onnx conversion)",
            SourceUrl = $"{Hf}/{repo}",
            DirectoryName = dir,
            // Verified against sherpa-onnx 1.13.8: the sense_voice recognizer implements only
            // greedy_search and therefore cannot apply model-level hotword boosting.
            SupportsHotwords = false,
            SupportsEnglish = true,
            SupportsChinese = true,
            ApproxSizeBytes = 250_000_000,
            ModelFile = "model.int8.onnx",
            TokensFile = "tokens.txt",
            Notes = "Non-streaming; requires external segmentation (see AudioSegmenter). Higher accuracy "
                + "(punctuated output, ITN) at a similar RTF to the 14M streaming model. No model-level "
                + "hotwords; domain terms go through TextCorrectionEngine instead.",
            Files = new[]
            {
                new ModelFileSpec("model.int8.onnx", true, SourceUrl: Url("model.int8.onnx")),
                new ModelFileSpec("tokens.txt", true, SourceUrl: Url("tokens.txt"))
            }
        };
    }
}
