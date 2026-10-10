using LocalMeetingSubtitle.Core.Abstractions;
using LocalMeetingSubtitle.Core.Models;
using LocalMeetingSubtitle.Media;
using Xunit.Abstractions;

namespace LocalMeetingSubtitle.IntegrationTests;

/// <summary>
/// V0.5 Phase 4: the decoder can seek (used to resume an interrupted job). The seek must be
/// sample-exact on lossless audio and the emitted block positions must stay absolute on the media
/// timeline, otherwise a resumed run would silently transcribe the wrong audio.
/// </summary>
public sealed class MediaDecodeOffsetTests
{
    private const int Rate = 16000;
    private static readonly TimeSpan Seek = TimeSpan.FromSeconds(2);

    private readonly ITestOutputHelper _output;

    public MediaDecodeOffsetTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task SeekedDecode_IsSampleExactOnLosslessAudio()
    {
        var mediaPath = FindTestMedia("two-speakers.wav");
        Skip.If(mediaPath is null, "testmedia/two-speakers.wav not present.", _output);
        if (mediaPath is null || !TryResolveTools(out var tools))
        {
            return;
        }

        var decoder = new FFmpegMediaDecodeService(tools);
        var full = await DecodeAsync(decoder, new MediaDecodeRequest(mediaPath));
        var seeked = await DecodeAsync(decoder, new MediaDecodeRequest(mediaPath, StartOffset: Seek));

        _output.WriteLine($"full={full.Samples.Count} seeked={seeked.Samples.Count} firstStart={seeked.FirstStart.TotalSeconds:F3}s");

        Assert.InRange(seeked.FirstStart.TotalSeconds, 1.98, 2.02);
        Assert.Equal(full.Samples.Count - (int)(Seek.TotalSeconds * Rate), seeked.Samples.Count);

        // The seeked audio must be the SAME samples as the full decode at those absolute positions.
        int offsetSamples = (int)(Seek.TotalSeconds * Rate);
        int compare = Math.Min(Rate, Math.Min(seeked.Samples.Count, full.Samples.Count - offsetSamples));
        Assert.True(compare > Rate / 2, "there must be enough overlap to compare");

        double mean = 0;
        double worst = 0;
        for (int i = 0; i < compare; i++)
        {
            double diff = Math.Abs(seeked.Samples[i] - full.Samples[offsetSamples + i]);
            mean += diff;
            worst = Math.Max(worst, diff);
        }

        mean /= compare;
        _output.WriteLine($"sample comparison: mean={mean:F8} worst={worst:F8}");
        Assert.True(mean < 1e-6, $"a lossless seek must be sample exact (mean diff {mean:F8})");
    }

    [Fact]
    public async Task SeekedDecode_OnMp3_KeepsAbsolutePositionsAndCoversTheRest()
    {
        // mp3 leaves a decoder delay, so the samples are not bit-identical; the positions must still
        // be absolute and the remaining duration correct.
        var mediaPath = FindTestMedia("0.mp3");
        Skip.If(mediaPath is null, "testmedia/0.mp3 not present.", _output);
        if (mediaPath is null || !TryResolveTools(out var tools))
        {
            return;
        }

        var decoder = new FFmpegMediaDecodeService(tools);
        var full = await DecodeAsync(decoder, new MediaDecodeRequest(mediaPath));
        var seeked = await DecodeAsync(decoder, new MediaDecodeRequest(mediaPath, StartOffset: Seek));

        _output.WriteLine($"full={full.Samples.Count} seeked={seeked.Samples.Count} firstStart={seeked.FirstStart.TotalSeconds:F3}s");

        Assert.True(full.Samples.Count > 3 * Rate, "the fixture must be longer than three seconds");
        Assert.InRange(seeked.FirstStart.TotalSeconds, 1.95, 2.05);

        double expected = full.Samples.Count / (double)Rate - Seek.TotalSeconds;
        double actual = seeked.Samples.Count / (double)Rate;
        Assert.InRange(actual, expected - 0.15, expected + 0.15);
    }

    private bool TryResolveTools(out MediaToolPaths tools)
    {
        try
        {
            tools = FFmpegLocator.Resolve();
            return true;
        }
        catch (MediaDecodeException ex)
        {
            Skip.If(true, "FFmpeg not available: " + ex.Message, _output);
            tools = null!;
            return false;
        }
    }

    private static async Task<(List<float> Samples, TimeSpan FirstStart)> DecodeAsync(
        IMediaDecodeService decoder,
        MediaDecodeRequest request)
    {
        var samples = new List<float>();
        var firstStart = TimeSpan.MinValue;

        await foreach (var block in decoder.DecodeAsync(request))
        {
            if (firstStart < TimeSpan.Zero)
            {
                firstStart = block.Start;
            }

            samples.AddRange(block.Samples);
        }

        return (samples, firstStart == TimeSpan.MinValue ? TimeSpan.MinValue : firstStart);
    }

    private static string? FindTestMedia(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "testmedia", fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        return null;
    }
}
