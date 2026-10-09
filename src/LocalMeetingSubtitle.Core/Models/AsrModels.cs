namespace LocalMeetingSubtitle.Core.Models;

/// <summary>Which sherpa-onnx model family a descriptor describes.</summary>
public enum AsrModelKind
{
    /// <summary>Streaming zipformer transducer (native incremental output, supports hotwords).</summary>
    StreamingTransducer,
    /// <summary>Streaming zipformer CTC / zipformer2-ctc.</summary>
    StreamingCtc,
    /// <summary>Offline (non-streaming) model such as SenseVoice; requires external segmentation.</summary>
    Offline
}

/// <summary>A file that must be present for a model to load.</summary>
public sealed record ModelFileSpec(string RelativePath, bool Required, string? Sha256 = null, long? SizeBytes = null, string? SourceUrl = null);

/// <summary>Declarative description of a downloadable/loadable ASR model.</summary>
public sealed class ModelDescriptor
{
    public string Id { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public AsrModelKind Kind { get; init; }
    public string License { get; init; } = "";
    public string SourceUrl { get; init; } = "";
    /// <summary>Sub-directory (under the models root) that holds this model.</summary>
    public string DirectoryName { get; init; } = "";
    public bool SupportsHotwords { get; init; }
    public bool SupportsEnglish { get; init; }
    public bool SupportsChinese { get; init; } = true;
    public IReadOnlyList<ModelFileSpec> Files { get; init; } = Array.Empty<ModelFileSpec>();
    public long ApproxSizeBytes { get; init; }
    public string Notes { get; init; } = "";

    /// <summary>Relative paths (under <see cref="DirectoryName"/>) of the model files.</summary>
    public string EncoderFile { get; init; } = "";
    public string DecoderFile { get; init; } = "";
    public string JoinerFile { get; init; } = "";
    public string TokensFile { get; init; } = "tokens.txt";
    /// <summary>Single-file offline model (e.g. SenseVoice).</summary>
    public string ModelFile { get; init; } = "";
}

public sealed class AsrEngineOptions
{
    /// <summary>Absolute path to the directory that contains the model files.</summary>
    public string ModelDirectory { get; init; } = "";
    public AsrModelKind Kind { get; init; } = AsrModelKind.StreamingTransducer;

    // File names relative to ModelDirectory (populated from the ModelDescriptor).
    public string EncoderFileName { get; init; } = "encoder.onnx";
    public string DecoderFileName { get; init; } = "decoder.onnx";
    public string JoinerFileName { get; init; } = "joiner.onnx";
    public string TokensFileName { get; init; } = "tokens.txt";
    public string ModelFileName { get; init; } = "model.int8.onnx";
    public string ModelType { get; init; } = "transducer";
    public string DecodingMethod { get; init; } = "greedy_search";
    public int NumThreads { get; init; }
    public string Provider { get; init; } = "cpu";
    public int SampleRate { get; init; } = 16000;
    public int FeatureDim { get; init; } = 80;

    // Endpoint rules (streaming models only).
    public bool EnableEndpoint { get; init; } = true;
    public float Rule1MinTrailingSilence { get; init; } = 2.4f;
    public float Rule2MinTrailingSilence { get; init; } = 1.2f;
    public float Rule3MinUtteranceLength { get; init; } = 20.0f;

    // Model-level hotword boosting.
    public string? HotwordsFile { get; init; }
    public float HotwordsScore { get; init; } = 1.5f;

    // Offline (SenseVoice) options.
    public string Language { get; init; } = "auto";
    public bool UseInverseTextNormalization { get; init; } = true;

    public bool IsOffline => Kind == AsrModelKind.Offline;
}

/// <summary>
/// Policy for the recognizer's CPU thread count.
///
/// sherpa-onnx defaults to <c>ProcessorCount/2</c>, which on a many-core machine spends far more
/// CPU than a small streaming model needs — measured on a 64-logical-CPU host the RTF was ~2x
/// <em>worse</em> at 32 threads (0.100) than at 4 (0.048), with identical output. Capping the
/// automatic count also leaves the UI responsive while transcribing.
/// </summary>
public static class AsrThreadPolicy
{
    /// <summary>Upper bound applied when the user has not chosen a thread count.</summary>
    public const int MaxAutoThreads = 4;

    /// <summary>Uses <paramref name="configured"/> when positive, otherwise half the cores capped.</summary>
    public static int Resolve(int configured) =>
        configured > 0 ? configured : Math.Clamp(Environment.ProcessorCount / 2, 1, MaxAutoThreads);
}

public sealed class AsrCapabilities
{
    public bool Streaming { get; init; }
    public bool ModelLevelHotwords { get; init; }
    public bool TokenTimestamps { get; init; }
    public bool Chinese { get; init; } = true;
    public bool English { get; init; }
    public string Description { get; init; } = "";
}

public enum AsrInitStatus
{
    Success,
    ModelNotFound,
    NativeLibraryMissing,
    InvalidModel,
    Failed
}

public sealed record AsrInitResult(AsrInitStatus Status, string Message, AsrCapabilities? Capabilities = null)
{
    public bool Ok => Status == AsrInitStatus.Success;
}

/// <summary>Result of decoding the current audio buffered in a stream.</summary>
public readonly record struct AsrDecodeResult(
    string Text,
    bool IsEndpoint,
    IReadOnlyList<string>? Tokens = null,
    IReadOnlyList<float>? Timestamps = null);
