using LocalMeetingSubtitle.Core.Models;

namespace LocalMeetingSubtitle.Core.Abstractions;

public sealed class AudioFramesEventArgs : EventArgs
{
    /// <summary>Interleaved PCM samples in the source format (float32, [-1,1]).</summary>
    public float[] Samples { get; init; } = Array.Empty<float>();
    public AudioFormat Format { get; init; }
    /// <summary>Wall-clock time when this buffer finished capturing.</summary>
    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.Now;
}

public sealed class AudioCaptureErrorEventArgs : EventArgs
{
    public string Message { get; init; } = "";
    public Exception? Exception { get; init; }
    public bool Fatal { get; init; }
}

public sealed class AudioDeviceChangedEventArgs : EventArgs
{
    public string? NewDefaultDeviceId { get; init; }
    public string Message { get; init; } = "";
}

/// <summary>
/// Captures the system playback (render) audio stream via WASAPI loopback.
/// Implementations MUST NOT change device defaults, volumes, mute state, or exclusive mode.
/// </summary>
public interface IAudioCaptureService : IAsyncDisposable
{
    CaptureState State { get; }
    string? CurrentDeviceId { get; }

    IReadOnlyList<AudioDeviceInfo> EnumerateDevices();
    AudioDeviceInfo? GetDefaultDevice();

    /// <summary>Raised on the capture thread for every captured buffer.</summary>
    event EventHandler<AudioFramesEventArgs>? FramesAvailable;
    /// <summary>Normalized 0..1 output level, raised periodically.</summary>
    event EventHandler<double>? LevelChanged;
    event EventHandler<AudioCaptureErrorEventArgs>? Error;
    event EventHandler<AudioDeviceChangedEventArgs>? DeviceChanged;

    Task StartAsync(string deviceId, CancellationToken cancellationToken = default);
    void Pause();
    void Resume();
    Task StopAsync();
}

/// <summary>
/// Converts captured PCM into the mono float32 stream the ASR engine expects
/// (channel downmix + sample-rate conversion).
/// </summary>
public interface IAudioPreprocessor
{
    AudioFormat TargetFormat { get; }
    /// <summary>Returns mono float32 samples at <see cref="TargetFormat"/>'s rate.</summary>
    float[] Process(ReadOnlySpan<float> interleaved, AudioFormat sourceFormat);
    void Reset();
}
