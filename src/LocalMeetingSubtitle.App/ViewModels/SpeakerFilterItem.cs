namespace LocalMeetingSubtitle.App.ViewModels;

/// <summary>
/// One entry in the main window's by-speaker filter. A <c>null</c> <see cref="SpeakerId"/> is the
/// "全部 / All" option; every other entry maps to a single (non-merged) speaker.
/// </summary>
public sealed class SpeakerFilterItem
{
    public SpeakerFilterItem(string? speakerId, string name)
    {
        SpeakerId = speakerId;
        Name = name;
    }

    /// <summary>The speaker to filter by; <c>null</c> means "show every speaker".</summary>
    public string? SpeakerId { get; }

    /// <summary>Label shown in the filter combo box.</summary>
    public string Name { get; }
}
