using LocalMeetingSubtitle.Core.Abstractions;
using LocalMeetingSubtitle.Core.Audio;
using LocalMeetingSubtitle.Core.Models;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using CaptureState = LocalMeetingSubtitle.Core.Models.CaptureState;

namespace LocalMeetingSubtitle.Audio;

/// <summary>
/// Captures the system playback (render) stream through WASAPI loopback in <b>shared</b> mode.
/// The implementation never requests exclusive mode, never changes the default device, master
/// volume or mute state, never installs a virtual device and never replays the captured audio:
/// it only listens to whatever the system is already playing.
/// </summary>
public sealed class WasapiLoopbackCaptureService : IAudioCaptureService
{
    /// <summary>Minimum spacing between <see cref="LevelChanged"/> notifications (ms).</summary>
    private const int LevelIntervalMs = 50;
    /// <summary>How often the default render device is polled for a change (ms).</summary>
    private const int DefaultDevicePollMs = 2000;

    private readonly IAppLogger _logger;
    private readonly object _deviceLock = new();
    private readonly MMDeviceEnumerator _enumerator;

    private WasapiLoopbackCapture? _capture;
    private MMDevice? _captureDevice;
    private Timer? _defaultDeviceTimer;
    private volatile CaptureState _state = CaptureState.Stopped;
    private volatile bool _paused;
    private volatile bool _disposed;

    private AudioFormat _format;
    private long _lastLevelMs;
    private string? _lastDefaultDeviceId;

    public WasapiLoopbackCaptureService(IAppLogger? logger = null)
    {
        _logger = logger ?? NullLogger.Instance;
        _enumerator = new MMDeviceEnumerator();
    }

    public CaptureState State => _state;

    public string? CurrentDeviceId { get; private set; }

    /// <summary>The interleaved sample format produced by the current capture device.</summary>
    public AudioFormat SourceFormat => _format;

    public event EventHandler<AudioFramesEventArgs>? FramesAvailable;
    public event EventHandler<double>? LevelChanged;
    public event EventHandler<AudioCaptureErrorEventArgs>? Error;
    public event EventHandler<AudioDeviceChangedEventArgs>? DeviceChanged;

    public IReadOnlyList<AudioDeviceInfo> EnumerateDevices()
    {
        var list = new List<AudioDeviceInfo>();
        lock (_deviceLock)
        {
            string? defaultId = TryGetDefaultDeviceId();
            foreach (var device in _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                using (device)
                {
                    bool isDefault = string.Equals(device.ID, defaultId, StringComparison.OrdinalIgnoreCase);
                    list.Add(new AudioDeviceInfo(device.ID, device.FriendlyName, isDefault, IsLoopbackCapable: true));
                }
            }
        }
        return list;
    }

    public AudioDeviceInfo? GetDefaultDevice()
    {
        lock (_deviceLock)
        {
            try
            {
                using var device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                return new AudioDeviceInfo(device.ID, device.FriendlyName, IsDefault: true, IsLoopbackCapable: true);
            }
            catch (Exception ex)
            {
                _logger.Warn($"No default render device available: {ex.Message}");
                return null;
            }
        }
    }

    public Task StartAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(WasapiLoopbackCaptureService));
        if (_state is CaptureState.Running or CaptureState.Paused)
            throw new InvalidOperationException("Capture is already running.");

        _state = CaptureState.Starting;
        var device = ResolveDevice(deviceId);
        _captureDevice = device;

        try
        {
            var capture = new WasapiLoopbackCapture(device);
            var wf = capture.WaveFormat;
            _format = new AudioFormat(wf.SampleRate, wf.Channels, wf.BitsPerSample);

            capture.DataAvailable += OnDataAvailable;
            capture.RecordingStopped += OnRecordingStopped;

            _capture = capture;
            _paused = false;
            _lastLevelMs = 0;
            CurrentDeviceId = device.ID;
            lock (_deviceLock)
            {
                _lastDefaultDeviceId = TryGetDefaultDeviceId();
            }

            capture.StartRecording();
            _state = CaptureState.Running;
            StartDefaultDevicePolling();

            _logger.Info($"WASAPI loopback capture started on '{device.FriendlyName}' ({_format}).");
            return Task.CompletedTask;
        }
        catch
        {
            _state = CaptureState.Stopped;
            CleanupCapture();
            throw;
        }
    }

    public void Pause()
    {
        if (_capture is null || _state != CaptureState.Running) return;
        // The WASAPI stream keeps running untouched; we simply stop surfacing frames.
        _paused = true;
        _state = CaptureState.Paused;
    }

    public void Resume()
    {
        if (_capture is null || _state != CaptureState.Paused) return;
        _paused = false;
        _state = CaptureState.Running;
    }

    public Task StopAsync()
    {
        StopDefaultDevicePolling();
        _paused = false;
        CleanupCapture();
        _state = CaptureState.Stopped;
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        StopDefaultDevicePolling();
        await StopAsync().ConfigureAwait(false);

        try { _enumerator.Dispose(); }
        catch (Exception ex) { _logger.Debug($"Dispose enumerator: {ex.Message}"); }

        GC.SuppressFinalize(this);
    }

    private MMDevice ResolveDevice(string? deviceId)
    {
        lock (_deviceLock)
        {
            if (string.IsNullOrWhiteSpace(deviceId) ||
                string.Equals(deviceId, "default", StringComparison.OrdinalIgnoreCase))
            {
                return _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            }

            string? defaultId = TryGetDefaultDeviceId();
            if (string.Equals(deviceId, defaultId, StringComparison.OrdinalIgnoreCase))
            {
                return _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            }

            foreach (var device in _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                if (string.Equals(device.ID, deviceId, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(device.FriendlyName, deviceId, StringComparison.OrdinalIgnoreCase))
                {
                    return device;
                }
                device.Dispose();
            }

            throw new InvalidOperationException($"No render device matches '{deviceId}'.");
        }
    }

    private string? TryGetDefaultDeviceId()
    {
        try
        {
            using var device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            return device.ID;
        }
        catch (Exception ex)
        {
            _logger.Debug($"Unable to read default render device: {ex.Message}");
            return null;
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (_disposed || e.BytesRecorded <= 0) return;
        var capture = _capture;
        if (capture is null) return;

        float[] samples;
        try
        {
            samples = ConvertToFloat(e.Buffer, e.BytesRecorded, capture.WaveFormat);
        }
        catch (Exception ex)
        {
            RaiseError($"Failed to convert capture buffer: {ex.Message}", ex, fatal: false);
            return;
        }

        if (samples.Length == 0) return;

        if (!_paused)
        {
            FramesAvailable?.Invoke(this, new AudioFramesEventArgs
            {
                Samples = samples,
                Format = _format,
                CapturedAt = DateTimeOffset.Now
            });
        }

        long now = Environment.TickCount64;
        if (now - _lastLevelMs >= LevelIntervalMs)
        {
            _lastLevelMs = now;
            var mono = ChannelConverter.ToMono(samples, _format.Channels);
            double level = AudioMath.RmsToLevel(AudioMath.Rms(mono));
            LevelChanged?.Invoke(this, level);
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is null) return;

        _state = CaptureState.Faulted;
        RaiseError($"Capture stopped: {e.Exception.Message}", e.Exception, fatal: true);
    }

    private void RaiseError(string message, Exception? exception, bool fatal)
    {
        _logger.Error(message, exception);
        Error?.Invoke(this, new AudioCaptureErrorEventArgs
        {
            Message = message,
            Exception = exception,
            Fatal = fatal
        });
    }

    private void StartDefaultDevicePolling()
    {
        var timer = Interlocked.Exchange(ref _defaultDeviceTimer, null);
        timer?.Dispose();
        _defaultDeviceTimer = new Timer(PollDefaultDevice, null, DefaultDevicePollMs, DefaultDevicePollMs);
    }

    private void StopDefaultDevicePolling()
    {
        var timer = Interlocked.Exchange(ref _defaultDeviceTimer, null);
        timer?.Dispose();
    }

    private void PollDefaultDevice(object? state)
    {
        if (_disposed) return;

        string? id;
        try
        {
            lock (_deviceLock)
            {
                id = TryGetDefaultDeviceId();
            }
        }
        catch (Exception ex)
        {
            _logger.Debug($"Default device poll failed: {ex.Message}");
            return;
        }

        if (id is null) return;

        string? previous = _lastDefaultDeviceId;
        if (string.Equals(previous, id, StringComparison.OrdinalIgnoreCase)) return;

        _lastDefaultDeviceId = id;
        _logger.Info($"Default render device changed to {id}.");
        DeviceChanged?.Invoke(this, new AudioDeviceChangedEventArgs
        {
            NewDefaultDeviceId = id,
            Message = "Default render device changed."
        });
    }

    private void CleanupCapture()
    {
        var capture = Interlocked.Exchange(ref _capture, null);
        if (capture is not null)
        {
            capture.DataAvailable -= OnDataAvailable;
            capture.RecordingStopped -= OnRecordingStopped;
            try { capture.StopRecording(); }
            catch (Exception ex) { _logger.Debug($"StopRecording: {ex.Message}"); }
            try { capture.Dispose(); }
            catch (Exception ex) { _logger.Debug($"Dispose capture: {ex.Message}"); }
        }

        var device = Interlocked.Exchange(ref _captureDevice, null);
        try { device?.Dispose(); }
        catch (Exception ex) { _logger.Debug($"Dispose device: {ex.Message}"); }

        CurrentDeviceId = null;
    }

    /// <summary>Converts a raw capture buffer into interleaved float32 samples in [-1, 1].</summary>
    private static float[] ConvertToFloat(byte[] buffer, int bytesRecorded, WaveFormat format)
    {
        int bits = format.BitsPerSample;

        if (bits == 32 && IsFloatFormat(format))
        {
            int count = bytesRecorded / 4;
            var result = new float[count];
            Buffer.BlockCopy(buffer, 0, result, 0, count * 4);
            return result;
        }

        if (bits == 16)
        {
            int count = bytesRecorded / 2;
            var result = new float[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = BitConverter.ToInt16(buffer, i * 2) / 32768f;
            }
            return result;
        }

        if (bits == 24)
        {
            int count = bytesRecorded / 3;
            var result = new float[count];
            for (int i = 0; i < count; i++)
            {
                int o = i * 3;
                int v = buffer[o] | (buffer[o + 1] << 8) | (buffer[o + 2] << 16);
                if ((v & 0x800000) != 0) v |= unchecked((int)0xFF000000);
                result[i] = v / 8388608f;
            }
            return result;
        }

        if (bits == 32)
        {
            int count = bytesRecorded / 4;
            var result = new float[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = BitConverter.ToInt32(buffer, i * 4) / 2147483648f;
            }
            return result;
        }

        throw new NotSupportedException($"Unsupported capture format: {format}");
    }

    private static bool IsFloatFormat(WaveFormat format)
    {
        if (format.Encoding == WaveFormatEncoding.IeeeFloat) return true;
        if (format is WaveFormatExtensible ext) return ext.SubFormat == MediaSubtypeIeeeFloat;
        return false;
    }

    /// <summary>KSDATAFORMAT_SUBTYPE_IEEE_FLOAT (00000003-0000-0010-8000-00AA00389B71).</summary>
    private static readonly Guid MediaSubtypeIeeeFloat = new("00000003-0000-0010-8000-00aa00389b71");
}
