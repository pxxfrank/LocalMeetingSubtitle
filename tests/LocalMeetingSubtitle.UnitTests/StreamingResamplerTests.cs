using LocalMeetingSubtitle.Core.Audio;

namespace LocalMeetingSubtitle.UnitTests;

public sealed class StreamingResamplerTests
{
    private static float[] Sine(double frequencyHz, int sampleRate, double seconds, double amplitude = 1.0)
    {
        int count = (int)(sampleRate * seconds);
        var data = new float[count];
        for (int i = 0; i < count; i++)
        {
            data[i] = (float)(amplitude * Math.Sin(2.0 * Math.PI * frequencyHz * i / sampleRate));
        }
        return data;
    }

    private static double EstimateFrequency(ReadOnlySpan<float> samples, int sampleRate)
    {
        int start = Math.Min(1000, samples.Length / 4);
        int end = samples.Length - start;
        int crossings = 0;
        for (int i = start + 1; i < end; i++)
        {
            if ((samples[i - 1] <= 0f && samples[i] > 0f) || (samples[i - 1] >= 0f && samples[i] < 0f))
            {
                crossings++;
            }
        }

        double seconds = (end - start) / (double)sampleRate;
        return crossings / 2.0 / seconds;
    }

    [Theory]
    [InlineData(48000)]
    [InlineData(44100)]
    public void Downsample_KeepsFrequencyAndAmplitude(int inputRate)
    {
        var input = Sine(1000, inputRate, 1.0);
        var resampler = new StreamingResampler(inputRate, 16000);

        var head = resampler.Process(input);
        var output = Concat(head, resampler.Flush());

        Assert.InRange(output.Length, 15900, 16100);

        double frequency = EstimateFrequency(output, 16000);
        Assert.InRange(frequency, 950, 1050);

        double peak = 0;
        int from = Math.Min(1000, output.Length / 4);
        for (int i = from; i < output.Length - from; i++)
        {
            peak = Math.Max(peak, Math.Abs(output[i]));
        }
        Assert.InRange(peak, 0.9, 1.1);
    }

    [Fact]
    public void BlockSplitting_MatchesOneShotLengthAndContent()
    {
        const int inputRate = 48000;
        var input = Sine(1000, inputRate, 1.0);

        var oneShotResampler = new StreamingResampler(inputRate, 16000);
        var oneShot = oneShotResampler.Process(input);

        var blockedResampler = new StreamingResampler(inputRate, 16000);
        var blocked = new List<float>();
        const int block = 1000;
        for (int offset = 0; offset < input.Length; offset += block)
        {
            int length = Math.Min(block, input.Length - offset);
            blocked.AddRange(blockedResampler.Process(input.AsSpan(offset, length)));
        }

        var blockedArray = blocked.ToArray();

        // Guard against a trivially "equal" result (both empty) hiding a broken resampler.
        Assert.True(oneShot.Length > 0,
            "resampler produced no output at all (expected ~16000 samples for 1 s at 48k->16k).");

        Assert.True(Math.Abs(oneShot.Length - blockedArray.Length) <= 1,
            $"one-shot produced {oneShot.Length} samples, blocked produced {blockedArray.Length}.");

        int common = Math.Min(oneShot.Length, blockedArray.Length);
        for (int i = 0; i < common; i++)
        {
            Assert.True(Math.Abs(oneShot[i] - blockedArray[i]) < 1e-5f,
                $"sample {i} differs: {oneShot[i]} vs {blockedArray[i]}.");
        }
    }

    [Fact]
    public void EqualRates_IsPassthrough()
    {
        var resampler = new StreamingResampler(16000, 16000);

        Assert.True(resampler.IsPassthrough);

        var input = new[] { 0.1f, -0.2f, 0.3f, -0.4f, 0.5f };
        var output = resampler.Process(input);

        Assert.Equal(input, output);
    }

    [Fact]
    public void NonPositiveRates_AreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new StreamingResampler(0, 16000));
        Assert.Throws<ArgumentOutOfRangeException>(() => new StreamingResampler(48000, 0));
    }

    private static float[] Concat(float[] a, float[] b)
    {
        var result = new float[a.Length + b.Length];
        a.CopyTo(result, 0);
        b.CopyTo(result, a.Length);
        return result;
    }
}
