namespace LocalMeetingSubtitle.App.ViewModels;

/// <summary>
/// One selectable speaker in a combo box. A <c>null</c> <see cref="SpeakerId"/> represents the
/// "未知 / Unknown" option (no speaker assigned).
/// </summary>
public sealed class SpeakerChoice
{
    public SpeakerChoice(string? speakerId, string name)
    {
        SpeakerId = speakerId;
        Name = name;
    }

    /// <summary>The speaker's id; <c>null</c> for "未知 / Unknown".</summary>
    public string? SpeakerId { get; }

    /// <summary>Label shown in the combo box.</summary>
    public string Name { get; }
}
