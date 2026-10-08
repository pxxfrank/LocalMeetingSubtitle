using LocalMeetingSubtitle.Core.Abstractions;
using LocalMeetingSubtitle.Core.Models;

namespace LocalMeetingSubtitle.Core.Audio;

/// <summary>
/// Capsule pipeline stage: downmix to mono, then resample to the engine's rate (16 kHz default).
/// Handles arbitrary capture formats — the rate is never assumed to already be 16 kHz.
/// </summary>
public sealed class DefaultAudioPreprocessor : IAudioPreprocessor
{
    private StreamingResampler _resampler;

    public DefaultAudioPreprocessor(int targetSampleRate = 16000)
    {
        TargetFormat = AudioFormat.Float32(targetSampleRate, 1);
        _resampler = new StreamingResampler(targetSampleRate, targetSampleRate);
    }

    public AudioFormat TargetFormat { get; }

    public float[] Process(ReadOnlySpan<float> interleaved, AudioFormat sourceFormat)
    {
        if (!sourceFormat.IsFloat)
        {
            throw new NotSupportedException($"Source format must be float32, got {sourceFormat}.");
        }

        var mono = ChannelConverter.ToMono(interleaved, sourceFormat.Channels);

        if (sourceFormat.SampleRate != TargetFormat.SampleRate)
        {
            if (_resampler.InputRate != sourceFormat.SampleRate)
            {
                _resampler = new StreamingResampler(sourceFormat.SampleRate, TargetFormat.SampleRate);
            }
            return _resampler.Process(mono);
        }

        return mono;
    }

    public void Reset() => _resampler.Reset();
}
