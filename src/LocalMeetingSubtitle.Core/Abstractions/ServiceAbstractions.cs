using LocalMeetingSubtitle.Core.Models;

namespace LocalMeetingSubtitle.Core.Abstractions;

public sealed record HotwordValidationResult(bool IsValid, string? Message)
{
    public static readonly HotwordValidationResult Ok = new(true, null);
    public static HotwordValidationResult Invalid(string message) => new(false, message);
}

/// <summary>
/// Owns the hotword list and the separate text-correction rules, and produces a
/// sherpa-onnx hotwords file. Model-level boosting and text correction are independent.
/// </summary>
public interface IHotwordService
{
    IReadOnlyList<Hotword> AllHotwords { get; }
    IReadOnlyList<Hotword> ActiveHotwords { get; }
    IReadOnlyList<TextCorrectionRule> ActiveRules { get; }

    /// <summary>Whether model-level boosting is actually in effect for the loaded engine.</summary>
    HotwordMode Mode { get; }
    string? ModeDetail { get; }

    /// <summary>Called by the pipeline once the engine capability is known.</summary>
    void SetEngineCapability(bool supportsModelHotwords, bool modelCanBeRebuiltWithoutDataLoss);

    Task ReloadAsync(CancellationToken cancellationToken = default);

    HotwordValidationResult Validate(string text);

    /// <summary>Writes the model-level hotwords file. Returns null when boosting is unavailable or empty.</summary>
    string? WriteModelHotwordFile(string destinationPath);

    /// <summary>Applies enabled correction rules deterministically. Never a blind global replace.</summary>
    string ApplyCorrections(string text);

    /// <summary>Applies corrections in-place to a segment. Returns true if the text changed.</summary>
    bool ApplyCorrections(SubtitleSegment segment);
}

public sealed record ExportRequest(
    MeetingSession Session,
    IReadOnlyList<SubtitleSegment> Segments,
    ExportFormat Format,
    string OutputPath,
    bool IncludeTimestamps);

public interface ITranscriptFormatter
{
    ExportFormat Format { get; }
    string FormatTranscript(MeetingSession session, IReadOnlyList<SubtitleSegment> segments);
}

public interface ISubtitleExportService
{
    IReadOnlyList<ExportFormat> SupportedFormats { get; }
    string GetExtension(ExportFormat format);
    Task ExportAsync(ExportRequest request, CancellationToken cancellationToken = default);
}

public interface IModelManager
{
    string ModelsRoot { get; }
    IReadOnlyList<ModelDescriptor> Catalog { get; }
    ModelDescriptor? FindById(string id);
    bool IsInstalled(ModelDescriptor descriptor);
    IReadOnlyList<ModelFileSpec> MissingFiles(ModelDescriptor descriptor);
    string GetModelDirectory(ModelDescriptor descriptor);
    Task<ModelInstallResult> EnsureInstalledAsync(ModelDescriptor descriptor, IProgress<double>? progress = null, CancellationToken cancellationToken = default);
}

public sealed record ModelInstallResult(bool Success, string Message);
