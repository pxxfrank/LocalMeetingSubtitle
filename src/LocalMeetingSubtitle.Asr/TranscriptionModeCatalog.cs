using LocalMeetingSubtitle.Core.Abstractions;
using LocalMeetingSubtitle.Core.Models;

namespace LocalMeetingSubtitle.Asr;

/// <summary>
/// A transcription mode resolved to a concrete model + a concrete inference configuration.
/// The three modes differ on real axes (model family, decoding method, hotword boosting, segmentation),
/// never on their label alone.
/// </summary>
public sealed record ResolvedTranscriptionMode(
    TranscriptionMode Mode,
    string DisplayName,
    string Description,
    ModelDescriptor Descriptor,
    AsrEngineOptions EngineOptions,
    OfflineTranscriptionOptions TranscriptionOptions,
    bool IsAvailable,
    string? UnavailableReason);

/// <summary>
/// Maps the user-facing 快速/标准/高精度 modes onto the model catalog. A mode whose model is not
/// installed is still returned (with <see cref="ResolvedTranscriptionMode.IsAvailable"/> false and a
/// reason) so the UI can disable it instead of failing at start time.
/// </summary>
public static class TranscriptionModeCatalog
{
    private const string FastModelId = "streaming-zipformer-zh-14M";
    private const string HighAccuracyModelId = "sense-voice-small-int8";

    public static IReadOnlyList<TranscriptionMode> All { get; } = new[]
    {
        TranscriptionMode.Fast,
        TranscriptionMode.Standard,
        TranscriptionMode.HighAccuracy
    };

    public static ResolvedTranscriptionMode Resolve(
        TranscriptionMode mode,
        IModelManager modelManager,
        string? hotwordsFile = null,
        int numThreads = 0)
    {
        var descriptor = mode switch
        {
            TranscriptionMode.Fast => Require(modelManager, FastModelId),
            TranscriptionMode.Standard => Require(modelManager, FastModelId),
            TranscriptionMode.HighAccuracy => Require(modelManager, HighAccuracyModelId),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown transcription mode.")
        };

        var spec = mode switch
        {
            TranscriptionMode.Fast => BuildFast(descriptor, modelManager, numThreads),
            TranscriptionMode.Standard => BuildStandard(descriptor, modelManager, hotwordsFile, numThreads),
            _ => BuildHighAccuracy(descriptor, modelManager, numThreads)
        };

        bool installed = modelManager.IsInstalled(descriptor);
        return spec with
        {
            IsAvailable = installed,
            UnavailableReason = installed ? null : BuildMissingMessage(modelManager, descriptor)
        };
    }

    private static ResolvedTranscriptionMode BuildFast(ModelDescriptor descriptor, IModelManager modelManager, int numThreads) =>
        new(
            TranscriptionMode.Fast,
            "快速",
            "最小流式模型 + 贪心解码，速度最快，适合快速出草稿。",
            descriptor,
            AsrOptionsFactory.FromDescriptor(descriptor, modelManager.ModelsRoot, numThreads: numThreads),
            BuildOptions(descriptor, maxSegmentSeconds: 15.0, overlapSeconds: 0.0, appendTerminalPunctuation: true),
            false,
            null);

    private static ResolvedTranscriptionMode BuildStandard(
        ModelDescriptor descriptor, IModelManager modelManager, string? hotwordsFile, int numThreads) =>
        new(
            TranscriptionMode.Standard,
            "标准",
            "流式模型 + 束搜索 + 热词增强，在速度与准确率之间取平衡。",
            descriptor,
            AsrOptionsFactory.FromDescriptor(
                descriptor,
                modelManager.ModelsRoot,
                hotwordsFile: hotwordsFile,
                numThreads: numThreads,
                decodingMethod: "modified_beam_search"),
            BuildOptions(descriptor, maxSegmentSeconds: 20.0, overlapSeconds: 0.0, appendTerminalPunctuation: true),
            false,
            null);

    private static ResolvedTranscriptionMode BuildHighAccuracy(ModelDescriptor descriptor, IModelManager modelManager, int numThreads) =>
        new(
            TranscriptionMode.HighAccuracy,
            "高精度",
            "离线 SenseVoice 模型 + 重叠分段，准确率最高，速度最慢。",
            descriptor,
            AsrOptionsFactory.FromDescriptor(
                descriptor,
                modelManager.ModelsRoot,
                numThreads: numThreads,
                language: "auto",
                useInverseTextNormalization: true),
            BuildOptions(descriptor, maxSegmentSeconds: 30.0, overlapSeconds: 1.5, appendTerminalPunctuation: false),
            false,
            null);

    private static OfflineTranscriptionOptions BuildOptions(
        ModelDescriptor descriptor,
        double maxSegmentSeconds,
        double overlapSeconds,
        bool appendTerminalPunctuation) =>
        new()
        {
            MaxSegmentSeconds = maxSegmentSeconds,
            OverlapSeconds = overlapSeconds,
            AppendTerminalPunctuation = appendTerminalPunctuation,
            ModelId = descriptor.Id
        };

    private static ModelDescriptor Require(IModelManager modelManager, string id) =>
        modelManager.FindById(id)
        ?? throw new InvalidOperationException($"Model '{id}' is not present in the catalog.");

    private static string BuildMissingMessage(IModelManager modelManager, ModelDescriptor descriptor)
    {
        var missing = modelManager.MissingFiles(descriptor);
        return missing.Count == 0
            ? $"该模式所需的模型 {descriptor.Id} 尚未安装。"
            : $"该模式所需的模型 {descriptor.Id} 尚未安装（缺少 {missing.Count} 个文件）。";
    }
}
