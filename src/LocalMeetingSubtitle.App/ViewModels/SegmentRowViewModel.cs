using System.Collections.ObjectModel;
using LocalMeetingSubtitle.App.Infrastructure;
using LocalMeetingSubtitle.Core.Models;

namespace LocalMeetingSubtitle.App.ViewModels;

/// <summary>
/// One subtitle row in the speaker-management window: the text, its time and a combo box that lets
/// the user reassign the segment to a different speaker (or to "未知 / Unknown").
/// </summary>
public sealed class SegmentRowViewModel : ObservableObject
{
    private string? _originalSpeakerId;
    private SpeakerChoice? _selectedSpeaker;

    public SegmentRowViewModel(SubtitleSegment segment, string? speakerId, IEnumerable<SpeakerChoice> choices)
    {
        Segment = segment;
        _originalSpeakerId = speakerId;
        SpeakerChoices = new ObservableCollection<SpeakerChoice>(choices);
        _selectedSpeaker = SpeakerChoices.FirstOrDefault(c => c.SpeakerId == speakerId)
                           ?? SpeakerChoices.FirstOrDefault(c => c.SpeakerId is null);
    }

    public SubtitleSegment Segment { get; }

    /// <summary>The text shown for this segment (respects the user's edits).</summary>
    public string DisplayText => Segment.DisplayText;

    /// <summary>Start offset formatted as [HH:MM] (same display precision as the subtitle board).</summary>
    public string TimeText => $"[{Segment.StartOffset.Hours + Segment.StartOffset.Days * 24:00}:{Segment.StartOffset.Minutes:00}]";

    /// <summary>Pickable speakers: every non-merged speaker plus the "未知 / Unknown" option.</summary>
    public ObservableCollection<SpeakerChoice> SpeakerChoices { get; }

    /// <summary>Selected combo entry (kept in sync with <see cref="SelectedSpeakerId"/>).</summary>
    public SpeakerChoice? SelectedSpeaker
    {
        get => _selectedSpeaker;
        set
        {
            if (SetProperty(ref _selectedSpeaker, value))
            {
                OnPropertyChanged(nameof(SelectedSpeakerId));
            }
        }
    }

    /// <summary>The chosen speaker id; <c>null</c> means "未知 / Unknown".</summary>
    public string? SelectedSpeakerId => _selectedSpeaker?.SpeakerId;

    /// <summary>The speaker id as loaded (before any edit) — used to detect changes worth applying.</summary>
    public string? OriginalSpeakerId => _originalSpeakerId;

    /// <summary>True when the row's selection differs from the last loaded/applied value.</summary>
    public bool IsChanged => _selectedSpeaker?.SpeakerId != _originalSpeakerId;

    /// <summary>Records the current selection as the new baseline after a successful apply.</summary>
    public void AcceptChanges() => _originalSpeakerId = _selectedSpeaker?.SpeakerId;
}
