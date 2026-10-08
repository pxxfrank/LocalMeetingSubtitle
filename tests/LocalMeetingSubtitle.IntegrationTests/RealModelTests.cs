using LocalMeetingSubtitle.Asr;
using LocalMeetingSubtitle.Core.Models;
using Xunit.Abstractions;

namespace LocalMeetingSubtitle.IntegrationTests;

public sealed class RealModelTests
{
    private readonly ITestOutputHelper _output;

    public RealModelTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task RealStreamingModel_DecodesChineseFromTestWav()
    {
        var modelsRoot = ModelLocator.FindModelsRoot();
        var modelDirectory = modelsRoot is null
            ? null
            : Path.Combine(modelsRoot, AsrModelCatalog.StreamingZipformerZh14M.DirectoryName);
        var wavPath = modelDirectory is null
            ? null
            : Path.Combine(modelDirectory, "test_wavs", "0.wav");

        Skip.If(wavPath is null || !File.Exists(wavPath),
            "test_wavs/0.wav not present for the streaming zh-14M model.", _output);
        if (wavPath is null || !File.Exists(wavPath))
        {
            return;
        }

        var (samples, sampleRate) = WavReader.ReadMono16BitPcm(wavPath);
        Assert.Equal(16000, sampleRate);

        var options = AsrOptionsFactory.FromDescriptor(AsrModelCatalog.StreamingZipformerZh14M, modelsRoot!);
        using var engine = new SherpaOnnxAsrEngine();
        var init = await engine.InitializeAsync(options);
        Assert.True(init.Ok, init.Message);

        using var session = engine.CreateSession();

        const int chunkSize = 1600; // 100 ms
        string lastNonEmpty = "";
        for (int offset = 0; offset < samples.Length; offset += chunkSize)
        {
            int length = Math.Min(chunkSize, samples.Length - offset);
            session.AcceptWaveform(samples.AsSpan(offset, length), sampleRate);
            while (session.IsReady())
            {
                session.Decode();
            }

            var partial = session.GetResult();
            if (!string.IsNullOrWhiteSpace(partial.Text))
            {
                lastNonEmpty = partial.Text;
            }

            if (partial.IsEndpoint)
            {
                session.Reset();
            }
        }

        session.InputFinished();
        while (session.IsReady())
        {
            session.Decode();
        }

        var finalResult = session.GetResult();
        var text = !string.IsNullOrWhiteSpace(finalResult.Text) ? finalResult.Text : lastNonEmpty;
        _output.WriteLine($"decoded: \"{text}\"");

        Assert.False(string.IsNullOrWhiteSpace(text), "Real model returned no text.");
        Assert.Contains(text, IsCjk);
    }

    private static bool IsCjk(char c) =>
        (c >= 0x4E00 && c <= 0x9FFF) ||
        (c >= 0x3400 && c <= 0x4DBF) ||
        (c >= 0xF900 && c <= 0xFAFF);
}
