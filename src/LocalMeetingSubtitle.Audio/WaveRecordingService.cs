using System.Threading.Channels;
using LocalMeetingSubtitle.Core.Abstractions;
using LocalMeetingSubtitle.Core.Audio;
using LocalMeetingSubtitle.Core.Models;
using NAudio.Wave;

namespace LocalMeetingSubtitle.Audio;

/// <summary>
/// Writes the optionally-recorded meeting audio to a 16 kHz mono 16-bit WAV. Captured frames are
/// pushed onto a bounded channel by <see cref="Write"/> (a copy, non-blocking — it never stalls the
/// capture thread) and drained by a single background writer task that downmixes/resamples via
/// <see cref="DefaultAudioPreprocessor"/>. Frames are dropped when the writer falls behind.
/// </summary>
public sealed class WaveRecordingService : IRecordingService
{
    private const int OutputSampleRate = 16000;
    private const int OutputChannels = 1;
    private const int OutputBitsPerSample = 16;

    /// <summary>Roughly 256 capture buffers (~a few seconds) before frames start being dropped.</summary>
    private const int QueueCapacity = 256;

    private readonly IAppLogger? _log;

    private Channel<(float[] Frames, AudioFormat Format)>? _queue;
    private Task<long>? _writerTask;
    private string? _path;
    private long _writtenBytes;
    private long _droppedFrames;
    private volatile bool _recording;

    public WaveRecordingService(IAppLogger? log = null) => _log = log;

    public bool IsRecording => _recording;

    public string? CurrentPath => _path;

    public long WrittenBytes => _writtenBytes;

    public Task StartAsync(string outputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        if (_recording)
        {
            throw new InvalidOperationException("A recording is already in progress.");
        }

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // A fresh bounded channel per run keeps the service reusable across sessions.
        var queue = Channel.CreateBounded<(float[] Frames, AudioFormat Format)>(
            new BoundedChannelOptions(QueueCapacity)
            {
                SingleReader = true,
                SingleWriter = false
                // Default FullMode = Wait: TryWrite returns false (instead of blocking) when full.
            });

        _queue = queue;
        _path = outputPath;
        _writtenBytes = 0;
        _droppedFrames = 0;
        _recording = true;
        _writerTask = Task.Run(() => WriteLoopAsync(queue.Reader, outputPath));

        _log?.Info($"Recording started → {outputPath}");
        return Task.CompletedTask;
    }

    public void Write(float[] interleaved, AudioFormat format)
    {
        if (!_recording || interleaved.Length == 0)
        {
            return;
        }

        var queue = _queue;
        if (queue is null)
        {
            return;
        }

        // Copy so the caller's buffer can be reused immediately; never block the capture thread.
        var copy = (float[])interleaved.Clone();
        if (!queue.Writer.TryWrite((copy, format)))
        {
            Interlocked.Increment(ref _droppedFrames);
        }
    }

    public async Task<RecordingResult> StopAsync()
    {
        var path = _path ?? "";
        var queue = _queue;
        var task = _writerTask;
        _queue = null;
        _writerTask = null;

        if (!_recording || queue is null)
        {
            return new RecordingResult(path, TimeSpan.Zero, GetFileSize(path), OutputSampleRate, OutputChannels);
        }

        _recording = false;
        queue.Writer.TryComplete();

        long samples = 0;
        if (task is not null)
        {
            try
            {
                samples = await task.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log?.Warn($"Recording writer failed: {ex.Message}");
            }
        }

        var dropped = Interlocked.Read(ref _droppedFrames);
        if (dropped > 0)
        {
            _log?.Warn($"Recording dropped {dropped} frame buffer(s); the writer could not keep up.");
        }

        var duration = TimeSpan.FromSeconds(samples / (double)OutputSampleRate);
        var size = GetFileSize(path);
        _log?.Info($"Recording stopped → {path} ({duration.TotalSeconds:0.0}s, {size} bytes).");
        return new RecordingResult(path, duration, size, OutputSampleRate, OutputChannels);
    }

    private async Task<long> WriteLoopAsync(ChannelReader<(float[] Frames, AudioFormat Format)> reader, string path)
    {
        var preprocessor = new DefaultAudioPreprocessor(OutputSampleRate);
        var writer = new WaveFileWriter(path, new WaveFormat(OutputSampleRate, OutputBitsPerSample, OutputChannels));
        long samples = 0;

        try
        {
            await foreach (var (frames, format) in reader.ReadAllAsync().ConfigureAwait(false))
            {
                float[] mono;
                try
                {
                    mono = preprocessor.Process(frames, format);
                }
                catch (Exception ex)
                {
                    _log?.Warn($"Recording skipped a buffer that could not be converted: {ex.Message}");
                    continue;
                }

                if (mono.Length == 0)
                {
                    continue;
                }

                var pcm = ToPcm16(mono);
                writer.Write(pcm, 0, pcm.Length);
                samples += mono.Length;
                _writtenBytes = writer.Length;
            }

            writer.Flush();
        }
        finally
        {
            writer.Dispose();
        }

        return samples;
    }

    private long GetFileSize(string path)
    {
        try
        {
            return File.Exists(path) ? new FileInfo(path).Length : _writtenBytes;
        }
        catch (IOException)
        {
            return _writtenBytes;
        }
    }

    /// <summary>Converts normalized float32 samples to little-endian 16-bit PCM (the probe's pattern).</summary>
    private static byte[] ToPcm16(float[] samples)
    {
        var bytes = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            int v = (int)Math.Round(samples[i] * 32767.0);
            if (v > 32767) v = 32767;
            else if (v < -32768) v = -32768;
            bytes[i * 2] = (byte)(v & 0xFF);
            bytes[i * 2 + 1] = (byte)((v >> 8) & 0xFF);
        }
        return bytes;
    }

    public async ValueTask DisposeAsync()
    {
        if (_recording)
        {
            try
            {
                await StopAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log?.Warn($"Disposing the recorder failed: {ex.Message}");
            }
        }
    }
}
