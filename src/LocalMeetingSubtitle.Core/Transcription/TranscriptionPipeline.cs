using System.Diagnostics;
using System.Threading.Channels;
using LocalMeetingSubtitle.Core.Abstractions;
using LocalMeetingSubtitle.Core.Audio;
using LocalMeetingSubtitle.Core.Models;

namespace LocalMeetingSubtitle.Core.Transcription;

public sealed class PipelineOverloadEventArgs : EventArgs
{
    public double DroppedSeconds { get; init; }
    public int QueueLength { get; init; }
    public string Message { get; init; } = "";
}

public sealed class PipelineOptions
{
    /// <summary>Max audio buffered between capture and ASR (seconds). Bounds memory and latency.</summary>
    public double QueueCapacitySeconds { get; init; } = 30;
    /// <summary>ASR worker poll interval when the queue is empty.</summary>
    public int IdlePollMs { get; init; } = 10;
    /// <summary>Offline-engine segmentation settings (ignored for streaming engines).</summary>
    public double OfflineMaxSegmentSeconds { get; init; } = 15.0;
    public double OfflineSilenceRms { get; init; } = 0.010;
}

/// <summary>
/// Connects capture -> preprocess -> ASR -> accumulator -> persistence without blocking the UI
/// or the capture thread. Audio and inference run on separate tasks; persistence runs on its
/// own writer so a slow disk can never stall recognition.
/// </summary>
public sealed class TranscriptionPipeline : IAsyncDisposable
{
    private readonly IAudioCaptureService _capture;
    private readonly IAudioPreprocessor _preprocessor;
    private readonly IHotwordService _hotwords;
    private readonly ISubtitleRepository _repository;
    private readonly IPerformanceMonitor? _performance;
    private readonly IAppLogger _log;
    private readonly PipelineOptions _options;

    private IAsrEngine _engine;
    private IAsrSession? _asrSession;
    private SubtitleAccumulator _accumulator = new();
    private AudioSegmenter? _segmenter;

    private BoundedAudioQueue _queue = new(16000 * 30);
    private readonly SemaphoreSlim _signal = new(0);
    private CancellationTokenSource? _cts;
    private Task? _asrTask;
    private Task? _writerTask;
    private Channel<SubtitleSegment>? _writeChannel;

    private MeetingSession _session = new();
    private volatile bool _feedingEnabled;
    private volatile bool _running;
    private long _cumulativeSamples;
    private long _lastOverloadLogTicks;
    private double _lastInferenceMs;
    private readonly object _swapGate = new();
    private IAsrEngine? _pendingSwap;

    public TranscriptionPipeline(
        IAudioCaptureService capture,
        IAudioPreprocessor preprocessor,
        IAsrEngine engine,
        IHotwordService hotwords,
        ISubtitleRepository repository,
        IPerformanceMonitor? performance = null,
        IAppLogger? log = null,
        PipelineOptions? options = null)
    {
        _capture = capture;
        _preprocessor = preprocessor;
        _engine = engine;
        _hotwords = hotwords;
        _repository = repository;
        _performance = performance;
        _log = log ?? NullLogger.Instance;
        _options = options ?? new PipelineOptions();

        _capture.FramesAvailable += OnFramesAvailable;
        _capture.LevelChanged += (_, level) => LevelChanged?.Invoke(this, level);
        _capture.Error += OnCaptureError;
        _capture.DeviceChanged += OnDeviceChanged;
    }

    public TranscriptionState State { get; private set; } = TranscriptionState.Idle;
    public string SessionId => _session.SessionId;
    public TranscriptionStatus Status { get; private set; } = TranscriptionStatus.Idle;

    public event EventHandler<TranscriptEvent>? TranscriptUpdated;
    public event EventHandler<TranscriptionStatus>? StatusChanged;
    public event EventHandler<double>? LevelChanged;
    public event EventHandler<PipelineOverloadEventArgs>? Overload;
    public event EventHandler<string>? ErrorOccurred;

    public async Task StartAsync(string deviceId, MeetingSession session, CancellationToken cancellationToken = default)
    {
        if (_running) throw new InvalidOperationException("Pipeline already running.");

        _session = session;
        _accumulator = new SubtitleAccumulator(_hotwords.ApplyCorrections);
        _queue = new BoundedAudioQueue((long)(_options.QueueCapacitySeconds * _preprocessor.TargetFormat.SampleRate));
        _cumulativeSamples = 0;
        _cts = new CancellationTokenSource();
        _writeChannel = Channel.CreateUnbounded<SubtitleSegment>(new UnboundedChannelOptions { SingleReader = true });

        _asrSession = _engine.CreateSession();
        _segmenter = _engine.Capabilities.Streaming
            ? null
            : new AudioSegmenter(_preprocessor.TargetFormat.SampleRate, _options.OfflineSilenceRms,
                maxSegmentSeconds: _options.OfflineMaxSegmentSeconds);

        _running = true;
        _feedingEnabled = false;

        _writerTask = Task.Run(() => WriterLoopAsync(_writeChannel.Reader, _cts.Token));
        _asrTask = Task.Run(() => AsrLoopAsync(_cts.Token));

        SetState(TranscriptionState.Transcribing, $"Listening on \"{session.AudioDeviceName}\"");

        try
        {
            await _capture.StartAsync(deviceId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _running = false;
            _feedingEnabled = false;
            SetState(TranscriptionState.Faulted, $"Audio capture failed: {ex.Message}");
            ErrorOccurred?.Invoke(this, ex.Message);
            throw;
        }

        _feedingEnabled = true;
    }

    public async Task PauseAsync()
    {
        if (!_running || State == TranscriptionState.Paused) return;
        _feedingEnabled = false;

        // Flush whatever the model has so we never lose the last sentence on pause.
        _signal.Release();
        await Task.Delay(60).ConfigureAwait(false);

        _capture.Pause();
        SetState(TranscriptionState.Paused, "Paused");
        await _repository.UpdateSessionStatusAsync(_session.SessionId, SessionStatus.Paused, null).ConfigureAwait(false);
    }

    public async Task ResumeAsync()
    {
        if (!_running || State != TranscriptionState.Paused) return;
        _capture.Resume();
        _feedingEnabled = true;
        SetState(TranscriptionState.Transcribing, "Transcribing");
        await Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (!_running)
        {
            SetState(TranscriptionState.Idle, "Stopped");
            return;
        }

        _feedingEnabled = false;
        _running = false;
        _signal.Release();

        try { await _capture.StopAsync().ConfigureAwait(false); }
        catch (Exception ex) { _log.Warn($"Capture stop failed: {ex.Message}"); }

        // Let the ASR loop drain the queue and flush the final partial.
        if (_asrTask != null)
        {
            await Task.WhenAny(_asrTask, Task.Delay(5000)).ConfigureAwait(false);
        }

        _writeChannel?.Writer.TryComplete();
        if (_writerTask != null)
        {
            await Task.WhenAny(_writerTask, Task.Delay(5000)).ConfigureAwait(false);
        }

        _cts?.Cancel();
        await _repository.UpdateSessionStatusAsync(_session.SessionId, SessionStatus.Completed, DateTimeOffset.Now).ConfigureAwait(false);
        SetState(TranscriptionState.Ready, "Stopped");
    }

    /// <summary>
    /// Hot-swaps the ASR engine on the inference thread. The current utterance is flushed and
    /// persisted first so no subtitle is silently lost; audio capture is never interrupted.
    /// </summary>
    public void RequestEngineSwap(IAsrEngine newEngine)
    {
        lock (_swapGate)
        {
            _pendingSwap = newEngine;
        }
        _signal.Release();
    }

    private void OnFramesAvailable(object? sender, AudioFramesEventArgs e)
    {
        if (!_feedingEnabled || !_running) return;

        try
        {
            var mono = _preprocessor.Process(e.Samples, e.Format);
            if (mono.Length == 0) return;

            if (!_queue.Enqueue(mono))
            {
                ReportOverload();
            }
            else
            {
                _signal.Release();
            }

            _performance?.ReportQueue(_queue.Count, (int)_queue.MaxSamples, _queue.DroppedSeconds(_preprocessor.TargetFormat.SampleRate));
        }
        catch (Exception ex)
        {
            _log.Error("Preprocessing failed", ex);
        }
    }

    private async Task AsrLoopAsync(CancellationToken token)
    {
        var stopwatch = new Stopwatch();
        while (!token.IsCancellationRequested)
        {
            IAsrEngine? swap;
            lock (_swapGate) { swap = _pendingSwap; _pendingSwap = null; }
            if (swap != null)
            {
                SwapEngine(swap);
            }

            if (!_running && _queue.Count == 0)
            {
                Flush();
                break;
            }

            if (!_queue.TryDequeue(out var chunk) || chunk == null)
            {
                // Nothing to do; wait briefly for the next signal.
                try { await _signal.WaitAsync(_options.IdlePollMs, token).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
                continue;
            }

            long samples = chunk.Length;
            _cumulativeSamples += samples;
            var audioTime = TimeSpan.FromSeconds(_cumulativeSamples / (double)_preprocessor.TargetFormat.SampleRate);

            try
            {
                stopwatch.Restart();
                if (_engine.Capabilities.Streaming)
                {
                    DecodeStreaming(chunk, audioTime);
                }
                else
                {
                    DecodeOffline(chunk, audioTime);
                }
                stopwatch.Stop();

                double audioSeconds = samples / (double)_preprocessor.TargetFormat.SampleRate;
                if (audioSeconds > 0)
                {
                    _lastInferenceMs = stopwatch.Elapsed.TotalMilliseconds;
                    _performance?.ReportRtf(stopwatch.Elapsed.TotalSeconds / audioSeconds);
                }
            }
            catch (Exception ex)
            {
                _log.Error("ASR decode failed", ex);
                ErrorOccurred?.Invoke(this, $"Recognition error: {ex.Message}");
            }
        }
    }

    private void DecodeStreaming(float[] chunk, TimeSpan audioTime)
    {
        var session = _asrSession!;
        session.AcceptWaveform(chunk, _preprocessor.TargetFormat.SampleRate);
        while (session.IsReady())
        {
            session.Decode();
        }

        var result = session.GetResult();
        if (!string.IsNullOrEmpty(result.Text) || result.IsEndpoint)
        {
            Emit(_accumulator.Update(result.Text, result.IsEndpoint, audioTime), audioTime);
        }
        if (result.IsEndpoint)
        {
            session.Reset();
        }
    }

    private void DecodeOffline(float[] chunk, TimeSpan audioTime)
    {
        var segments = _segmenter!.Push(chunk);
        foreach (var segment in segments)
        {
            DecodeOfflineSegment(segment, audioTime);
        }
    }

    private void DecodeOfflineSegment(float[] segment, TimeSpan audioTime)
    {
        var session = _asrSession!;
        session.AcceptWaveform(segment, _preprocessor.TargetFormat.SampleRate);
        session.InputFinished();
        if (session.IsReady()) session.Decode();
        var result = session.GetResult();
        var text = result.Text ?? "";
        Emit(_accumulator.Update(text, isEndpoint: true, audioTime), audioTime);
    }

    private void Flush()
    {
        try
        {
            if (!_engine.Capabilities.Streaming && _segmenter != null)
            {
                var tail = _segmenter.Flush();
                if (tail != null) DecodeOfflineSegment(tail, TimeSpan.FromSeconds(_cumulativeSamples / (double)_preprocessor.TargetFormat.SampleRate));
            }
            var ev = _accumulator.Flush(TimeSpan.FromSeconds(_cumulativeSamples / (double)_preprocessor.TargetFormat.SampleRate));
            Emit(ev, TimeSpan.Zero);
        }
        catch (Exception ex)
        {
            _log.Error("Flush failed", ex);
        }
    }

    private void Emit(TranscriptEvent ev, TimeSpan audioTime)
    {
        if (ev.Kind == TranscriptEventKind.None) return;

        if (_performance != null)
        {
            double backlogMs = _queue.CurrentSamples / (double)_preprocessor.TargetFormat.SampleRate * 1000.0;
            _performance.ReportLatency(backlogMs + _lastInferenceMs);
        }

        TranscriptUpdated?.Invoke(this, ev);

        if (ev.Kind == TranscriptEventKind.Final)
        {
            var segment = new SubtitleSegment
            {
                SessionId = _session.SessionId,
                SequenceNumber = ev.SequenceNumber,
                StartOffset = ev.StartOffset,
                EndOffset = ev.EndOffset,
                OriginalText = ev.Text,
                CorrectedText = ev.CorrectedText,
                CreatedAt = DateTimeOffset.Now
            };
            _writeChannel?.Writer.TryWrite(segment);
        }
    }

    private void SwapEngine(IAsrEngine newEngine)
    {
        try
        {
            _log.Info("Hot-swapping ASR engine; flushing current utterance first.");
            Flush();

            // Build the replacement session before tearing the current one down: if CreateSession
            // throws, the running engine/session stay intact instead of leaving the loop decoding
            // with an already-disposed session (which throws a NullReferenceException).
            var newSession = newEngine.CreateSession();
            var newSegmenter = newEngine.Capabilities.Streaming
                ? null
                : new AudioSegmenter(_preprocessor.TargetFormat.SampleRate);

            var oldSession = _asrSession;
            var oldEngine = _engine;

            _engine = newEngine;
            _asrSession = newSession;
            _segmenter = newSegmenter;

            oldSession?.Dispose();
            oldEngine.Dispose();

            StatusChanged?.Invoke(this, Status with { HotwordDetail = "Model rebuilt with new hotwords." });
        }
        catch (Exception ex)
        {
            _log.Error("Engine swap failed", ex);
            ErrorOccurred?.Invoke(this, $"Failed to apply new hotwords: {ex.Message}");
        }
    }

    private async Task WriterLoopAsync(ChannelReader<SubtitleSegment> reader, CancellationToken token)
    {
        try
        {
            await foreach (var segment in reader.ReadAllAsync(token).ConfigureAwait(false))
            {
                var attempts = 0;
                while (true)
                {
                    try
                    {
                        await _repository.AppendSegmentAsync(segment, token).ConfigureAwait(false);
                        break;
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        attempts++;
                        _log.Error($"Persisting segment #{segment.SequenceNumber} failed (attempt {attempts})", ex);
                        if (attempts >= 3)
                        {
                            ErrorOccurred?.Invoke(this, $"Failed to save subtitle to database: {ex.Message}");
                            break;
                        }
                        await Task.Delay(250, token).ConfigureAwait(false);
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    private void ReportOverload()
    {
        double dropped = _queue.DroppedSeconds(_preprocessor.TargetFormat.SampleRate);
        _performance?.ReportQueue(_queue.Count, (int)_queue.MaxSamples, dropped);

        // Throttle overload notifications to once per ~2 s.
        long now = Environment.TickCount64;
        if (now - _lastOverloadLogTicks < 2000) return;
        _lastOverloadLogTicks = now;

        var msg = $"Audio backlog exceeded {_options.QueueCapacitySeconds:0}s; {dropped:0.0}s of audio dropped. Recognition may be behind.";
        _log.Warn(msg);
        Overload?.Invoke(this, new PipelineOverloadEventArgs
        {
            DroppedSeconds = dropped,
            QueueLength = _queue.Count,
            Message = msg
        });
    }

    private void OnCaptureError(object? sender, AudioCaptureErrorEventArgs e)
    {
        _log.Error($"Capture error: {e.Message}", e.Exception);
        ErrorOccurred?.Invoke(this, e.Message);
        if (e.Fatal)
        {
            SetState(TranscriptionState.Faulted, e.Message);
        }
    }

    private void OnDeviceChanged(object? sender, AudioDeviceChangedEventArgs e)
    {
        StatusChanged?.Invoke(this, Status with { Message = e.Message });
    }

    private void SetState(TranscriptionState state, string message)
    {
        State = state;
        Status = new TranscriptionStatus(state, message, _hotwords.Mode, _hotwords.ModeDetail);
        StatusChanged?.Invoke(this, Status);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_running) await StopAsync().ConfigureAwait(false);
        }
        catch { /* best effort */ }

        // StopAsync waits at most 5 s for the ASR loop; if it overshot, the loop is still decoding
        // with _asrSession. Let it finish (bounded, since the token is cancelled) before the session
        // and the native stream handle behind it are freed — otherwise the loop throws a
        // NullReferenceException on the freed handle, which surfaces as a "Recognition error".
        if (_asrTask is { } asrTask)
        {
            _cts?.Cancel();
            try
            {
                await asrTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _log.Warn($"ASR loop ended with an error during dispose: {ex.Message}");
            }
        }

        _capture.FramesAvailable -= OnFramesAvailable;
        _capture.Error -= OnCaptureError;
        _capture.DeviceChanged -= OnDeviceChanged;

        _asrSession?.Dispose();
        _cts?.Dispose();
        _signal.Dispose();
        await _capture.DisposeAsync().ConfigureAwait(false);
    }
}
