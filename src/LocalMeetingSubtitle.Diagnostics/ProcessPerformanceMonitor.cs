using System.Diagnostics;
using LocalMeetingSubtitle.Core.Abstractions;

namespace LocalMeetingSubtitle.Diagnostics;

/// <summary>
/// Samples CPU/memory for the current process on a lightweight timer and combines them with
/// pipeline metrics reported by the caller. CPU is read from the "Processor Information /
/// % Processor Utility" performance counter when available and falls back to process CPU-time
/// deltas (divided by wall time and logical processor count) otherwise — the fallback keeps
/// the monitor working in VMs and containers where the counter category is missing.
/// </summary>
public sealed class ProcessPerformanceMonitor : IPerformanceMonitor
{
    private const int SampleIntervalMs = 1000;
    private const int CpuAverageWindow = 3;

    private readonly object _gate = new();
    private readonly Process _process;
    private readonly RollingAverage _cpuAverage = new(CpuAverageWindow);

    private Timer? _timer;
    private PerformanceCounter? _cpuCounter;
    private bool _cpuCounterAvailable;

    private TimeSpan _lastCpuTime;
    private long _lastCpuTimestamp;
    private double _cpuPercent;

    private double _rtf;
    private int _queueLength;
    private int _maxQueueLength;
    private double _droppedSeconds;
    private double _latencyMs;

    public ProcessPerformanceMonitor()
    {
        _process = Process.GetCurrentProcess();
        TryCreateCpuCounter();
    }

    public bool IsRunning => _timer is not null;

    public event EventHandler<PerformanceSnapshot>? Sampled;

    public void Start()
    {
        lock (_gate)
        {
            if (_timer is not null) return;
            _lastCpuTimestamp = 0;
            _lastCpuTime = TimeSpan.Zero;
            _timer = new Timer(OnSample, null, SampleIntervalMs, SampleIntervalMs);
        }
    }

    public void Stop()
    {
        Timer? timer;
        lock (_gate)
        {
            timer = _timer;
            _timer = null;
        }
        timer?.Dispose();
    }

    public PerformanceSnapshot Snapshot()
    {
        double workingSetMb = ReadWorkingSetMb();
        lock (_gate)
        {
            return new PerformanceSnapshot(
                DateTimeOffset.Now,
                _cpuPercent,
                workingSetMb,
                _rtf,
                _queueLength,
                _maxQueueLength,
                _droppedSeconds,
                _latencyMs);
        }
    }

    public void ReportRtf(double rtf)
    {
        lock (_gate) _rtf = rtf;
    }

    public void ReportQueue(int currentLength, int maxLength, double droppedSeconds)
    {
        lock (_gate)
        {
            _queueLength = currentLength;
            _maxQueueLength = maxLength;
            _droppedSeconds = droppedSeconds;
        }
    }

    public void ReportLatency(double latencyMs)
    {
        lock (_gate) _latencyMs = latencyMs;
    }

    public void Dispose()
    {
        Stop();
        _cpuCounter?.Dispose();
        _cpuCounter = null;
        _process.Dispose();
        GC.SuppressFinalize(this);
    }

    private void OnSample(object? state)
    {
        double cpu = SampleCpuPercent();
        double workingSetMb = ReadWorkingSetMb();

        PerformanceSnapshot snapshot;
        lock (_gate)
        {
            _cpuPercent = cpu;
            snapshot = new PerformanceSnapshot(
                DateTimeOffset.Now,
                cpu,
                workingSetMb,
                _rtf,
                _queueLength,
                _maxQueueLength,
                _droppedSeconds,
                _latencyMs);
        }

        Sampled?.Invoke(this, snapshot);
    }

    private double ReadWorkingSetMb()
    {
        try
        {
            _process.Refresh();
            return _process.WorkingSet64 / (1024.0 * 1024.0);
        }
        catch
        {
            return 0.0;
        }
    }

    private double SampleCpuPercent()
    {
        double raw = 0.0;

        if (_cpuCounterAvailable && _cpuCounter is not null)
        {
            try
            {
                raw = _cpuCounter.NextValue();
            }
            catch
            {
                // Counter vanished/unusable (e.g. VM without the category) — drop to the fallback.
                _cpuCounterAvailable = false;
            }
        }

        if (!_cpuCounterAvailable)
        {
            raw = SampleCpuFromProcess();
        }

        _cpuAverage.Add(raw);
        return Math.Clamp(_cpuAverage.Average, 0.0, 100.0);
    }

    private double SampleCpuFromProcess()
    {
        TimeSpan cpuTime;
        try
        {
            _process.Refresh();
            cpuTime = _process.TotalProcessorTime;
        }
        catch
        {
            return 0.0;
        }

        long timestamp = Stopwatch.GetTimestamp();
        double result = 0.0;

        if (_lastCpuTimestamp != 0)
        {
            double wallSeconds = (timestamp - _lastCpuTimestamp) / (double)Stopwatch.Frequency;
            double cpuSeconds = (cpuTime - _lastCpuTime).TotalSeconds;
            int cores = Environment.ProcessorCount;
            if (wallSeconds > 0 && cores > 0)
            {
                result = cpuSeconds / wallSeconds / cores * 100.0;
            }
        }

        _lastCpuTime = cpuTime;
        _lastCpuTimestamp = timestamp;
        return result;
    }

    private void TryCreateCpuCounter()
    {
        try
        {
            var counter = new PerformanceCounter(
                "Processor Information", "% Processor Utility", "_Total", readOnly: true);
            counter.NextValue(); // Prime the counter; the first read always returns 0.
            _cpuCounter = counter;
            _cpuCounterAvailable = true;
        }
        catch (Exception)
        {
            _cpuCounter?.Dispose();
            _cpuCounter = null;
            _cpuCounterAvailable = false;
        }
    }
}
