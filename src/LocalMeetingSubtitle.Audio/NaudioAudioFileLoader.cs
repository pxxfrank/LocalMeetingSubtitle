using LocalMeetingSubtitle.Core.Abstractions;
using LocalMeetingSubtitle.Core.Audio;
using LocalMeetingSubtitle.Core.Models;
using NAudio.Wave;

namespace LocalMeetingSubtitle.Audio;

/// <summary>
/// Decodes a local audio file to mono float32 at the target rate using NAudio's <see cref="AudioFileReader"/>
/// (which decodes every format Media Foundation / NAudio supports) followed by the same
/// downmix + resample preprocessor the live pipeline uses.
/// </summary>
public sealed class NaudioAudioFileLoader : IAudioFileLoader
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".wav", ".mp3", ".flac", ".m4a", ".aac", ".wma", ".aiff", ".aif", ".ogg"
    };

    public bool IsSupported(string path) =>
        !string.IsNullOrWhiteSpace(path) && SupportedExtensions.Contains(Path.GetExtension(path));

    public TimeSpan GetDuration(string path)
    {
        using var reader = new AudioFileReader(path);
        return reader.TotalTime;
    }

    public float[] LoadMono(string path, int targetSampleRate, out int sampleRate)
    {
        if (targetSampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetSampleRate));
        }

        using var reader = new AudioFileReader(path);

        // AudioFileReader always yields 32-bit float; keep the file's own rate/channels for the preprocessor.
        var sourceFormat = new AudioFormat(reader.WaveFormat.SampleRate, reader.WaveFormat.Channels, 32);
        var preprocessor = new DefaultAudioPreprocessor(targetSampleRate);

        var samples = new List<float>();
        var buffer = new float[Math.Max(sourceFormat.SampleRate * sourceFormat.Channels, 4096)];
        int read;
        while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            var mono = preprocessor.Process(buffer.AsSpan(0, read), sourceFormat);
            if (mono.Length > 0)
            {
                samples.AddRange(mono);
            }
        }

        sampleRate = targetSampleRate;
        return samples.ToArray();
    }
}
