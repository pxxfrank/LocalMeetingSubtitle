using LocalMeetingSubtitle.Core.Abstractions;
using LocalMeetingSubtitle.Core.Models;

namespace LocalMeetingSubtitle.PerformanceTests;

/// <summary>Minimal capture double that lets a test push audio buffers on demand.</summary>
internal sealed class FakeAudioCaptureService : IAudioCaptureService
{
    public CaptureState State { get; private set; } = CaptureState.Stopped;
    public string? CurrentDeviceId { get; private set; }

    public event EventHandler<AudioFramesEventArgs>? FramesAvailable;
    public event EventHandler<double>? LevelChanged;
    public event EventHandler<AudioCaptureErrorEventArgs>? Error;
    public event EventHandler<AudioDeviceChangedEventArgs>? DeviceChanged;

    public IReadOnlyList<AudioDeviceInfo> EnumerateDevices() =>
        new[] { new AudioDeviceInfo("fake", "Fake Device", IsDefault: true, IsLoopbackCapable: true) };

    public AudioDeviceInfo? GetDefaultDevice() => EnumerateDevices()[0];

    public Task StartAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        CurrentDeviceId = deviceId;
        State = CaptureState.Running;
        return Task.CompletedTask;
    }

    public void Pause() => State = CaptureState.Paused;
    public void Resume() => State = CaptureState.Running;
    public Task StopAsync() { State = CaptureState.Stopped; return Task.CompletedTask; }
    public ValueTask DisposeAsync() { State = CaptureState.Stopped; return ValueTask.CompletedTask; }

    public void RaiseFrames(float[] samples, AudioFormat format) =>
        FramesAvailable?.Invoke(this, new AudioFramesEventArgs
        {
            Samples = samples,
            Format = format,
            CapturedAt = DateTimeOffset.Now
        });
}

/// <summary>Pass-through preprocessor (tests feed mono audio at the target rate).</summary>
internal sealed class FakePreprocessor : IAudioPreprocessor
{
    public FakePreprocessor(int targetSampleRate = 16000) =>
        TargetFormat = AudioFormat.Float32(targetSampleRate, 1);

    public AudioFormat TargetFormat { get; }

    public float[] Process(ReadOnlySpan<float> interleaved, AudioFormat sourceFormat) => interleaved.ToArray();
    public void Reset() { }
}

/// <summary>Hotword service double with identity corrections.</summary>
internal sealed class FakeHotwordService : IHotwordService
{
    public IReadOnlyList<Hotword> AllHotwords => Array.Empty<Hotword>();
    public IReadOnlyList<Hotword> ActiveHotwords => Array.Empty<Hotword>();
    public IReadOnlyList<TextCorrectionRule> ActiveRules => Array.Empty<TextCorrectionRule>();
    public HotwordMode Mode => HotwordMode.TextCorrectionOnly;
    public string? ModeDetail => null;

    public void SetEngineCapability(bool supportsModelHotwords, bool modelCanBeRebuiltWithoutDataLoss) { }
    public Task ReloadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public HotwordValidationResult Validate(string text) => HotwordValidationResult.Ok;
    public string? WriteModelHotwordFile(string destinationPath) => null;
    public string ApplyCorrections(string text) => text;

    public bool ApplyCorrections(SubtitleSegment segment)
    {
        segment.CorrectedText = segment.OriginalText;
        return false;
    }
}

/// <summary>
/// In-memory subtitle repository so the long-run pipeline scenario measures the pipeline, not disk.
/// </summary>
internal sealed class InMemorySubtitleRepository : ISubtitleRepository
{
    private readonly object _gate = new();
    private readonly List<SubtitleSegment> _segments = new();

    public int SegmentCount { get { lock (_gate) return _segments.Count; } }

    public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<MeetingSession> CreateSessionAsync(MeetingSession session, CancellationToken cancellationToken = default) =>
        Task.FromResult(session);

    public Task AppendSegmentAsync(SubtitleSegment segment, CancellationToken cancellationToken = default)
    {
        lock (_gate) { _segments.Add(segment); }
        return Task.CompletedTask;
    }

    public Task UpdateSegmentTextAsync(long segmentId, string correctedText, bool isEdited, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<IReadOnlyList<SubtitleSegment>> GetSegmentsAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<SubtitleSegment>>(
                _segments.Where(s => s.SessionId == sessionId).OrderBy(s => s.SequenceNumber).ToList());
        }
    }

    public Task<MeetingSession?> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default) =>
        Task.FromResult<MeetingSession?>(null);

    public Task<IReadOnlyList<MeetingSession>> GetRecentSessionsAsync(int limit = 100, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<MeetingSession>>(Array.Empty<MeetingSession>());

    public Task<int> GetNextSequenceAsync(string sessionId, CancellationToken cancellationToken = default) =>
        Task.FromResult(0);

    public Task UpdateSessionStatusAsync(string sessionId, SessionStatus status, DateTimeOffset? endTime, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<int> RecoverAbortedSessionsAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);

    public Task AppendMetricAsync(PerformanceMetric metric, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<IReadOnlyList<PerformanceMetric>> GetMetricsAsync(string sessionId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PerformanceMetric>>(Array.Empty<PerformanceMetric>());

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>Records the queue/RTF metrics the pipeline reports.</summary>
internal sealed class RecordingPerformanceMonitor : IPerformanceMonitor
{
    public bool IsRunning { get; private set; }
    public double LastRtf { get; private set; }
    public double MaxReportedQueueSamples { get; private set; }
    public double MaxReportedDroppedSeconds { get; private set; }

    public event EventHandler<PerformanceSnapshot>? Sampled;

    public void Start() => IsRunning = true;
    public void Stop() => IsRunning = false;
    public PerformanceSnapshot Snapshot() => new(DateTimeOffset.Now, 0, 0, LastRtf, 0, 0, MaxReportedDroppedSeconds, 0);
    public void ReportRtf(double rtf) => LastRtf = rtf;
    public void ReportLatency(double latencyMs) { }

    public void ReportQueue(int currentLength, int maxLength, double droppedSeconds)
    {
        MaxReportedQueueSamples = Math.Max(MaxReportedQueueSamples, maxLength);
        MaxReportedDroppedSeconds = Math.Max(MaxReportedDroppedSeconds, droppedSeconds);
    }

    public void Dispose() { }
}

/// <summary>Synthetic mono audio helpers.</summary>
internal static class TestAudio
{
    public static float[] Speech(double seconds, int sampleRate = 16000)
    {
        int count = (int)(seconds * sampleRate);
        var data = new float[count];
        for (int i = 0; i < count; i++)
        {
            data[i] = (float)(0.3 * Math.Sin(2.0 * Math.PI * 440.0 * i / sampleRate));
        }
        return data;
    }

    public static float[] Silence(double seconds, int sampleRate = 16000) => new float[(int)(seconds * sampleRate)];

    public static float[] Concat(params float[][] parts)
    {
        int total = parts.Sum(p => p.Length);
        var result = new float[total];
        int offset = 0;
        foreach (var part in parts)
        {
            part.CopyTo(result, offset);
            offset += part.Length;
        }
        return result;
    }
}
