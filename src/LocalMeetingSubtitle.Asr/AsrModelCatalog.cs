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

    /// <summary>
    /// The descriptors a user may select for the live path. Offline (SenseVoice) models are excluded
    /// deliberately: the live offline path still emits zero-duration segments (see KNOWN_ISSUES), so
    /// they stay reachable only through file transcription.
    /// </summary>
    public static IReadOnlyList<ModelDescriptor> StreamingModels { get; } =
        All.Where(m => m.Kind == AsrModelKind.StreamingTransducer).ToArray();

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
            ApproxSizeBytes = 25_354_625,
            EncoderFile = "encoder-epoch-99-avg-1.int8.onnx",
            DecoderFile = "decoder-epoch-99-avg-1.int8.onnx",
            JoinerFile = "joiner-epoch-99-avg-1.int8.onnx",
            TokensFile = "tokens.txt",
            Notes = "Native streaming output; hotwords supported via modified_beam_search. Small (14M params).",
            Files = new[]
            {
                new ModelFileSpec("encoder-epoch-99-avg-1.int8.onnx", true, SizeBytes: 21_621_684, SourceUrl: Url("encoder-epoch-99-avg-1.int8.onnx")),
                new ModelFileSpec("decoder-epoch-99-avg-1.int8.onnx", true, SizeBytes: 1_888_682, SourceUrl: Url("decoder-epoch-99-avg-1.int8.onnx")),
                new ModelFileSpec("joiner-epoch-99-avg-1.int8.onnx", true, SizeBytes: 1_795_562, SourceUrl: Url("joiner-epoch-99-avg-1.int8.onnx")),
                new ModelFileSpec("tokens.txt", true, SizeBytes: 48_697, SourceUrl: Url("tokens.txt"))
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
            // Measured on sherpa-onnx 1.13.8 (docs/MODEL_SELECTION.md): model-level hotword boosting
            // has no usable effect on this model. With identical modified_beam_search config the output
            // was byte-identical at hotwords-score 1.5, 4.0 and 20, while the same file measurably
            // steered the zh-14M model (生疼→升腾). Its tokens.txt also has no whole-word English
            // tokens (English is BPE subwords such as ▁TH + DAY + ING), so ASCII domain terms cannot be
            // boosted at all. Domain terms reach this model through the correction rules instead, which
            // is exactly what SupportsHotwords = false makes the pipeline do.
            SupportsHotwords = false,
            SupportsEnglish = true,
            SupportsChinese = true,
            ApproxSizeBytes = 198_270_793,
            EncoderFile = "encoder-epoch-99-avg-1.int8.onnx",
            DecoderFile = "decoder-epoch-99-avg-1.int8.onnx",
            JoinerFile = "joiner-epoch-99-avg-1.int8.onnx",
            TokensFile = "tokens.txt",
            Notes = "Streaming bilingual zh/en; recognizes English and Chinese/English code-switching that "
                + "the 14M model garbles. Larger and ~2-3x slower; Chinese may repeat characters. No "
                + "model-level hotwords (measured ineffective) — domain terms go through TextCorrectionEngine.",
            Files = new[]
            {
                new ModelFileSpec("encoder-epoch-99-avg-1.int8.onnx", true, SizeBytes: 181_895_032, SourceUrl: Url("encoder-epoch-99-avg-1.int8.onnx")),
                new ModelFileSpec("decoder-epoch-99-avg-1.int8.onnx", true, SizeBytes: 13_091_040, SourceUrl: Url("decoder-epoch-99-avg-1.int8.onnx")),
                new ModelFileSpec("joiner-epoch-99-avg-1.int8.onnx", true, SizeBytes: 3_228_404, SourceUrl: Url("joiner-epoch-99-avg-1.int8.onnx")),
                new ModelFileSpec("tokens.txt", true, SizeBytes: 56_317, SourceUrl: Url("tokens.txt"))
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
            ApproxSizeBytes = 239_549_735,
            ModelFile = "model.int8.onnx",
            TokensFile = "tokens.txt",
            Notes = "Non-streaming; requires external segmentation (see AudioSegmenter). Higher accuracy "
                + "(punctuated output, ITN) at a similar RTF to the 14M streaming model. No model-level "
                + "hotwords; domain terms go through TextCorrectionEngine instead.",
            Files = new[]
            {
                new ModelFileSpec("model.int8.onnx", true, SizeBytes: 239_233_841, SourceUrl: Url("model.int8.onnx")),
                new ModelFileSpec("tokens.txt", true, SizeBytes: 315_894, SourceUrl: Url("tokens.txt"))
            }
        };
    }
}
