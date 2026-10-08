using System.Diagnostics;
using LocalMeetingSubtitle.Core.Audio;
using LocalMeetingSubtitle.Core.Models;
using Xunit.Abstractions;

namespace LocalMeetingSubtitle.PerformanceTests;

[Trait("Category", "Performance")]
public sealed class ResamplerThroughputTests
{
    private readonly ITestOutputHelper _output;

    public ResamplerThroughputTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Resampler_ProcessesSixtySecondsOfStereo48k_MuchFasterThanRealTime()
    {
        const int seconds = 60;
        const int inputRate = 48000;
        const int outputRate = 16000;

        var preprocessor = new DefaultAudioPreprocessor(outputRate);
        var format = AudioFormat.Float32(inputRate, 2);

        // A constant 440 Hz tone on both channels keeps the resampler busy.
        const int framesPerBlock = 4800; // 100 ms
        var block = new float[framesPerBlock * 2];
        for (int i = 0; i < framesPerBlock; i++)
        {
            float v = (float)(0.5 * Math.Sin(2.0 * Math.PI * 440.0 * i / inputRate));
            block[2 * i] = v;
            block[2 * i + 1] = v;
        }

        int blocks = seconds * 10;
        long produced = 0;

        var stopwatch = Stopwatch.StartNew();
        for (int b = 0; b < blocks; b++)
        {
            produced += preprocessor.Process(block, format).Length;
        }
        stopwatch.Stop();

        double realtimeFactor = stopwatch.Elapsed.TotalSeconds / seconds;
        double speed = seconds / Math.Max(stopwatch.Elapsed.TotalSeconds, 1e-9);
        long expected = (long)seconds * outputRate;

        _output.WriteLine($"input=60s stereo 48kHz -> 16kHz mono");
        _output.WriteLine($"process_time={stopwatch.Elapsed.TotalSeconds:F3}s rtf={realtimeFactor:F4} speed={speed:F1}x realtime");
        _output.WriteLine($"output_samples={produced} (expected ~{expected})");

        Assert.True(realtimeFactor < 0.5,
            $"RTF {realtimeFactor:F3} is not far below real time.");

        // A resampler that produces no samples is not "fast", it is broken.
        Assert.InRange(produced, expected - expected / 20, expected + expected / 20);
    }
}
