using LocalMeetingSubtitle.App.Infrastructure;
using LocalMeetingSubtitle.Core.Models;

namespace LocalMeetingSubtitle.App.ViewModels;

/// <summary>One rendered subtitle row (either a committed final line or the live partial).</summary>
public sealed class SubtitleLineViewModel : ObservableObject
{
    private string _text;
    private bool _isFinal;
    private bool _isMatch;

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

    /// <summary>[HH:MM:SS] — hours are not clamped to 24.</summary>
    public string Timestamp => $"[{StartOffset.Hours + StartOffset.Days * 24:00}:{StartOffset.Minutes:00}:{StartOffset.Seconds:00}]";

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
