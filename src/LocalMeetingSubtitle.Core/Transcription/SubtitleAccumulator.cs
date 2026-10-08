using LocalMeetingSubtitle.Core.Models;

namespace LocalMeetingSubtitle.Core.Transcription;

/// <summary>
/// Turns a stream of ASR hypotheses into partial/final transcript events.
///
/// Rules:
/// <list type="bullet">
/// <item>Non-endpoint hypotheses update the current <i>partial</i> only — they are never persisted.</item>
/// <item>An endpoint commits exactly one <i>final</i> segment (the endpoint text, or the last partial if empty).</item>
/// <item>Consecutive identical finals (after whitespace/punctuation folding) are suppressed to avoid repeats.</item>
/// <item><see cref="Flush"/> commits a still-open partial (pause/stop) so the last sentence is never lost.</item>
/// </list>
/// Sequence numbers are 1-based and only advance on committed finals.
/// </summary>
public sealed class SubtitleAccumulator
{
    private readonly Func<string, string> _correct;
    private readonly int _dedupWindow;
    private readonly Queue<string> _recentFinals = new();

    private string _partial = "";
    private TimeSpan _segmentStart;
    private bool _hasSegmentStart;
    private int _sequence;
    private TimeSpan _lastFinalEnd = TimeSpan.MinValue;

    public SubtitleAccumulator(Func<string, string>? correct = null, int dedupWindow = 6)
    {
        _correct = correct ?? (s => s);
        _dedupWindow = Math.Max(1, dedupWindow);
    }

    public int Sequence => _sequence;
    public string CurrentPartial => _partial;

    public TranscriptEvent Update(string? text, bool isEndpoint, TimeSpan audioTime)
    {
        var normalized = Normalize(text ?? "");

        if (!isEndpoint)
        {
            if (normalized.Length == 0)
            {
                // Some engines briefly emit empty hypotheses; keep the previous partial visible.
                return TranscriptEvent.None;
            }

            if (!_hasSegmentStart)
            {
                _segmentStart = audioTime;
                _hasSegmentStart = true;
            }
            _partial = normalized;
            return new TranscriptEvent(TranscriptEventKind.Partial, normalized, _correct(normalized), _sequence, _segmentStart, audioTime);
        }

        var finalText = normalized.Length > 0 ? normalized : _partial;
        var start = _hasSegmentStart ? _segmentStart : audioTime;
        _partial = "";
        _hasSegmentStart = false;

        if (finalText.Length == 0)
        {
            return TranscriptEvent.None;
        }

        if (IsRecentDuplicate(finalText, audioTime))
        {
            return TranscriptEvent.None;
        }

        _sequence++;
        Remember(finalText);
        _lastFinalEnd = audioTime;
        return new TranscriptEvent(TranscriptEventKind.Final, finalText, _correct(finalText), _sequence, start, audioTime);
    }

    /// <summary>Commits any open partial. Call on pause/stop so the final sentence is preserved.</summary>
    public TranscriptEvent Flush(TimeSpan audioTime)
    {
        if (_partial.Length == 0)
        {
            return TranscriptEvent.None;
        }

        var text = _partial;
        var start = _hasSegmentStart ? _segmentStart : audioTime;
        _partial = "";
        _hasSegmentStart = false;

        if (IsRecentDuplicate(text, audioTime))
        {
            return TranscriptEvent.None;
        }

        _sequence++;
        Remember(text);
        _lastFinalEnd = audioTime;
        return new TranscriptEvent(TranscriptEventKind.Final, text, _correct(text), _sequence, start, audioTime);
    }

    public void Reset()
    {
        _partial = "";
        _hasSegmentStart = false;
        _sequence = 0;
        _lastFinalEnd = TimeSpan.MinValue;
        _recentFinals.Clear();
    }

    private bool IsRecentDuplicate(string text, TimeSpan audioTime)
    {
        // Only suppress a repeat that arrives immediately after the previous final; a genuine
        // repeat later in the meeting is still allowed through.
        if (_lastFinalEnd != TimeSpan.MinValue && (audioTime - _lastFinalEnd) > TimeSpan.FromSeconds(2))
        {
            return false;
        }
        var compact = Fold(text);
        return _recentFinals.Contains(compact);
    }

    private void Remember(string text)
    {
        _recentFinals.Enqueue(Fold(text));
        while (_recentFinals.Count > _dedupWindow)
        {
            _recentFinals.Dequeue();
        }
    }

    internal static string Normalize(string text)
    {
        text = text.Trim();
        if (text.Length == 0) return text;
        // Collapse runs of whitespace to a single space.
        var sb = new System.Text.StringBuilder(text.Length);
        bool prevSpace = false;
        foreach (var ch in text)
        {
            bool isSpace = char.IsWhiteSpace(ch);
            if (isSpace)
            {
                if (!prevSpace) sb.Append(' ');
                prevSpace = true;
            }
            else
            {
                sb.Append(ch);
                prevSpace = false;
            }
        }
        return sb.ToString();
    }

    internal static string Fold(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch) || char.IsPunctuation(ch) || char.IsSymbol(ch)) continue;
            sb.Append(char.ToLowerInvariant(ch));
        }
        return sb.ToString();
    }
}
