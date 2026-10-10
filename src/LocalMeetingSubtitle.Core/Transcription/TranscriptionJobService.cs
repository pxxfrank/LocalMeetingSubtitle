using System.Collections.Concurrent;
using LocalMeetingSubtitle.Core.Abstractions;
using LocalMeetingSubtitle.Core.Models;

namespace LocalMeetingSubtitle.Core.Transcription;

/// <summary>
/// A single FIFO worker over the persisted job queue. Enqueuing only writes rows; every run rebuilds
/// its request from the stored job through the injected <see cref="TranscriptionJobResolver"/>, so a
/// job survives a restart and an interrupted job resumes from its last committed segment.
///
/// The worker is a dedicated below-normal-priority thread, created lazily on <see cref="StartAsync"/>.
/// Note that after the first real <c>await</c> inside the run, continuations execute on the thread
/// pool, so the priority hint applies mainly to process start-up — the real isolation from live ASR
/// is the single job, the low thread caps and the separate ffmpeg children.
/// </summary>
public sealed class TranscriptionJobService : ITranscriptionJobService
{
    private static readonly TimeSpan ProgressWriteInterval = TimeSpan.FromSeconds(2);

    private readonly IFileTranscriptionService _files;
    private readonly ITranscriptionJobRepository _jobs;
    private readonly IMediaFileRepository _mediaFiles;
    private readonly ISubtitleRepository _subtitles;
    private readonly TranscriptionJobResolver _resolver;
    private readonly IAppLogger _log;

    private readonly BlockingCollection<string> _queue = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _running = new();
    private readonly ConcurrentDictionary<string, byte> _cancelled = new();
    private readonly SemaphoreSlim _startGate = new(1, 1);
    private readonly object _queuedAtGate = new();
    private DateTimeOffset _lastQueuedAt = DateTimeOffset.MinValue;
    private CancellationTokenSource? _shutdown;
    private Thread? _worker;
    private bool _disposed;

    public TranscriptionJobService(
        IFileTranscriptionService files,
        ITranscriptionJobRepository jobs,
        IMediaFileRepository mediaFiles,
        ISubtitleRepository subtitles,
        TranscriptionJobResolver resolver,
        IAppLogger? log = null)
    {
        _files = files;
        _jobs = jobs;
        _mediaFiles = mediaFiles;
        _subtitles = subtitles;
        _resolver = resolver;
        _log = log ?? NullLogger.Instance;
    }

    public event EventHandler<TranscriptionJob>? JobChanged;

    public bool IsProcessing => !_running.IsEmpty;

    public int QueueLength => _queue.Count;

    public async Task<TranscriptionJob> EnqueueAsync(
        TranscriptionJobRequest request,
        MediaInfo media,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(media);

        var record = new MediaFileRecord
        {
            Path = media.Path,
            FileName = media.FileName,
            Kind = media.Kind,
            ContainerFormat = media.ContainerFormat,
            Duration = media.Duration,
            SizeBytes = media.FileSize,
            AudioStreamIndex = request.AudioStreamIndex
        };

        var job = new TranscriptionJob
        {
            MediaFileId = record.MediaFileId,
            Title = request.Title ?? media.FileName,
            Mode = request.Mode,
            ModelId = request.ModelId,
            AudioStreamIndex = request.AudioStreamIndex,
            RunDiarization = request.RunDiarization,
            DiarizationCountMode = request.DiarizationCountMode,
            ManualSpeakerCount = request.ManualSpeakerCount,
            ClusteringThreshold = request.ClusteringThreshold,
            Status = TranscriptionJobStatus.Queued,
            Total = media.Duration,
            QueuedAt = NextQueuedAt()
        };

        await _mediaFiles.AddAsync(record, cancellationToken).ConfigureAwait(false);
        await _jobs.AddAsync(job, cancellationToken).ConfigureAwait(false);
        _log.Info($"Queued file transcription job {job.JobId} for '{record.FileName}'.");
        Raise(job);

        if (_worker is not null && !_queue.IsAddingCompleted)
        {
            _queue.Add(job.JobId);
        }

        return job;
    }

    public Task<TranscriptionJob?> GetJobAsync(string jobId, CancellationToken cancellationToken = default)
        => _jobs.GetAsync(jobId, cancellationToken);

    public Task<IReadOnlyList<TranscriptionJob>> GetJobsAsync(CancellationToken cancellationToken = default)
        => _jobs.GetJobsAsync(null, cancellationToken);

    public async Task<bool> CancelAsync(string jobId, CancellationToken cancellationToken = default)
    {
        var job = await _jobs.GetAsync(jobId, cancellationToken).ConfigureAwait(false);
        if (job is null || job.IsFinished)
        {
            return false;
        }

        _cancelled[jobId] = 1;
        if (_running.TryGetValue(jobId, out var cts))
        {
            await cts.CancelAsync().ConfigureAwait(false);
            return true;
        }

        // Not running yet: mark it now so the worker skips it.
        job.Status = TranscriptionJobStatus.Cancelled;
        job.FinishedAt = DateTimeOffset.Now;
        await _jobs.UpdateAsync(job, cancellationToken).ConfigureAwait(false);
        Raise(job);
        return true;
    }

    public async Task<bool> ResumeAsync(string jobId, CancellationToken cancellationToken = default)
    {
        var job = await _jobs.GetAsync(jobId, cancellationToken).ConfigureAwait(false);
        if (job is null || job.Status is TranscriptionJobStatus.Queued or TranscriptionJobStatus.Running)
        {
            return false;
        }

        _cancelled.TryRemove(jobId, out _);
        job.Status = TranscriptionJobStatus.Queued;
        job.FinishedAt = null;
        job.Error = null;
        await _jobs.UpdateAsync(job, cancellationToken).ConfigureAwait(false);
        Raise(job);

        if (_worker is not null && !_queue.IsAddingCompleted)
        {
            _queue.Add(jobId);
        }

        return true;
    }

    public async Task<int> RecoverUnfinishedAsync(CancellationToken cancellationToken = default)
    {
        var running = await _jobs.GetJobsAsync(TranscriptionJobStatus.Running, cancellationToken).ConfigureAwait(false);
        foreach (var job in running)
        {
            job.Status = TranscriptionJobStatus.Interrupted;
            await _jobs.UpdateAsync(job, cancellationToken).ConfigureAwait(false);
            Raise(job);
            _log.Warn($"Job {job.JobId} was left running by a previous session; marked interrupted.");
        }

        return running.Count;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _startGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_worker is not null || _disposed)
            {
                return;
            }

            _shutdown = new CancellationTokenSource();
            _worker = new Thread(() => WorkerLoop(_shutdown.Token))
            {
                IsBackground = true,
                Priority = ThreadPriority.BelowNormal,
                Name = "file-transcription-worker"
            };
            _worker.Start();

            // Pick up anything left from a previous session (queued or interrupted).
            var pending = await _jobs.GetJobsAsync(null, cancellationToken).ConfigureAwait(false);
            foreach (var job in pending.Where(j => j.IsRunnable).OrderBy(j => j.QueuedAt).ThenBy(j => j.JobId, StringComparer.Ordinal))
            {
                _queue.Add(job.JobId);
            }
        }
        finally
        {
            _startGate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        var worker = _worker;
        var shutdown = _shutdown;
        _worker = null;
        _shutdown = null;

        if (worker is null)
        {
            return;
        }

        _queue.CompleteAdding();
        if (shutdown is not null)
        {
            await shutdown.CancelAsync().ConfigureAwait(false);
        }

        await Task.Run(() => worker.Join(TimeSpan.FromSeconds(30)), cancellationToken).ConfigureAwait(false);
        shutdown?.Dispose();
    }

    public async Task<int> DrainAsync(CancellationToken cancellationToken = default)
    {
        var pending = await _jobs.GetJobsAsync(null, cancellationToken).ConfigureAwait(false);
        int processed = 0;

        foreach (var job in pending.Where(j => j.IsRunnable).OrderBy(j => j.QueuedAt).ThenBy(j => j.JobId, StringComparer.Ordinal).ToList())
        {
            cancellationToken.ThrowIfCancellationRequested();
            await RunJobAsync(job.JobId, cancellationToken).ConfigureAwait(false);
            processed++;
        }

        return processed;
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        await StopAsync().ConfigureAwait(false);
        _queue.Dispose();
        _startGate.Dispose();
        await _jobs.DisposeAsync().ConfigureAwait(false);
        await _mediaFiles.DisposeAsync().ConfigureAwait(false);
        await _subtitles.DisposeAsync().ConfigureAwait(false);
    }

    private void WorkerLoop(CancellationToken shutdown)
    {
        try
        {
            foreach (var jobId in _queue.GetConsumingEnumerable(shutdown))
            {
                try
                {
                    RunJobAsync(jobId, shutdown).GetAwaiter().GetResult();
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    _log.Error($"File transcription job {jobId} crashed", ex);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task RunJobAsync(string jobId, CancellationToken shutdown)
    {
        var job = await _jobs.GetAsync(jobId, shutdown).ConfigureAwait(false);
        if (job is null || !job.IsRunnable)
        {
            return;
        }

        if (_cancelled.ContainsKey(jobId))
        {
            job.Status = TranscriptionJobStatus.Cancelled;
            job.FinishedAt = DateTimeOffset.Now;
            await _jobs.UpdateAsync(job, shutdown).ConfigureAwait(false);
            Raise(job);
            return;
        }

        bool resumed = !string.IsNullOrEmpty(job.SessionId);
        job.Status = TranscriptionJobStatus.Running;
        job.StartedAt ??= DateTimeOffset.Now;
        job.Attempts++;
        if (resumed)
        {
            job.ResumeCount++;
        }

        await _jobs.UpdateAsync(job, shutdown).ConfigureAwait(false);
        Raise(job);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(shutdown);
        _running[jobId] = cts;

        var lastProgressWrite = DateTimeOffset.MinValue;
        var progress = new ProgressAdapter<FileTranscriptionProgress>(p =>
        {
            job.Phase = p.Phase;
            job.Processed = p.Processed;
            job.Total = p.Total > TimeSpan.Zero ? p.Total : job.Total;
            job.SegmentsEmitted = p.SegmentsEmitted;
            Raise(job);

            // Throttled, blocking write: a display snapshot is worth a millisecond, and the real
            // resume cursor is the committed segments (written by the file service, not here).
            if (DateTimeOffset.Now - lastProgressWrite >= ProgressWriteInterval)
            {
                lastProgressWrite = DateTimeOffset.Now;
                try
                {
                    _jobs.UpdateAsync(job, CancellationToken.None).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    _log.Warn($"Could not persist progress for job {jobId}: {ex.Message}");
                }
            }
        });

        IAsrEngine? engine = null;
        try
        {
            var request = await _resolver(job, cts.Token).ConfigureAwait(false);
            engine = request.Engine;

            var result = await _files
                .RunAsync(request with { SessionId = job.SessionId }, progress, cts.Token)
                .ConfigureAwait(false);

            if (!string.IsNullOrEmpty(result.SessionId))
            {
                job.SessionId = result.SessionId;
            }

            job.Processed = result.AudioDuration;
            job.Warning = result.Warning;

            if (result.Error is not null)
            {
                job.Status = TranscriptionJobStatus.Failed;
                job.Error = result.Error;
            }
            else if (result.Cancelled)
            {
                job.Status = TranscriptionJobStatus.Cancelled;
            }
            else
            {
                job.Status = TranscriptionJobStatus.Succeeded;
                job.Error = null;
            }
        }
        catch (OperationCanceledException)
        {
            job.Status = TranscriptionJobStatus.Cancelled;
        }
        catch (Exception ex)
        {
            job.Status = TranscriptionJobStatus.Failed;
            job.Error = ex.Message;
            _log.Error($"File transcription job {jobId} failed", ex);
        }
        finally
        {
            _running.TryRemove(jobId, out _);
            _cancelled.TryRemove(jobId, out _);
            engine?.Dispose();
        }

        job.FinishedAt = DateTimeOffset.Now;
        try
        {
            await _jobs.UpdateAsync(job, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Warn($"Could not persist the outcome of job {jobId}: {ex.Message}");
        }

        Raise(job);
        _log.Info($"Job {jobId} finished as {job.Status} ({job.SegmentsEmitted} segment(s)).");
    }

    private void Raise(TranscriptionJob job)
    {
        try
        {
            JobChanged?.Invoke(this, job);
        }
        catch (Exception ex)
        {
            _log.Warn($"A job-changed handler threw: {ex.Message}");
        }
    }

    /// <summary>
    /// Strictly increasing queue timestamps so FIFO order is well defined even when several jobs are
    /// enqueued inside the same clock tick (the queue sorts on the stored ISO timestamp).
    /// </summary>
    private DateTimeOffset NextQueuedAt()
    {
        lock (_queuedAtGate)
        {
            var candidate = DateTimeOffset.Now;
            if (candidate <= _lastQueuedAt)
            {
                candidate = _lastQueuedAt.AddTicks(1);
            }

            _lastQueuedAt = candidate;
            return candidate;
        }
    }

    /// <summary>Forwards progress synchronously so reports keep their order.</summary>
    private sealed class ProgressAdapter<T> : IProgress<T>
    {
        private readonly Action<T> _handler;
        public ProgressAdapter(Action<T> handler) => _handler = handler;
        public void Report(T value) => _handler(value);
    }
}
