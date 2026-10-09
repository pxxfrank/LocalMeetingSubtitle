using System.Text;
using LocalMeetingSubtitle.Core.Abstractions;
using LocalMeetingSubtitle.Core.Audio;
using LocalMeetingSubtitle.Core.Models;
using LocalMeetingSubtitle.Storage;

namespace LocalMeetingSubtitle.IntegrationTests;

/// <summary>Shared, in-test doubles used to drive the pipeline without real hardware.</summary>
internal sealed class FakeAudioCaptureService : IAudioCaptureService
{
    public CaptureState State { get; private set; } = CaptureState.Stopped;
    public string? CurrentDeviceId { get; private set; }

    public event EventHandler<AudioFramesEventArgs>? FramesAvailable;
    public event EventHandler<double>? LevelChanged;
    public event EventHandler<AudioCaptureErrorEventArgs>? Error;
    public event EventHandler<AudioDeviceChangedEventArgs>? DeviceChanged;

    public int RaisedFrameCount { get; private set; }

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

    /// <summary>Simulates the capture thread surfacing one buffer.</summary>
    public void RaiseFrames(float[] samples, AudioFormat format)
    {
        RaisedFrameCount++;
        FramesAvailable?.Invoke(this, new AudioFramesEventArgs
        {
            Samples = samples,
            Format = format,
            CapturedAt = DateTimeOffset.Now
        });
    }

    public void RaiseError(string message, bool fatal = false) =>
        Error?.Invoke(this, new AudioCaptureErrorEventArgs { Message = message, Fatal = fatal });
}

/// <summary>Pass-through preprocessor: tests feed already-mono audio at the target rate.</summary>
internal sealed class FakePreprocessor : IAudioPreprocessor
{
    public FakePreprocessor(int targetSampleRate = 16000) =>
        TargetFormat = AudioFormat.Float32(targetSampleRate, 1);

    public AudioFormat TargetFormat { get; }
    public int ProcessCalls { get; private set; }
    public int ResetCalls { get; private set; }

    public float[] Process(ReadOnlySpan<float> interleaved, AudioFormat sourceFormat)
    {
        ProcessCalls++;
        return interleaved.ToArray();
    }

    public void Reset() => ResetCalls++;
}

/// <summary>Minimal hotword service: no model boosting, identity (or scripted) corrections.</summary>
internal sealed class FakeHotwordService : IHotwordService
{
    public IReadOnlyList<Hotword> AllHotwords { get; set; } = Array.Empty<Hotword>();
    public IReadOnlyList<Hotword> ActiveHotwords => AllHotwords.Where(h => h.Enabled).ToList();
    public IReadOnlyList<TextCorrectionRule> ActiveRules { get; set; } = Array.Empty<TextCorrectionRule>();
    public HotwordMode Mode { get; private set; } = HotwordMode.TextCorrectionOnly;
    public string? ModeDetail => null;
    public bool UseBuiltInLexicon { get; set; }

    public Func<string, string> Correction { get; set; } = static s => s;

    public void SetEngineCapability(bool supportsModelHotwords, bool modelCanBeRebuiltWithoutDataLoss) =>
        Mode = supportsModelHotwords ? HotwordMode.ModelLevel : HotwordMode.TextCorrectionOnly;

    public Task ReloadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public HotwordValidationResult Validate(string text) => HotwordValidationResult.Ok;
    public string? WriteModelHotwordFile(string destinationPath) => null;
    public string ApplyCorrections(string text) => Correction(text);

    public bool ApplyCorrections(SubtitleSegment segment)
    {
        var corrected = ApplyCorrections(segment.OriginalText);
        bool changed = !string.Equals(corrected, segment.CorrectedText, StringComparison.Ordinal);
        segment.CorrectedText = corrected;
        return changed;
    }
}

/// <summary>
/// Streaming ASR test engine driven by a scripted sequence of hypotheses. It consumes one
/// scripted result per fed buffer (unlike the shipped MockAsrEngine, whose <c>Decode</c> does
/// not advance the script and therefore spins forever in the pipeline's streaming loop).
/// </summary>
internal sealed class ScriptedStreamingEngine : IAsrEngine
{
    private readonly IReadOnlyList<AsrDecodeResult> _script;
    private readonly TimeSpan _decodeDelay;

    public ScriptedStreamingEngine(
        IEnumerable<AsrDecodeResult>? script = null,
        TimeSpan decodeDelay = default,
        bool streaming = true)
    {
        _script = script?.ToList() ?? new List<AsrDecodeResult>();
        _decodeDelay = decodeDelay;
        Capabilities = new AsrCapabilities
        {
            Streaming = streaming,
            ModelLevelHotwords = false,
            Chinese = true,
            Description = "scripted"
        };
        IsInitialized = true;
    }

    public string Id => "scripted";
    public bool IsInitialized { get; private set; }
    public AsrCapabilities Capabilities { get; }

    public Task<AsrInitResult> InitializeAsync(AsrEngineOptions options, CancellationToken cancellationToken = default) =>
        Task.FromResult(new AsrInitResult(AsrInitStatus.Success, "scripted", Capabilities));

    public IAsrSession CreateSession() => new ScriptedSession(_script, _decodeDelay);

    public void Dispose() => IsInitialized = false;

    private sealed class ScriptedSession : IAsrSession
    {
        private readonly IReadOnlyList<AsrDecodeResult> _script;
        private readonly TimeSpan _decodeDelay;
        private int _index;
        private AsrDecodeResult _current;
        private bool _hasData;

        public ScriptedSession(IReadOnlyList<AsrDecodeResult> script, TimeSpan decodeDelay)
        {
            _script = script;
            _decodeDelay = decodeDelay;
        }

        public void AcceptWaveform(ReadOnlySpan<float> samples, int sampleRate) => _hasData = true;

        public bool IsReady() => _hasData;

        public void Decode()
        {
            if (_decodeDelay > TimeSpan.Zero)
            {
                Thread.Sleep(_decodeDelay);
            }

            _hasData = false;
            _current = _index < _script.Count ? _script[_index++] : new AsrDecodeResult("", false);
        }

        public AsrDecodeResult GetResult() => _current;

        public void Reset()
        {
            _current = default;
            _hasData = false;
        }

        public void InputFinished() { }
        public void Dispose() { }
    }
}

/// <summary>Captures the metrics the pipeline reports so queue bounds can be asserted.</summary>
internal sealed class FakePerformanceMonitor : IPerformanceMonitor
{
    public bool IsRunning { get; private set; }
    public double LastRtf { get; private set; }
    public int ReportQueueCalls { get; private set; }
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
        ReportQueueCalls++;
        MaxReportedQueueSamples = Math.Max(MaxReportedQueueSamples, maxLength);
        MaxReportedDroppedSeconds = Math.Max(MaxReportedDroppedSeconds, droppedSeconds);
    }

    public void Dispose() { }
}

/// <summary>Throwaway SQLite database (with WAL siblings) in a private temp directory.</summary>
internal sealed class TempSqlite : IAsyncDisposable
{
    private readonly string _directory;

    private TempSqlite()
    {
        _directory = Path.Combine(Path.GetTempPath(), "lms-it-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        Database = new SqliteDatabase(Path.Combine(_directory, "subtitles.db"));
    }

    public SqliteDatabase Database { get; }

    public static async Task<TempSqlite> CreateAsync()
    {
        var temp = new TempSqlite();
        await temp.Database.InitializeAsync();
        return temp;
    }

    public SqliteSubtitleRepository CreateSubtitleRepository() => new(Database);
    public SqliteHotwordRepository CreateHotwordRepository() => new(Database);

    public async ValueTask DisposeAsync()
    {
        await Database.DisposeAsync();

        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (Directory.Exists(_directory))
                {
                    Directory.Delete(_directory, recursive: true);
                }

                return;
            }
            catch (IOException)
            {
                Thread.Sleep(25);
            }
            catch (UnauthorizedAccessException)
            {
                Thread.Sleep(25);
            }
        }
    }
}

/// <summary>Synthetic mono audio helpers for driving the segmenter/pipeline.</summary>
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

/// <summary>Locates the repository <c>models</c> directory by walking up from the test binaries.</summary>
internal static class ModelLocator
{
    public static string? FindModelsRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "models");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }
        return null;
    }
}

/// <summary>Reads a canonical PCM16 WAV file into mono float samples.</summary>
internal static class WavReader
{
    public static (float[] Samples, int SampleRate) ReadMono16BitPcm(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 12 ||
            Encoding.ASCII.GetString(bytes, 0, 4) != "RIFF" ||
            Encoding.ASCII.GetString(bytes, 8, 4) != "WAVE")
        {
            throw new InvalidDataException("Not a RIFF/WAVE file.");
        }

        int position = 12;
        int sampleRate = 0;
        int channels = 0;
        int bits = 0;
        int audioFormat = 0;
        float[]? samples = null;

        while (position + 8 <= bytes.Length)
        {
            string id = Encoding.ASCII.GetString(bytes, position, 4);
            int size = BitConverter.ToInt32(bytes, position + 4);
            int dataStart = position + 8;

            if (id == "fmt ")
            {
                audioFormat = BitConverter.ToInt16(bytes, dataStart);
                channels = BitConverter.ToInt16(bytes, dataStart + 2);
                sampleRate = BitConverter.ToInt32(bytes, dataStart + 4);
                bits = BitConverter.ToInt16(bytes, dataStart + 14);
            }
            else if (id == "data")
            {
                int count = Math.Min(size, bytes.Length - dataStart);
                if (audioFormat == 1 && bits == 16)
                {
                    var shorts = new short[count / 2];
                    Buffer.BlockCopy(bytes, dataStart, shorts, 0, shorts.Length * 2);
                    samples = ChannelConverter.Int16ToFloat(shorts);
                }
            }

            position = dataStart + size + (size % 2);
        }

        if (samples is null)
        {
            throw new InvalidDataException("WAV file has no 16-bit PCM data chunk.");
        }

        if (channels > 1)
        {
            samples = ChannelConverter.ToMono(samples, channels);
        }

        return (samples, sampleRate);
    }
}
