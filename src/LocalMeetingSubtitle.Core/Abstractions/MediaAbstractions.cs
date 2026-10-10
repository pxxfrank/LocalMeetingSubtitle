using LocalMeetingSubtitle.Core.Models;

namespace LocalMeetingSubtitle.Core.Abstractions;

/// <summary>
/// Probes and decodes local audio/video files through the bundled FFmpeg toolchain.
/// Decoding is streaming: the whole media file is never held in memory at once.
/// </summary>
public interface IMediaDecodeService
{
    /// <summary>Reads container/stream metadata for a local file.</summary>
    Task<MediaInfo> ProbeAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>Streams the selected audio track as 16 kHz mono float32 blocks (never buffers the whole file).</summary>
    IAsyncEnumerable<PcmBlock> DecodeAsync(MediaDecodeRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Machine-readable reason a media operation failed.</summary>
public enum MediaErrorKind
{
    FileNotFound,
    NotReadable,
    NoAudioTrack,
    StreamIndexOutOfRange,
    UnsupportedFormat,
    ToolMissing,
    Cancelled,
    Failed
}

/// <summary>Thrown by <see cref="IMediaDecodeService"/> for every failure, carrying a <see cref="MediaErrorKind"/>.</summary>
public sealed class MediaDecodeException : Exception
{
    public MediaErrorKind Kind { get; }

    public MediaDecodeException(MediaErrorKind kind, string message, Exception? inner = null)
        : base(message, inner) => Kind = kind;
}

/// <summary>Absolute paths to the resolved FFmpeg executables.</summary>
public sealed record MediaToolPaths(string FfmpegPath, string FfprobePath);
