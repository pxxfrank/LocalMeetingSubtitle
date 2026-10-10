using LocalMeetingSubtitle.Core.Abstractions;
using LocalMeetingSubtitle.Core.Models;

namespace LocalMeetingSubtitle.Core.Speakers;

/// <summary>
/// Merges consecutive same-speaker transcript segments into dialogue turns. Pure: it takes the
/// already-decided per-segment speaker assignments and never touches diarization models or the
/// database.
/// </summary>
public sealed class TranscriptAlignmentService : ITranscriptAlignmentService
{
    public DialogueTranscript Align(
        string sessionId,
        IReadOnlyList<TranscriptSegmentFact> segments,
        IReadOnlyDictionary<long, SpeakerAssignment> assignmentsBySegmentId,
        IReadOnlyDictionary<string, Speaker> speakersById,
        DialogueAssemblyOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentNullException.ThrowIfNull(assignmentsBySegmentId);
        ArgumentNullException.ThrowIfNull(speakersById);

        var opt = options ?? new DialogueAssemblyOptions();
        if (segments.Count == 0)
        {
            return new DialogueTranscript { SessionId = sessionId };
        }

        var turns = new List<DialogueTurn>();
        var builder = new TurnBuilder();

        foreach (var segment in segments)
        {
            string? speakerId = assignmentsBySegmentId.TryGetValue(segment.SegmentId, out var assignment)
                ? assignment.SpeakerId
                : null;
            bool needsConfirmation = assignment?.NeedsConfirmation ?? false;

            if (builder.HasTurn && !builder.CanAppend(speakerId, segment, opt))
            {
                turns.Add(builder.Build(speakersById, opt));
                builder = new TurnBuilder();
            }

            builder.Append(speakerId, segment, needsConfirmation);
        }

        turns.Add(builder.Build(speakersById, opt));
        return new DialogueTranscript
        {
            SessionId = sessionId,
            Turns = turns,
            Participants = BuildParticipants(turns, speakersById)
        };
    }

    private static IReadOnlyList<DialogueParticipant> BuildParticipants(
        IReadOnlyList<DialogueTurn> turns,
        IReadOnlyDictionary<string, Speaker> speakersById)
    {
        var byId = new Dictionary<string, (TimeSpan Speaking, int Turns, int Segments)>(StringComparer.Ordinal);

        foreach (var turn in turns)
        {
            if (turn.SpeakerId is null)
            {
                continue;
            }

            byId.TryGetValue(turn.SpeakerId, out var acc);
            byId[turn.SpeakerId] = (
                acc.Speaking + turn.Duration,
                acc.Turns + 1,
                acc.Segments + turn.SegmentIds.Count);
        }

        return byId
            .Select(pair => new DialogueParticipant(
                pair.Key,
                NameOf(speakersById, pair.Key, ""),
                speakersById.TryGetValue(pair.Key, out var speaker) ? speaker.ColorArgb : 0,
                pair.Value.Speaking,
                pair.Value.Turns,
                pair.Value.Segments))
            .OrderByDescending(p => p.SpeakingTime)
            .ThenBy(p => p.Name, StringComparer.Ordinal)
            .ToList();
    }

    private static string NameOf(IReadOnlyDictionary<string, Speaker> speakersById, string speakerId, string fallback)
    {
        if (speakersById.TryGetValue(speakerId, out var speaker))
        {
            string name = speaker.EffectiveName;
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }
        }

        return string.IsNullOrEmpty(fallback) ? speakerId : fallback;
    }

    /// <summary>Accumulates the segments of one turn.</summary>
    private struct TurnBuilder
    {
        private string? _speakerId;
        private TimeSpan _start;
        private TimeSpan _end;
        private int _chars;
        private bool _needsConfirmation;
        private List<long>? _segmentIds;
        private System.Text.StringBuilder? _text;

        public bool HasTurn { get; private set; }

        public bool CanAppend(string? speakerId, TranscriptSegmentFact segment, DialogueAssemblyOptions options)
        {
            if (!HasTurn)
            {
                return true;
            }

            if (!string.Equals(speakerId, _speakerId, StringComparison.Ordinal))
            {
                return false;
            }

            if (segment.Start - _end > options.MaxGap)
            {
                return false;
            }

            return _chars + segment.Text.Length <= options.MaxTurnChars;
        }

        public void Append(string? speakerId, TranscriptSegmentFact segment, bool needsConfirmation)
        {
            if (!HasTurn)
            {
                _speakerId = speakerId;
                _start = segment.Start;
                _chars = 0;
                _text = new System.Text.StringBuilder();
                _segmentIds = new List<long>();
                HasTurn = true;
            }

            _end = segment.End;
            _chars += segment.Text.Length;
            _needsConfirmation |= needsConfirmation;
            _text!.Append(segment.Text);
            _segmentIds!.Add(segment.SegmentId);
        }

        public DialogueTurn Build(IReadOnlyDictionary<string, Speaker> speakersById, DialogueAssemblyOptions options)
        {
            bool unknown = _speakerId is null;
            return new DialogueTurn
            {
                SpeakerId = _speakerId,
                SpeakerName = unknown ? options.UnknownSpeakerLabel : NameOf(speakersById, _speakerId!, options.UnknownSpeakerLabel),
                SpeakerColorArgb = unknown
                    ? options.UnknownSpeakerColorArgb
                    : speakersById.TryGetValue(_speakerId!, out var speaker) ? speaker.ColorArgb : options.UnknownSpeakerColorArgb,
                Start = _start,
                End = _end,
                Text = _text?.ToString() ?? "",
                SegmentIds = _segmentIds ?? new List<long>(),
                NeedsConfirmation = _needsConfirmation
            };
        }
    }
}
