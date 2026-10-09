using LocalMeetingSubtitle.App.Infrastructure;
using LocalMeetingSubtitle.Core.Models;
using System.Windows.Media;

namespace LocalMeetingSubtitle.App.ViewModels;

/// <summary>One rendered subtitle row (either a committed final line or the live partial).</summary>
public sealed class SubtitleLineViewModel : ObservableObject
{
    private string _text;
    private bool _isFinal;
    private bool _isMatch;
    private string? _speakerLabel;
    private bool _needsSpeakerConfirmation;
    private SolidColorBrush? _speakerBrush;
    private bool _isSpeakerVisible = true;

    public SubtitleLineViewModel(int sequenceNumber, TimeSpan startOffset, string text, bool isFinal)
    {
        SequenceNumber = sequenceNumber;
        StartOffset = startOffset;
        _text = text;
        _isFinal = isFinal;
    }

    public int SequenceNumber { get; private set; }

    public TimeSpan StartOffset { get; private set; }

    /// <summary>The associated persisted segment (final lines only).</summary>
    public SubtitleSegment? Segment { get; set; }

    public string Text
    {
        get => _text;
        set
        {
            if (SetProperty(ref _text, value))
            {
                OnPropertyChanged(nameof(Display));
            }
        }
    }

    public bool IsFinal
    {
        get => _isFinal;
        private set
        {
            if (SetProperty(ref _isFinal, value))
            {
                OnPropertyChanged(nameof(IsPartial));
            }
        }
    }

    public bool IsPartial => !_isFinal;

    /// <summary>True when this row matches the current search text (highlighted in the UI).</summary>
    public bool IsMatch
    {
        get => _isMatch;
        set => SetProperty(ref _isMatch, value);
    }

    /// <summary>The speaker's name for this row; null when unknown or not yet analyzed.</summary>
    public string? SpeakerLabel
    {
        get => _speakerLabel;
        private set
        {
            if (SetProperty(ref _speakerLabel, value))
            {
                OnPropertyChanged(nameof(HasSpeaker));
            }
        }
    }

    public bool HasSpeaker => !string.IsNullOrEmpty(_speakerLabel);

    /// <summary>True when the automatic assignment was ambiguous and a human should confirm it.</summary>
    public bool NeedsSpeakerConfirmation
    {
        get => _needsSpeakerConfirmation;
        private set => SetProperty(ref _needsSpeakerConfirmation, value);
    }

    /// <summary>Stable color for the speaker tag (frozen for cheap reuse).</summary>
    public SolidColorBrush? SpeakerBrush
    {
        get => _speakerBrush;
        private set => SetProperty(ref _speakerBrush, value);
    }

    /// <summary>
    /// True when this row passes the active by-speaker filter. Defaults to visible so the filter
    /// is a no-op until a specific speaker is selected.
    /// </summary>
    public bool IsSpeakerVisible
    {
        get => _isSpeakerVisible;
        set => SetProperty(ref _isSpeakerVisible, value);
    }

    /// <summary>Applies (or clears) the speaker tag for this row.</summary>
    public void SetSpeaker(string? speakerName, int colorArgb, bool needsConfirmation)
    {
        SpeakerLabel = speakerName;
        NeedsSpeakerConfirmation = needsConfirmation;

        if (string.IsNullOrEmpty(speakerName))
        {
            SpeakerBrush = null;
            return;
        }

        var color = System.Windows.Media.Color.FromArgb(
            (byte)((colorArgb >> 24) & 0xFF),
            (byte)((colorArgb >> 16) & 0xFF),
            (byte)((colorArgb >> 8) & 0xFF),
            (byte)(colorArgb & 0xFF));
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        SpeakerBrush = brush;
    }

    /// <summary>[HH:MM] — hours are not clamped to 24. Second-level precision is deliberately omitted.</summary>
    public string Timestamp => $"[{StartOffset.Hours + StartOffset.Days * 24:00}:{StartOffset.Minutes:00}]";

    public string Display => $"{Timestamp} {_text}";

    /// <summary>Promotes a live partial line to a committed final line in place.</summary>
    public void MakeFinal(int sequenceNumber, TimeSpan startOffset, string text)
    {
        SequenceNumber = sequenceNumber;
        StartOffset = startOffset;
        OnPropertyChanged(nameof(Timestamp));
        Text = text;
        IsFinal = true;
        OnPropertyChanged(nameof(Display));
    }
}
