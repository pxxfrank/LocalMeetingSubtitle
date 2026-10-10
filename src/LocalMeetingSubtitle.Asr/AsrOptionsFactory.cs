using LocalMeetingSubtitle.Core.Models;

namespace LocalMeetingSubtitle.Asr;

/// <summary>Builds engine options from a catalog descriptor + models root.</summary>
public static class AsrOptionsFactory
{
    public static string GetModelDirectory(string modelsRoot, ModelDescriptor descriptor)
        => Path.Combine(modelsRoot, descriptor.DirectoryName);

    public static AsrEngineOptions FromDescriptor(
        ModelDescriptor descriptor,
        string modelsRoot,
        string? hotwordsFile = null,
        int numThreads = 0,
        float hotwordsScore = 1.5f,
        string? decodingMethod = null,
        string? language = null,
        bool? useInverseTextNormalization = null)
    {
        return new AsrEngineOptions
        {
            ModelDirectory = GetModelDirectory(modelsRoot, descriptor),
            Kind = descriptor.Kind,
            EncoderFileName = descriptor.EncoderFile,
            DecoderFileName = descriptor.DecoderFile,
            JoinerFileName = descriptor.JoinerFile,
            TokensFileName = descriptor.TokensFile,
            ModelFileName = descriptor.ModelFile,
            NumThreads = AsrThreadPolicy.Resolve(numThreads),
            Provider = "cpu",
            HotwordsFile = hotwordsFile,
            HotwordsScore = hotwordsScore,
            DecodingMethod = decodingMethod ?? "greedy_search",
            Language = language ?? "auto",
            UseInverseTextNormalization = useInverseTextNormalization ?? true
        };
    }
}
