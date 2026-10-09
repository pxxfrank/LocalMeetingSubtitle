using System.Collections.ObjectModel;
using System.Windows.Media;
using LocalMeetingSubtitle.App.Infrastructure;
using LocalMeetingSubtitle.Core.Models;

namespace LocalMeetingSubtitle.App.ViewModels;

/// <summary>
/// One row in the speaker-management window: an editable name, a color swatch, usage statistics and
/// a target for the "merge into…" action.
/// </summary>
public sealed class SpeakerRowViewModel : ObservableObject
{
    private readonly Speaker _speaker;
    private string _displayName;
    private bool _isDirty;
    private SpeakerChoice? _mergeTarget;

    public SpeakerRowViewModel(
        Speaker speaker,
        int segmentCount,
        double speechSeconds,
        IEnumerable<SpeakerChoice> mergeTargets)
    {
        _speaker = speaker;
        _displayName = speaker.DisplayName;
        SegmentCount = segmentCount;
        SpeechSeconds = speechSeconds;
        MergeTargets = new ObservableCollection<SpeakerChoice>(mergeTargets);
        ColorBrush = CreateBrush(speaker.ColorArgb);
    }

    /// <summary>The underlying speaker entity (its <see cref="Speaker.DisplayName"/> is only written on save).</summary>
    public Speaker Speaker => _speaker;

    public string SpeakerId => _speaker.SpeakerId;

    /// <summary>The stable anonymous label ("A", "B", …).</summary>
    public string Label => _speaker.Label;

    /// <summary>Editable name; empty keeps the speaker anonymous.</summary>
    public string DisplayName
    {
        get => _displayName;
        set
        {
            if (SetProperty(ref _displayName, value))
            {
                IsDirty = true;
            }
        }
    }

    /// <summary>True when <see cref="DisplayName"/> has been edited since the last load/save.</summary>
    public bool IsDirty
    {
        get => _isDirty;
        private set => SetProperty(ref _isDirty, value);
    }

    /// <summary>Frozen swatch brush from <see cref="Speaker.ColorArgb"/>.</summary>
    public SolidColorBrush ColorBrush { get; }

    /// <summary>Number of subtitle segments currently assigned to this speaker.</summary>
    public int SegmentCount { get; }

    /// <summary>
    /// Estimated speaking time in seconds, summed from the durations of the segments assigned to
    /// this speaker. The raw diarization interval → speaker mapping is not persisted, so this is an
    /// estimate rather than a precise total (the UI labels it as such).
    /// </summary>
    public double SpeechSeconds { get; }

    /// <summary>Human-readable estimate, e.g. "≈ 01:23".</summary>
    public string SpeechTimeText => $"≈ {FormatDuration(SpeechSeconds)}";

    /// <summary>Other (non-merged) speakers this one can be merged into.</summary>
    public ObservableCollection<SpeakerChoice> MergeTargets { get; }

    /// <summary>The chosen merge destination, or <c>null</c> when none is selected yet.</summary>
    public SpeakerChoice? MergeTarget
    {
        get => _mergeTarget;
        set => SetProperty(ref _mergeTarget, value);
    }

    /// <summary>Marks the current name as persisted.</summary>
    public void AcceptChanges() => IsDirty = false;

    private static string FormatDuration(double seconds)
    {
        if (seconds <= 0)
        {
            return "00:00";
        }

        var total = TimeSpan.FromSeconds(seconds);
        return $"{(int)total.TotalMinutes:00}:{total.Seconds:00}";
    }

    private static SolidColorBrush CreateBrush(int colorArgb)
    {
        var color = System.Windows.Media.Color.FromArgb(
            (byte)((colorArgb >> 24) & 0xFF),
            (byte)((colorArgb >> 16) & 0xFF),
            (byte)((colorArgb >> 8) & 0xFF),
            (byte)(colorArgb & 0xFF));
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
