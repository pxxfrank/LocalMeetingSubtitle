using System.Text;

namespace LocalMeetingSubtitle.Core.Audio;

/// <summary>
/// Writes a canonical 16-bit PCM mono/stereo WAV incrementally and patches the RIFF/data sizes on
/// close.
///
/// This is deliberately a <b>synchronous</b> writer, unlike the live-capture
/// <c>WaveRecordingService</c>, which uses a bounded channel so the capture thread never blocks. A
/// file job is pull-based: it already owns back-pressure, and a dropped frame here would silently
/// shift the whole diarization timeline, so every sample is written.
/// </summary>
public sealed class PcmWavWriter : IDisposable
{
    private const int HeaderBytes = 44;
    private readonly FileStream _stream;
    private readonly BinaryWriter _writer;
    private readonly int _sampleRate;
    private readonly int _channels;
    private bool _disposed;

    public PcmWavWriter(string path, int sampleRate, int channels = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfLessThan(sampleRate, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(channels, 1);

        _sampleRate = sampleRate;
        _channels = channels;
        _stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
        _writer = new BinaryWriter(_stream, Encoding.ASCII, leaveOpen: true);
        WriteHeader(0);
    }

    /// <summary>Frames (one frame = one sample per channel) written so far.</summary>
    public long FrameCount { get; private set; }

    public TimeSpan Duration => TimeSpan.FromSeconds(FrameCount / (double)_sampleRate);

    /// <summary>Appends normalised float samples, clamped to the 16-bit range.</summary>
    public void Write(ReadOnlySpan<float> samples)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        int frames = samples.Length / _channels;
        for (int i = 0; i < frames * _channels; i++)
        {
            _writer.Write((short)Math.Clamp(samples[i] * 32767f, -32768f, 32767f));
        }

        FrameCount += frames;
    }

    /// <summary>Finalises the header so the file is a valid WAV.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            long dataBytes = FrameCount * _channels * 2;
            _stream.Seek(4, SeekOrigin.Begin);
            _writer.Write((int)(36 + dataBytes));
            _stream.Seek(HeaderBytes - 4, SeekOrigin.Begin);
            _writer.Write((int)dataBytes);
            _writer.Flush();
        }
        finally
        {
            _writer.Dispose();
            _stream.Dispose();
        }
    }

    private void WriteHeader(int dataBytes)
    {
        int blockAlign = _channels * 2;
        _writer.Write("RIFF"u8.ToArray());
        _writer.Write(36 + dataBytes);
        _writer.Write("WAVE"u8.ToArray());
        _writer.Write("fmt "u8.ToArray());
        _writer.Write(16);
        _writer.Write((short)1);                       // PCM
        _writer.Write((short)_channels);
        _writer.Write(_sampleRate);
        _writer.Write(_sampleRate * blockAlign);       // byte rate
        _writer.Write((short)blockAlign);
        _writer.Write((short)16);                      // bits per sample
        _writer.Write("data"u8.ToArray());
        _writer.Write(dataBytes);
    }
}
