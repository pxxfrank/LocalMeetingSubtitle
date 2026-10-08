using LocalMeetingSubtitle.Core.Audio;
using LocalMeetingSubtitle.Core.Models;

namespace LocalMeetingSubtitle.UnitTests;

public sealed class ChannelConverterTests
{
    [Fact]
    public void Stereo_IsDownmixedByAveraging()
    {
        var interleaved = new[] { 0.5f, 1.0f, -1.0f, 0.0f, 0.25f, 0.75f };

        var mono = ChannelConverter.ToMono(interleaved, channels: 2);

        Assert.Equal(3, mono.Length);
        Assert.Equal(0.75f, mono[0], 5);
        Assert.Equal(-0.5f, mono[1], 5);
        Assert.Equal(0.5f, mono[2], 5);
    }

    [Fact]
    public void MultiChannel_IsDownmixedByAveraging()
    {
        var interleaved = new[] { 0.1f, 0.2f, 0.3f, 0.4f, 1.0f, -1.0f, 0.0f, 0.0f };

        var mono = ChannelConverter.ToMono(interleaved, channels: 4);

        Assert.Equal(2, mono.Length);
        Assert.Equal(0.25f, mono[0], 5);
        Assert.Equal(0.0f, mono[1], 5);
    }

    [Fact]
    public void Mono_IsReturnedUnchanged()
    {
        var samples = new[] { 0.1f, 0.2f, 0.3f };

        var mono = ChannelConverter.ToMono(samples, channels: 1);

        Assert.Equal(samples, mono);
    }

    [Fact]
    public void Mono_FromAudioFormat_UsesChannelCount()
    {
        var interleaved = new[] { 1.0f, 0.0f };
        var format = AudioFormat.Float32(48000, 2);

        var mono = ChannelConverter.ToMono(interleaved, format);

        Assert.Single(mono);
        Assert.Equal(0.5f, mono[0], 5);
    }

    [Fact]
    public void Int16ToFloat_NormalizesToUnitRange()
    {
        short[] input = { 0, 32767, -32768, 16384 };

        var output = ChannelConverter.Int16ToFloat(input);

        Assert.Equal(0f, output[0], 5);
        Assert.Equal(32767f / 32768f, output[1], 5);
        Assert.Equal(-1f, output[2], 5);
        Assert.Equal(0.5f, output[3], 5);
    }

    [Fact]
    public void Int32ToFloat_NormalizesToUnitRange()
    {
        int[] input = { 0, int.MaxValue, int.MinValue, int.MaxValue / 2 };

        var output = ChannelConverter.Int32ToFloat(input);

        Assert.Equal(0f, output[0], 5);
        Assert.InRange(output[1], 0.999999f, 1.0f);
        Assert.Equal(-1f, output[2], 5);
        Assert.Equal(0.5f, output[3], 5);
    }
}
