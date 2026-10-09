using LocalMeetingSubtitle.Core.Abstractions;
using LocalMeetingSubtitle.Core.Models;

namespace LocalMeetingSubtitle.Core.Speakers;

/// <summary>
/// Orchestrates post-meeting diarization: decode audio → run the engine → create anonymous speaker
/// labels → align intervals to the session's subtitle segments → persist.
///
/// The whole heavy path runs on a single dedicated, below-normal-priority thread and only one run
/// may be in flight, so diarization can never starve the real-time recognizer. It reads the audio
/// file and the segments directly; it never touches the live capture path.
/// </summary>
public sealed class SpeakerDiarizationService : ISpeakerDiarizationService
{
    /// <summary>Longest audio accepted for analysis (guards memory: 1 h @16 kHz mono float ≈ 230 MB).</summary>
    public static readonly TimeSpan MaxAudioDuration = TimeSpan.FromHours(4);

    private const int TargetSampleRate = 16000;

    private readonly Func<ISpeakerDiarizationEngine> _engineFactory;
    private readonly IAudioFileLoader _loader;
    private readonly ISpeakerAlignmentService _alignment;
    private readonly ISpeakerRepository _speakers;
    private readonly ISubtitleRepository _subtitles;
    private readonly IAudioAssetRepository? _audioAssets;
    private readonly DiarizationEngineOptions _baseOptions;
    private readonly IAppLogger _log;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SpeakerDiarizationService(
        Func<ISpeakerDiarizationEngine> engineFactory,
        IAudioFileLoader loader,
        ISpeakerAlignmentService alignment,
        ISpeakerRepository speakers,
        ISubtitleRepository subtitles,
        DiarizationEngineOptions baseOptions,
        IAudioAssetRepository? audioAssets = null,
        IAppLogger? log = null)
    {
        _engineFactory = engineFactory ?? throw new ArgumentNullException(nameof(engineFactory));
        _loader = loader ?? throw new ArgumentNullException(nameof(loader));
        _alignment = alignment ?? throw new ArgumentNullException(nameof(alignment));
        _speakers = speakers ?? throw new ArgumentNullException(nameof(speakers));
        _subtitles = subtitles ?? throw new ArgumentNullException(nameof(subtitles));
        _baseOptions = baseOptions ?? throw new ArgumentNullException(nameof(baseOptions));
        _audioAssets = audioAssets;
        _log = log ?? NullLogger.Instance;
    }

    public bool IsBusy => _gate.CurrentCount == 0;

    public async Task<DiarizationResult> RunAsync(
        DiarizationRequest request,
        IProgress<DiarizationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!await _gate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return new DiarizationResult("", false, 0, 0, 0, "A diarization run is already in progress.");
        }

        try
        {
            var run = new DiarizationRun
            {
                SessionId = request.SessionId,
                AudioAssetId = request.AudioAssetId,
                Status = DiarizationRunStatus.Running,
                RequestedSpeakerCount = request.CountMode == SpeakerCountMode.Manual ? request.ManualSpeakerCount : 0,
                ClusteringThreshold = request.ClusteringThreshold,
                MinDurationOn = _baseOptions.MinDurationOn,
                MinDurationOff = _baseOptions.MinDurationOff,
                SegmentationModelId = Path.GetFileName(_baseOptions.SegmentationModelPath),
                EmbeddingModelId = Path.GetFileName(_baseOptions.EmbeddingModelPath)
            };

            await _speakers.SaveRunAsync(run, cancellationToken).ConfigureAwait(false);

            // Dedicated, below-normal-priority thread: the scheduler preempts diarization for the ASR worker.
            var completion = new TaskCompletionSource<DiarizationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                try
                {
                    completion.SetResult(RunCoreAsync(request, run, progress, cancellationToken).GetAwaiter().GetResult());
                }
                catch (Exception ex)
                {
                    completion.SetException(ex);
                }
            })
            {
                IsBackground = true,
                Priority = ThreadPriority.BelowNormal,
                Name = "speaker-diarization"
            };

            thread.Start();
            return await completion.Task.ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<DiarizationResult> RunCoreAsync(
        DiarizationRequest request,
        DiarizationRun run,
        IProgress<DiarizationProgress>? progress,
        CancellationToken cancellationToken)
    {
        ISpeakerDiarizationEngine? engine = null;
        try
        {
            if (!_loader.IsSupported(request.AudioFilePath) || !File.Exists(request.AudioFilePath))
            {
                return await FailAsync(run, "不支持的音频文件 / unsupported or missing audio file: " + request.AudioFilePath, cancellationToken)
                    .ConfigureAwait(false);
            }

            var duration = _loader.GetDuration(request.AudioFilePath);
            if (duration > MaxAudioDuration)
            {
                return await FailAsync(run, $"音频过长（{duration:hh\\:mm}），上限 {MaxAudioDuration:hh\\:mm} / audio too long", cancellationToken)
                    .ConfigureAwait(false);
            }

            var samples = _loader.LoadMono(request.AudioFilePath, TargetSampleRate, out _);
            run.DurationMs = (long)Math.Round(samples.Length * 1000.0 / TargetSampleRate);

            var options = BuildOptions(request);
            engine = _engineFactory();
            var init = engine.Initialize(options);
            if (!init.Ok)
            {
                return await FailAsync(run, "识别模型未就绪 / diarization model not ready: " + init.Message, cancellationToken)
                    .ConfigureAwait(false);
            }

            _log.Info($"Diarization run {run.RunId}: {samples.Length} samples ({duration}) from {request.AudioFilePath}");
            var intervals = engine.Process(samples, progress, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            // One anonymous Speaker per distinct engine cluster, labeled A, B, C, ...
            var rawIndices = intervals.Select(i => i.RawSpeakerIndex).Distinct().OrderBy(i => i).ToList();
            var speakersByRaw = new Dictionary<int, Speaker>();
            for (var i = 0; i < rawIndices.Count; i++)
            {
                var speaker = new Speaker
                {
                    SessionId = request.SessionId,
                    Label = LabelFor(i),
                    ColorArgb = SpeakerColorPalette.ColorFor(i),
                    SortOrder = i
                };
                await _speakers.UpsertSpeakerAsync(speaker, cancellationToken).ConfigureAwait(false);
                speakersByRaw[rawIndices[i]] = speaker;
            }

            var stored = intervals
                .Select(i => new SpeakerInterval
                {
                    RunId = run.RunId,
                    SessionId = request.SessionId,
                    Start = i.Start,
                    End = i.End,
                    RawSpeakerIndex = i.RawSpeakerIndex,
                    Confidence = i.Confidence
                })
                .ToList();
            await _speakers.ReplaceIntervalsAsync(run.RunId, stored, cancellationToken).ConfigureAwait(false);

            var segments = await _subtitles.GetSegmentsAsync(request.SessionId, cancellationToken).ConfigureAwait(false);
            var speakerIdByRaw = speakersByRaw.ToDictionary(kv => kv.Key, kv => kv.Value.SpeakerId);
            var outcomes = _alignment.Align(segments, intervals, speakerIdByRaw);

            var assignments = outcomes
                .Select(o => new SpeakerAssignment
                {
                    SessionId = request.SessionId,
                    SegmentId = o.SegmentId,
                    RunId = run.RunId,
                    SpeakerId = o.SpeakerId,
                    Source = SpeakerAssignmentSource.Auto,
                    Confidence = o.Confidence,
                    NeedsConfirmation = o.NeedsConfirmation,
                    UpdatedAt = DateTimeOffset.Now
                })
                .ToList();

            // Auto rows only: a manual reassignment survives re-analysis.
            await _speakers.UpsertAssignmentsAsync(assignments, overwriteManual: false, cancellationToken).ConfigureAwait(false);

            run.Status = DiarizationRunStatus.Succeeded;
            run.ResolvedSpeakerCount = speakersByRaw.Count;
            await _speakers.SaveRunAsync(run, cancellationToken).ConfigureAwait(false);

            var assigned = outcomes.Count(o => o.SpeakerId is not null);
            var needsConfirmation = outcomes.Count(o => o.NeedsConfirmation);
            _log.Info($"Diarization run {run.RunId} succeeded: {speakersByRaw.Count} speakers, {assigned}/{segments.Count} assigned, {needsConfirmation} need confirmation");
            return new DiarizationResult(run.RunId, true, speakersByRaw.Count, assigned, needsConfirmation, null);
        }
        catch (OperationCanceledException)
        {
            run.Status = DiarizationRunStatus.Cancelled;
            run.ErrorMessage = "cancelled";
            await TrySaveRunAsync(run).ConfigureAwait(false);
            _log.Info($"Diarization run {run.RunId} cancelled");
            return new DiarizationResult(run.RunId, false, 0, 0, 0, "cancelled");
        }
        catch (Exception ex)
        {
            _log.Error($"Diarization run {run.RunId} failed", ex);
            return await FailAsync(run, ex.Message, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            engine?.Dispose();
        }
    }

    private DiarizationEngineOptions BuildOptions(DiarizationRequest request)
    {
        var options = new DiarizationEngineOptions
        {
            SegmentationModelPath = _baseOptions.SegmentationModelPath,
            EmbeddingModelPath = _baseOptions.EmbeddingModelPath,
            NumThreads = _baseOptions.NumThreads,
            Provider = _baseOptions.Provider,
            ClusteringThreshold = (float)request.ClusteringThreshold,
            MinDurationOn = _baseOptions.MinDurationOn,
            MinDurationOff = _baseOptions.MinDurationOff,
            ComputeConfidence = _baseOptions.ComputeConfidence,
            NumClusters = request.CountMode == SpeakerCountMode.Manual && request.ManualSpeakerCount > 0
                ? request.ManualSpeakerCount
                : 0
        };

        return options;
    }

    private async Task<DiarizationResult> FailAsync(DiarizationRun run, string message, CancellationToken cancellationToken)
    {
        run.Status = DiarizationRunStatus.Failed;
        run.ErrorMessage = message;
        await TrySaveRunAsync(run, cancellationToken).ConfigureAwait(false);
        return new DiarizationResult(run.RunId, false, 0, 0, 0, message);
    }

    private async Task TrySaveRunAsync(DiarizationRun run, CancellationToken cancellationToken = default)
    {
        try
        {
            await _speakers.SaveRunAsync(run, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Error("Failed to persist diarization run state", ex);
        }
    }

    private static string LabelFor(int index) =>
        index < 26 ? ((char)('A' + index)).ToString() : "S" + (index + 1);
}
