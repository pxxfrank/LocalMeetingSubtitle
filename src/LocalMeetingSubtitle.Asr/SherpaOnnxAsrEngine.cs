using LocalMeetingSubtitle.Core.Abstractions;
using LocalMeetingSubtitle.Core.Models;
using SherpaOnnx;

namespace LocalMeetingSubtitle.Asr;

/// <summary>
/// Real sherpa-onnx engine. Supports streaming transducers (native partial output + endpointing
/// + model-level hotwords via modified_beam_search) and offline models such as SenseVoice.
/// </summary>
public sealed class SherpaOnnxAsrEngine : IAsrEngine
{
    private readonly IAppLogger _log;
    private AsrEngineOptions? _options;
    private OnlineRecognizer? _online;
    private OfflineRecognizer? _offline;

    public SherpaOnnxAsrEngine(IAppLogger? log = null)
    {
        _log = log ?? NullLogger.Instance;
    }

    public string Id { get; private set; } = "sherpa-onnx";
    public bool IsInitialized { get; private set; }
    public AsrCapabilities Capabilities { get; private set; } = new() { Streaming = false, Description = "not initialized" };

    public Task<AsrInitResult> InitializeAsync(AsrEngineOptions options, CancellationToken cancellationToken = default)
    {
        _options = options;
        Id = $"sherpa-onnx:{options.Kind}";

        var missing = RequiredFiles(options).Where(f => !File.Exists(f)).ToList();
        if (missing.Count > 0)
        {
            return Task.FromResult(new AsrInitResult(AsrInitStatus.ModelNotFound,
                "Model file(s) not found: " + string.Join(", ", missing.Select(Path.GetFileName))));
        }

        if (!SherpaNativeProbe.TryLoad(out var version, out var nativeError))
        {
            return Task.FromResult(new AsrInitResult(AsrInitStatus.NativeLibraryMissing,
                "sherpa-onnx native library could not be loaded. Ensure the win-x64 native DLLs are present. " + nativeError));
        }

        try
        {
            if (options.Kind == AsrModelKind.Offline)
            {
                _offline = BuildOffline(options);
                Capabilities = new AsrCapabilities
                {
                    Streaming = false,
                    ModelLevelHotwords = options.HotwordsFile != null,
                    TokenTimestamps = true,
                    Chinese = true,
                    English = true,
                    Description = $"SenseVoice (offline), sherpa-onnx {version}"
                };
            }
            else
            {
                _online = BuildOnline(options);
                bool hotwordsActive = !string.IsNullOrEmpty(options.HotwordsFile) && File.Exists(options.HotwordsFile);
                string effectiveDecoding = hotwordsActive && options.Kind == AsrModelKind.StreamingTransducer
                    ? "modified_beam_search"
                    : options.DecodingMethod;
                Capabilities = new AsrCapabilities
                {
                    Streaming = true,
                    ModelLevelHotwords = options.Kind == AsrModelKind.StreamingTransducer,
                    TokenTimestamps = true,
                    Chinese = true,
                    English = options.Kind == AsrModelKind.StreamingTransducer,
                    Description = $"Streaming zipformer, sherpa-onnx {version}, decoding={effectiveDecoding}, hotwords={(hotwordsActive ? "on" : "off")}"
                };
            }

            IsInitialized = true;
            _log.Info($"ASR engine initialized: {Capabilities.Description}");
            return Task.FromResult(new AsrInitResult(AsrInitStatus.Success, Capabilities.Description, Capabilities));
        }
        catch (Exception ex)
        {
            _log.Error("ASR engine initialization failed", ex);
            return Task.FromResult(new AsrInitResult(AsrInitStatus.Failed, ex.Message));
        }
    }

    public IAsrSession CreateSession()
    {
        if (!IsInitialized) throw new InvalidOperationException("Engine is not initialized.");
        return _offline != null ? new OfflineSession(_offline) : new OnlineSession(_online!, _log);
    }

    public void Dispose()
    {
        _online?.Dispose();
        _offline?.Dispose();
        _online = null;
        _offline = null;
        IsInitialized = false;
    }

    private static IEnumerable<string> RequiredFiles(AsrEngineOptions o)
    {
        var dir = o.ModelDirectory;
        if (o.Kind == AsrModelKind.Offline)
        {
            yield return Path.Combine(dir, o.ModelFileName);
            yield return Path.Combine(dir, o.TokensFileName);
        }
        else
        {
            yield return Path.Combine(dir, o.EncoderFileName);
            yield return Path.Combine(dir, o.DecoderFileName);
            yield return Path.Combine(dir, o.JoinerFileName);
            yield return Path.Combine(dir, o.TokensFileName);
        }
    }

    private OnlineRecognizer BuildOnline(AsrEngineOptions o)
    {
        var config = new OnlineRecognizerConfig();
        config.FeatConfig.SampleRate = o.SampleRate;
        config.FeatConfig.FeatureDim = o.FeatureDim;

        config.ModelConfig.Transducer.Encoder = Path.Combine(o.ModelDirectory, o.EncoderFileName);
        config.ModelConfig.Transducer.Decoder = Path.Combine(o.ModelDirectory, o.DecoderFileName);
        config.ModelConfig.Transducer.Joiner = Path.Combine(o.ModelDirectory, o.JoinerFileName);
        config.ModelConfig.Tokens = Path.Combine(o.ModelDirectory, o.TokensFileName);
        config.ModelConfig.NumThreads = o.NumThreads > 0 ? o.NumThreads : Math.Max(1, Environment.ProcessorCount / 2);
        config.ModelConfig.Provider = o.Provider;
        // ModelType left empty so sherpa-onnx auto-detects (setting an invalid literal logs a warning).

        bool hotwords = !string.IsNullOrEmpty(o.HotwordsFile) && File.Exists(o.HotwordsFile);
        config.DecodingMethod = hotwords ? "modified_beam_search" : o.DecodingMethod;
        config.MaxActivePaths = 4;

        config.EnableEndpoint = o.EnableEndpoint ? 1 : 0;
        config.Rule1MinTrailingSilence = o.Rule1MinTrailingSilence;
        config.Rule2MinTrailingSilence = o.Rule2MinTrailingSilence;
        config.Rule3MinUtteranceLength = o.Rule3MinUtteranceLength;

        if (hotwords)
        {
            config.HotwordsFile = o.HotwordsFile!;
            config.HotwordsScore = o.HotwordsScore;
        }

        return new OnlineRecognizer(config);
    }

    private OfflineRecognizer BuildOffline(AsrEngineOptions o)
    {
        var config = new OfflineRecognizerConfig();
        config.FeatConfig.SampleRate = o.SampleRate;
        config.FeatConfig.FeatureDim = o.FeatureDim;

        config.ModelConfig.SenseVoice.Model = Path.Combine(o.ModelDirectory, o.ModelFileName);
        config.ModelConfig.SenseVoice.Language = o.Language;
        config.ModelConfig.SenseVoice.UseInverseTextNormalization = o.UseInverseTextNormalization ? 1 : 0;
        config.ModelConfig.Tokens = Path.Combine(o.ModelDirectory, o.TokensFileName);
        config.ModelConfig.NumThreads = o.NumThreads > 0 ? o.NumThreads : Math.Max(1, Environment.ProcessorCount / 2);
        config.ModelConfig.Provider = o.Provider;
        config.ModelConfig.ModelType = "sense_voice";
        config.DecodingMethod = "greedy_search";

        if (!string.IsNullOrEmpty(o.HotwordsFile) && File.Exists(o.HotwordsFile))
        {
            config.HotwordsFile = o.HotwordsFile;
            config.HotwordsScore = o.HotwordsScore;
        }

        return new OfflineRecognizer(config);
    }

    private sealed class OnlineSession : IAsrSession
    {
        private readonly OnlineRecognizer _recognizer;
        private readonly IAppLogger _log;
        private readonly OnlineStream _stream;
        private long _acceptedSamples;
        private bool _hasDecoded;
        private bool _emptyResultWarned;

        public OnlineSession(OnlineRecognizer recognizer, IAppLogger log)
        {
            _recognizer = recognizer;
            _log = log;
            _stream = recognizer.CreateStream();
        }

        public void AcceptWaveform(ReadOnlySpan<float> samples, int sampleRate)
        {
            _acceptedSamples += samples.Length;
            _stream.AcceptWaveform(sampleRate, samples.ToArray());
        }

        public bool IsReady() => _recognizer.IsReady(_stream);

        public void Decode()
        {
            _recognizer.Decode(_stream);
            _hasDecoded = true;
        }

        public AsrDecodeResult GetResult()
        {
            if (!_hasDecoded)
            {
                // Nothing has been decoded for this stream yet, so there is no hypothesis to read.
                // sherpa-onnx's C# wrapper throws NullReferenceException from OnlineRecognizerResult
                // in exactly this state, so do not call it.
                if (!_emptyResultWarned)
                {
                    _emptyResultWarned = true;
                    _log.Warn($"sherpa-onnx stream has not produced a result yet after {_acceptedSamples} "
                              + $"accepted sample(s) (IsReady={SafeIsReady()}); skipping the result read.");
                }

                return new AsrDecodeResult("", false);
            }

            try
            {
                var result = _recognizer.GetResult(_stream);
                bool endpoint = _recognizer.IsEndpoint(_stream);
                return new AsrDecodeResult(result.Text ?? "", endpoint, result.Tokens, result.Timestamps);
            }
            catch (NullReferenceException)
            {
                // Belt and braces: the wrapper can also throw for a decoded-but-empty stream.
                _log.Debug("sherpa-onnx GetResult returned no result for a decoded stream; treated as empty.");
                return new AsrDecodeResult("", false);
            }
        }

        private bool SafeIsReady()
        {
            try { return _recognizer.IsReady(_stream); }
            catch { return false; }
        }

        public void Reset()
        {
            _recognizer.Reset(_stream);
            _hasDecoded = false;
        }
        public void InputFinished() => _stream.InputFinished();
        public void Dispose() => _stream.Dispose();
    }

    private sealed class OfflineSession : IAsrSession
    {
        private readonly OfflineRecognizer _recognizer;
        private OfflineStream _stream;
        private bool _consumed;

        public OfflineSession(OfflineRecognizer recognizer)
        {
            _recognizer = recognizer;
            _stream = recognizer.CreateStream();
        }

        public void AcceptWaveform(ReadOnlySpan<float> samples, int sampleRate)
        {
            if (_consumed)
            {
                _stream.Dispose();
                _stream = _recognizer.CreateStream();
                _consumed = false;
            }
            _stream.AcceptWaveform(sampleRate, samples.ToArray());
        }

        public bool IsReady() => true;
        public void Decode() => _recognizer.Decode(_stream);

        public AsrDecodeResult GetResult()
        {
            var result = _stream.Result;
            _consumed = true;
            return new AsrDecodeResult(result.Text ?? "", true, result.Tokens, result.Timestamps);
        }

        public void Reset()
        {
            _stream.Dispose();
            _stream = _recognizer.CreateStream();
            _consumed = false;
        }

        public void InputFinished() { }
        public void Dispose() => _stream.Dispose();
    }
}
