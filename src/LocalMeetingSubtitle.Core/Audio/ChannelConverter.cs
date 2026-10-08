using LocalMeetingSubtitle.Core.Models;

namespace LocalMeetingSubtitle.Core.Audio;

/// <summary>Downmixes interleaved PCM to mono. Capture formats are frequently stereo.</summary>
public static class ChannelConverter
{
    public static float[] ToMono(ReadOnlySpan<float> interleaved, int channels)
    {
        if (channels <= 1)
        {
            return interleaved.ToArray();
        }

        int frames = interleaved.Length / channels;
        var mono = new float[frames];
        if (channels == 2)
        {
            for (int i = 0, s = 0; i < frames; i++, s += 2)
            {
                mono[i] = (interleaved[s] + interleaved[s + 1]) * 0.5f;
            }
        }
        else
        {
            float inv = 1f / channels;
            for (int i = 0, s = 0; i < frames; i++, s += channels)
            {
                float sum = 0;
                for (int c = 0; c < channels; c++) sum += interleaved[s + c];
                mono[i] = sum * inv;
            }
        }
        return mono;
    }

    public static float[] ToMono(ReadOnlySpan<float> interleaved, AudioFormat format) => ToMono(interleaved, format.Channels);

    /// <summary>Converts interleaved 16-bit PCM to normalized float32.</summary>
    public static float[] Int16ToFloat(ReadOnlySpan<short> samples)
    {
        var output = new float[samples.Length];
        for (int i = 0; i < samples.Length; i++) output[i] = samples[i] / 32768f;
        return output;
    }

    /// <summary>Converts interleaved 32-bit integer PCM to normalized float32.</summary>
    public static float[] Int32ToFloat(ReadOnlySpan<int> samples)
    {
        var output = new float[samples.Length];
        const float scale = 1f / 2147483648f;
        for (int i = 0; i < samples.Length; i++) output[i] = samples[i] * scale;
        return output;
    }
}
