using LocalMeetingSubtitle.Core.Abstractions;
using LocalMeetingSubtitle.Core.Models;

namespace LocalMeetingSubtitle.Asr;

/// <summary>
/// Deterministic, offline test double used only by automated tests (and never wired in Release).
/// Replays a scripted sequence of hypotheses so the pipeline's partial/final/dedup logic can be
/// tested without a multi-hundred-megabyte model.
/// </summary>
public sealed class MockAsrEngine : IAsrEngine
{
    private readonly IReadOnlyList<AsrDecodeResult> _script;
    private readonly bool _streaming;
    private readonly bool _supportsHotwords;

    public MockAsrEngine(IEnumerable<AsrDecodeResult> script, bool streaming = true, bool supportsHotwords = false)
    {
        _script = script.ToList();
        _streaming = streaming;
        _supportsHotwords = supportsHotwords;
        Capabilities = new AsrCapabilities
        {
            Streaming = streaming,
            ModelLevelHotwords = supportsHotwords,
            TokenTimestamps = false,
            Description = "mock"
        };
        IsInitialized = true;
    }

    public string Id => "mock";
    public bool IsInitialized { get; private set; }
    public AsrCapabilities Capabilities { get; }

    public Task<AsrInitResult> InitializeAsync(AsrEngineOptions options, CancellationToken cancellationToken = default)
        => Task.FromResult(new AsrInitResult(AsrInitStatus.Success, "mock", Capabilities));

    public IAsrSession CreateSession() => new ScriptedSession(_script);

    public void Dispose() => IsInitialized = false;

    private sealed class ScriptedSession : IAsrSession
    {
        private readonly IReadOnlyList<AsrDecodeResult> _script;
        private int _index;
        private bool _ready;
        private AsrDecodeResult _current = new("", false);

        public ScriptedSession(IReadOnlyList<AsrDecodeResult> script) => _script = script;

        // One scripted hypothesis is consumed per accepted chunk, mimicking a streaming engine
        // that becomes ready exactly once per buffer (and never spinning forever).
        public void AcceptWaveform(ReadOnlySpan<float> samples, int sampleRate) => _ready = _index < _script.Count;
        public bool IsReady() => _ready;

        public void Decode()
        {
            if (!_ready) return;
            _current = _script[_index++];
            _ready = false;
        }

        public AsrDecodeResult GetResult() => _current;

        public void Reset() { }
        public void InputFinished() { }
        public void Dispose() { }
    }
}
