namespace LocalMeetingSubtitle.Core.Abstractions;

/// <summary>Immutable snapshot of runtime resource usage for one sampling instant.</summary>
public sealed record PerformanceSnapshot(
    DateTimeOffset Timestamp,
    double CpuPercent,
    double WorkingSetMb,
    double Rtf,
    int QueueLength,
    int MaxQueueLength,
    double DroppedAudioSeconds,
    double EndToEndLatencyMs);

/// <summary>Collects CPU/memory and pipeline metrics. Must be cheap and non-blocking.</summary>
public interface IPerformanceMonitor : IDisposable
{
    bool IsRunning { get; }
    void Start();
    void Stop();
    PerformanceSnapshot Snapshot();

    /// <summary>Last measured real-time factor (inferenceSeconds / audioSeconds).</summary>
    void ReportRtf(double rtf);
    /// <summary>Current/last queue depth, its high-water mark, and cumulative dropped audio.</summary>
    void ReportQueue(int currentLength, int maxLength, double droppedSeconds);
    /// <summary>Latency between audio capture and the latest emitted subtitle.</summary>
    void ReportLatency(double latencyMs);

    event EventHandler<PerformanceSnapshot>? Sampled;
}
