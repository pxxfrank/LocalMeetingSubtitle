using LocalMeetingSubtitle.Core.Abstractions;
using LocalMeetingSubtitle.Core.Models;

namespace LocalMeetingSubtitle.Core.Speakers;

/// <summary>
/// Assigns at most one speaker to each subtitle segment by comparing the segment's audio interval
/// with the diarization intervals on the shared timeline.
///
/// Deliberately conservative: without word-level timestamps a single segment cannot be split
/// between speakers, so an ambiguous segment is assigned its best candidate and flagged
/// (<see cref="AlignmentOutcome.NeedsConfirmation"/>) rather than guessed confidently.
/// A segment with no usable overlap becomes "unknown speaker" (a null speaker id) — it is never
/// force-assigned. This type is pure: it never persists anything and never edits segment text.
/// </summary>
public sealed class SpeakerAlignmentService : ISpeakerAlignmentService
{
    private static readonly AlignmentOptions Defaults = new();

    public IReadOnlyList<AlignmentOutcome> Align(
        IReadOnlyList<SubtitleSegment> segments,
        IReadOnlyList<DiarizationInterval> intervals,
        IReadOnlyDictionary<int, string> speakerIdByRawIndex,
        AlignmentOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentNullException.ThrowIfNull(intervals);
        ArgumentNullException.ThrowIfNull(speakerIdByRawIndex);

        var opt = options ?? Defaults;
        var results = new List<AlignmentOutcome>(segments.Count);

        foreach (var segment in segments)
        {
            results.Add(AlignOne(segment, intervals, speakerIdByRawIndex, opt));
        }

        return results;
    }

    private static AlignmentOutcome AlignOne(
        SubtitleSegment segment,
        IReadOnlyList<DiarizationInterval> intervals,
        IReadOnlyDictionary<int, string> speakerIdByRawIndex,
        AlignmentOptions opt)
    {
        var start = segment.StartOffset;
        var end = segment.EndOffset;
        var duration = (end - start).TotalMilliseconds;

        if (duration <= 0 || intervals.Count == 0)
        {
            return Unknown(segment.SegmentId, 0d);
        }

        // Accumulate overlap per raw speaker index.
        var overlaps = new Dictionary<int, double>();
        var confidence = new Dictionary<int, float>();
        foreach (var interval in intervals)
        {
            var overlap = OverlapMs(start, end, interval.Start, interval.End);
            if (overlap < opt.MinOverlapMs)
            {
                continue;
            }

            overlaps.TryGetValue(interval.RawSpeakerIndex, out var current);
            overlaps[interval.RawSpeakerIndex] = current + overlap;

            if (interval.Confidence is { } c)
            {
                confidence.TryGetValue(interval.RawSpeakerIndex, out var cc);
                confidence[interval.RawSpeakerIndex] = Math.Max(cc, c);
            }
        }

        if (overlaps.Count == 0)
        {
            return Unknown(segment.SegmentId, 0d);
        }

        // Two largest overlaps.
        var ranked = overlaps.OrderByDescending(kv => kv.Value).ToList();
        var top1 = ranked[0];
        var top2Value = ranked.Count > 1 ? ranked[1].Value : 0d;

        var frac1 = top1.Value / duration;
        var margin = (top1.Value - top2Value) / duration;

        // An exact tie (e.g. two speakers share the segment evenly) is genuinely undecidable *and*
        // has no single best candidate, so it becomes unknown + needs confirmation.
        if (ranked.Count > 1 && Math.Abs(top1.Value - top2Value) < double.Epsilon)
        {
            return new AlignmentOutcome(segment.SegmentId, null, -1, null, frac1, true);
        }

        var speakerId = speakerIdByRawIndex.TryGetValue(top1.Key, out var sid) ? sid : null;
        confidence.TryGetValue(top1.Key, out var topConfidence);

        if (frac1 >= opt.MinOverlapFraction && margin > opt.ConfirmMargin)
        {
            return new AlignmentOutcome(segment.SegmentId, speakerId, top1.Key, topConfidence, frac1, false);
        }

        // Best-effort assignment that a human should confirm.
        return new AlignmentOutcome(segment.SegmentId, speakerId, top1.Key, topConfidence, frac1, true);
    }

    private static AlignmentOutcome Unknown(long segmentId, double overlapFraction) =>
        new(segmentId, null, -1, null, overlapFraction, false);

    private static double OverlapMs(TimeSpan aStart, TimeSpan aEnd, TimeSpan bStart, TimeSpan bEnd)
    {
        var start = aStart > bStart ? aStart : bStart;
        var end = aEnd < bEnd ? aEnd : bEnd;
        return end > start ? (end - start).TotalMilliseconds : 0d;
    }
}
