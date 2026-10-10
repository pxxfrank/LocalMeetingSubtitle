using System.Runtime.CompilerServices;
using LocalMeetingSubtitle.Core.Abstractions;
using LocalMeetingSubtitle.Core.Models;

namespace LocalMeetingSubtitle.UnitTests;

/// <summary>Progress that reports synchronously so assertions never race the callback.</summary>
internal sealed class SynchronousProgress<T> : IProgress<T>
{
    private readonly Action<T> _handler;

    public SynchronousProgress(Action<T> handler) => _handler = handler;

    public void Report(T value) => _handler(value);
}

/// <summary>An ASR engine that returns one scripted text per decoded segment.</summary>
internal sealed class ScriptedSegmentsAsrEngine : IAsrEngine
{
    private readonly Queue<string> _texts;

    public ScriptedSegmentsAsrEngine(params string[] texts)
    {
        _texts = new Queue<string>(texts);
        Capabilities = new AsrCapabilities { Streaming = false, Description = "scripted-segments" };
    }

    public bool Initialized { get; init; } = true;

    public string Id => "scripted-segments";
    public bool IsInitialized => Initialized;
    public AsrCapabilities Capabilities { get; }
    /// <summary>Number of sessions the engine handed out.</summary>
    public int SessionsCreated { get; private set; }

    public Task<AsrInitResult> InitializeAsync(AsrEngineOptions options, CancellationToken cancellationToken = default) =>
        Task.FromResult(new AsrInitResult(AsrInitStatus.Success, "scripted", Capabilities));

    public IAsrSession CreateSession()
    {
        SessionsCreated++;
        return new Session(_texts);
    }

    public void Dispose() { }

    private sealed class Session : IAsrSession
    {
        private readonly Queue<string> _texts;
        private bool _pending;
        private AsrDecodeResult _result;

        public Session(Queue<string> texts) => _texts = texts;

        public void AcceptWaveform(ReadOnlySpan<float> samples, int sampleRate) => _pending = true;
        public bool IsReady() => _pending;

        public void Decode()
        {
            _pending = false;
            _result = new AsrDecodeResult(_texts.Count > 0 ? _texts.Dequeue() : "", true);
        }

        public AsrDecodeResult GetResult() => _result;

        public void Reset()
        {
            _pending = false;
            _result = default;
        }

        public void InputFinished() { }
        public void Dispose() { }
    }
}

/// <summary>Media decoder fed from a scripted list of PCM blocks.</summary>
internal sealed class FakeMediaDecodeService : IMediaDecodeService
{
    private readonly MediaInfo _info;
    private readonly IReadOnlyList<PcmBlock> _blocks;

    public FakeMediaDecodeService(MediaInfo info, IReadOnlyList<PcmBlock> blocks)
    {
        _info = info;
        _blocks = blocks;
    }

    public MediaDecodeException? ProbeError { get; set; }
    public int DecodeCalls { get; private set; }
    public MediaDecodeRequest? LastRequest { get; private set; }
    public bool Cancelled { get; init; }
    /// <summary>Cancels mid-stream after the first block, to exercise cleanup paths.</summary>
    public CancellationTokenSource? CancelAfterFirstBlock { get; set; }

    public Task<MediaInfo> ProbeAsync(string path, CancellationToken cancellationToken = default) =>
        ProbeError is null ? Task.FromResult(_info) : Task.FromException<MediaInfo>(ProbeError);

    public async IAsyncEnumerable<PcmBlock> DecodeAsync(
        MediaDecodeRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        DecodeCalls++;
        LastRequest = request;

        if (Cancelled)
        {
            throw new MediaDecodeException(MediaErrorKind.Cancelled, "decode cancelled");
        }

        int index = 0;
        foreach (var block in _blocks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return block;
            if (index++ == 0)
            {
                CancelAfterFirstBlock?.Cancel();
            }

            await Task.Yield();
        }
    }
}

/// <summary>
/// Diarization double: records the request and lets the test script what the real service would
/// persist (speakers + per-segment assignments).
/// </summary>
internal sealed class FakeDiarizationService : ISpeakerDiarizationService
{
    public bool Busy { get; set; }
    public bool IsBusy => Busy;

    public DiarizationResult Result { get; set; } = new("run-1", true, 2, 0, 0, null);
    public Func<string, CancellationToken, Task>? OnRun { get; set; }

    public int RunCount { get; private set; }
    public DiarizationRequest? LastRequest { get; private set; }

    public async Task<DiarizationResult> RunAsync(
        DiarizationRequest request,
        IProgress<DiarizationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        RunCount++;
        LastRequest = request;
        progress?.Report(new DiarizationProgress(1, 1));

        if (OnRun is not null)
        {
            await OnRun(request.SessionId, cancellationToken).ConfigureAwait(false);
        }

        return Result;
    }
}
