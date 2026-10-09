namespace LocalMeetingSubtitle.Core.Models;

/// <summary>Describes a PCM sample format.</summary>
public readonly record struct AudioFormat(int SampleRate, int Channels, int BitsPerSample)
{
    public static AudioFormat Float32(int sampleRate, int channels) => new(sampleRate, channels, 32);
    public bool IsFloat => BitsPerSample == 32;
    public override string ToString() => $"{SampleRate} Hz, {Channels} ch, {BitsPerSample}-bit";
}

/// <summary>A selectable audio capture (render/loopback) endpoint.</summary>
public sealed record AudioDeviceInfo(string Id, string FriendlyName, bool IsDefault, bool IsLoopbackCapable)
{
    public override string ToString() => IsDefault ? $"{FriendlyName} (default)" : FriendlyName;
}

public enum CaptureState
{
    Stopped,
    Starting,
    Running,
    Paused,
    Faulted
}

public enum SessionStatus
{
    Recording,
    Paused,
    Completed,
    Aborted
}

public enum TranscriptionState
{
    Idle,
    LoadingModel,
    Ready,
    Transcribing,
    Paused,
    Faulted
}

public enum SubtitleKind
{
    Partial,
    Final
}

public enum HotwordMode
{
    /// <summary>Model-level boosting (transducer + modified_beam_search) is active.</summary>
    ModelLevel,
    /// <summary>Model doesn't support boosting; only text post-correction applies.</summary>
    TextCorrectionOnly,
    /// <summary>Hotwords were supplied but the loaded model cannot use them.</summary>
    NotSupported,
    /// <summary>Hotword loading failed.</summary>
    LoadFailed
}

public enum ExportFormat
{
    Txt,
    Srt,
    Markdown
}
