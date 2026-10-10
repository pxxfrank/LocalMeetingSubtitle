namespace LocalMeetingSubtitle.Core.Models;

/// <summary>The dominant medium carried by a local file.</summary>
public enum MediaKind
{
    Audio,
    Video,
    Unknown
}

/// <summary>One audio stream inside a container, as reported by the media probe.</summary>
public sealed class AudioStreamInfo
{
    /// <summary>Absolute stream index inside the container (ffprobe's <c>streams[].index</c>).</summary>
    public int Index { get; init; }
    public string Codec { get; init; } = "";
    public int Channels { get; init; }
    public int SampleRate { get; init; }
    /// <summary>ISO 639 language tag from the stream metadata, when present.</summary>
    public string? Language { get; init; }
    /// <summary>Stream start relative to the container timeline.</summary>
    public TimeSpan? StartTime { get; init; }
    public TimeSpan? Duration { get; init; }
    public string Title { get; init; } = "";
}

/// <summary>Result of probing a local media file (container + streams + duration).</summary>
public sealed class MediaInfo
{
    public string Path { get; init; } = "";
    public string FileName { get; init; } = "";
    public long FileSize { get; init; }
    public TimeSpan Duration { get; init; }
    public MediaKind Kind { get; init; }
    public string ContainerFormat { get; init; } = "";
    public IReadOnlyList<AudioStreamInfo> AudioStreams { get; init; } = Array.Empty<AudioStreamInfo>();

    /// <summary>True when at least one audio stream was found. Always consistent with <see cref="AudioStreams"/>.</summary>
    public bool HasAudio => AudioStreams.Count > 0;

    /// <summary>Non-fatal notes gathered while probing (e.g. a missing duration).</summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

/// <summary>
/// A block of mono float32 samples plus its absolute position on the media timeline.
/// Blocks are produced incrementally so a whole file is never buffered in memory.
/// </summary>
public readonly record struct PcmBlock(float[] Samples, TimeSpan Start, int SampleRate);

/// <summary>
/// Asks the decoder to stream one audio track of a local file as 16 kHz mono float32.
/// A null <paramref name="AudioStreamIndex"/> selects the first audio stream.
/// <paramref name="StartOffset"/> seeks into the stream (used to resume an interrupted job);
/// emitted <see cref="PcmBlock.Start"/> values stay absolute on the media timeline.
/// </summary>
public sealed record MediaDecodeRequest(
    string Path,
    int? AudioStreamIndex = null,
    int TargetSampleRate = 16000,
    TimeSpan StartOffset = default);
