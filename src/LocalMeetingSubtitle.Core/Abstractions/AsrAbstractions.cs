using LocalMeetingSubtitle.Core.Models;

namespace LocalMeetingSubtitle.Core.Abstractions;

/// <summary>
/// A replaceable speech recognition engine. Streaming engines expose incremental
/// partial results; offline engines produce a result only after <see cref="IAsrSession.InputFinished"/>.
/// </summary>
public interface IAsrEngine : IDisposable
{
    string Id { get; }
    bool IsInitialized { get; }
    AsrCapabilities Capabilities { get; }

    Task<AsrInitResult> InitializeAsync(AsrEngineOptions options, CancellationToken cancellationToken = default);

    /// <summary>Creates an independent recognition session (one per audio stream).</summary>
    IAsrSession CreateSession();
}

/// <summary>One recognition stream. Not thread-safe: feed and decode from a single thread.</summary>
public interface IAsrSession : IDisposable
{
    void AcceptWaveform(ReadOnlySpan<float> samples, int sampleRate);
    /// <summary>True when enough audio is buffered to run <see cref="Decode"/>.</summary>
    bool IsReady();
    void Decode();
    /// <summary>Current hypothesis + endpoint flag.</summary>
    AsrDecodeResult GetResult();
    /// <summary>Clears decoding state after an endpoint (keeps the stream reusable).</summary>
    void Reset();
    /// <summary>Signals that no more audio will be accepted (flush).</summary>
    void InputFinished();
}
