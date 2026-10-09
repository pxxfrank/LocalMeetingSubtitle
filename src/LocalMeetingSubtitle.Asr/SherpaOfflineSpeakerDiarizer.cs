using LocalMeetingSubtitle.Core.Abstractions;
using LocalMeetingSubtitle.Core.Models;
using SherpaOnnx;

namespace LocalMeetingSubtitle.Asr;

/// <summary>
/// sherpa-onnx offline speaker diarization: a pyannote segmentation model + a speaker-embedding
/// model + agglomerative clustering. One blocking whole-file call.
///
/// CPU threads are capped low on purpose: this runs alongside (or after) the real-time recognizer,
/// which must keep its share of the CPU.
/// </summary>
public sealed class SherpaOfflineSpeakerDiarizer : ISpeakerDiarizationEngine
{
    private const int DefaultMaxThreads = 4;

    private readonly IAppLogger _log;
    private OfflineSpeakerDiarization? _diarizer;
    private bool _computeConfidence;

    public SherpaOfflineSpeakerDiarizer(IAppLogger? log = null)
        => _log = log ?? NullLogger.Instance;

    public string Id => "sherpa-onnx-offline-speaker-diarization";

    public bool IsInitialized { get; private set; }

    public int SampleRate => 16000;

    public DiarizationInitResult Initialize(DiarizationEngineOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!File.Exists(options.SegmentationModelPath))
        {
            return new DiarizationInitResult(false, "Segmentation model not found: " + options.SegmentationModelPath);
        }

        if (!File.Exists(options.EmbeddingModelPath))
        {
            return new DiarizationInitResult(false, "Speaker-embedding model not found: " + options.EmbeddingModelPath);
        }

        if (!SherpaNativeProbe.TryLoad(out var version, out var nativeError))
        {
            return new DiarizationInitResult(false, "sherpa-onnx native library could not be loaded. " + nativeError);
        }

        try
        {
            var threads = options.NumThreads > 0
                ? options.NumThreads
                : Math.Clamp(Environment.ProcessorCount / 4, 1, DefaultMaxThreads);

            var config = new OfflineSpeakerDiarizationConfig();
            config.Segmentation.Pyannote.Model = options.SegmentationModelPath;
            config.Segmentation.NumThreads = threads;
            config.Segmentation.Provider = options.Provider;

            config.Embedding.Model = options.EmbeddingModelPath;
            config.Embedding.NumThreads = threads;
            config.Embedding.Provider = options.Provider;

            config.Clustering.NumClusters = options.NumClusters;
            config.Clustering.Threshold = options.ClusteringThreshold;
            config.Clustering.ComputeConfidence = options.ComputeConfidence ? 1 : 0;

            config.MinDurationOn = options.MinDurationOn;
            config.MinDurationOff = options.MinDurationOff;

            var diarizer = new OfflineSpeakerDiarization(config);
            var sampleRate = diarizer.SampleRate;
            if (sampleRate != SampleRate)
            {
                diarizer.Dispose();
                return new DiarizationInitResult(false, $"Unexpected diarization sample rate {sampleRate} Hz (expected {SampleRate}).");
            }

            _diarizer = diarizer;
            _computeConfidence = options.ComputeConfidence;
            IsInitialized = true;
            _log.Info($"Diarization engine initialized: sherpa-onnx {version}, threads={threads}, clusters={(options.NumClusters > 0 ? options.NumClusters.ToString() : "auto")}, threshold={options.ClusteringThreshold}");
            return new DiarizationInitResult(true, $"sherpa-onnx {version} offline speaker diarization");
        }
        catch (Exception ex)
        {
            _log.Error("Diarization engine initialization failed", ex);
            return new DiarizationInitResult(false, ex.Message);
        }
    }

    public IReadOnlyList<DiarizationInterval> Process(
        float[] samples,
        IProgress<DiarizationProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (_diarizer is null)
        {
            throw new InvalidOperationException("Diarization engine is not initialized.");
        }

        ArgumentNullException.ThrowIfNull(samples);
        cancellationToken.ThrowIfCancellationRequested();

        OfflineSpeakerDiarizationSegment[] segments;
        if (progress is null)
        {
            segments = _diarizer.Process(samples);
        }
        else
        {
            // The native call aborts when the callback returns a non-zero value, which lets us honor
            // cancellation without aborting a thread.
            OfflineSpeakerDiarizationProgressCallback callback = (processed, total, _) =>
            {
                progress.Report(new DiarizationProgress(processed, total));
                return cancellationToken.IsCancellationRequested ? 1 : 0;
            };

            segments = _diarizer.ProcessWithCallback(samples, callback, IntPtr.Zero);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var result = new List<DiarizationInterval>(segments.Length);
        foreach (var segment in segments)
        {
            result.Add(new DiarizationInterval(
                TimeSpan.FromSeconds(segment.Start),
                TimeSpan.FromSeconds(segment.End),
                segment.Speaker,
                Confidence(segment.Confidence)));
        }

        return result;
    }

    private float? Confidence(float value)
    {
        // Only report confidence the model actually produced; never fabricate one.
        if (!_computeConfidence || float.IsNaN(value) || float.IsInfinity(value))
        {
            return null;
        }

        return value;
    }

    public void Dispose()
    {
        try
        {
            _diarizer?.Dispose();
        }
        catch (Exception ex)
        {
            _log.Error("Diarization engine dispose failed", ex);
        }
        finally
        {
            _diarizer = null;
            IsInitialized = false;
        }
    }
}
