using LocalMeetingSubtitle.Core.Models;

namespace LocalMeetingSubtitle.Core.Abstractions;

/// <summary>
/// Optional post-meeting recording of the captured audio. The default configuration records
/// nothing: audio is only written when the user has explicitly opted in. Writes are enqueued and
/// must never block the live capture thread.
/// </summary>
public interface IRecordingService : IAsyncDisposable
{
    /// <summary>True while a recording is in progress.</summary>
    bool IsRecording { get; }

    /// <summary>The file currently being written, or <c>null</c> before the first start.</summary>
    string? CurrentPath { get; }

    /// <summary>Bytes written to the output file so far.</summary>
    long WrittenBytes { get; }

    /// <summary>Opens <paramref name="outputPath"/> and begins accepting frames.</summary>
    Task StartAsync(string outputPath);

    /// <summary>
    /// Enqueues a COPY of the captured frames and returns immediately — must never block the
    /// capture thread. Frames are dropped (and counted) when the writer falls behind.
    /// </summary>
    void Write(float[] interleaved, AudioFormat format);

    /// <summary>Completes the queue, flushes the file and returns a summary of what was written.</summary>
    Task<RecordingResult> StopAsync();
}

/// <summary>Summary of a finished recording.</summary>
public readonly record struct RecordingResult(string Path, TimeSpan Duration, long SizeBytes, int SampleRate, int Channels);
