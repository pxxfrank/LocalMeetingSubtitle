using LocalMeetingSubtitle.Core.Models;
using LocalMeetingSubtitle.Core.Speakers;

namespace LocalMeetingSubtitle.UnitTests;

public sealed class SpeakerAlignmentServiceTests
{
    private static readonly IReadOnlyDictionary<int, string> Map = new Dictionary<int, string>
    {
        [0] = "spk-A",
        [1] = "spk-B"
    };

    private readonly SpeakerAlignmentService _service = new();

    [Fact]
    public void Segment_fully_inside_one_interval_is_assigned_confidently()
    {
        var segment = Segment(1, 1000, 3000);
        var intervals = new[] { Interval(0, 500, 4000) };

        var outcome = Assert.Single(_service.Align(new[] { segment }, intervals, Map));

        Assert.Equal("spk-A", outcome.SpeakerId);
        Assert.False(outcome.NeedsConfirmation);
        Assert.Equal(1.0, outcome.OverlapFraction, 3);
    }

    [Fact]
    public void Clear_majority_owner_is_assigned_without_confirmation()
    {
        // A owns 60%, B owns 40% -> fraction 0.6 with a 0.2 margin.
        var segment = Segment(1, 0, 1000);
        var intervals = new[]
        {
            Interval(0, 0, 600),
            Interval(1, 600, 1000)
        };

        var outcome = Assert.Single(_service.Align(new[] { segment }, intervals, Map));

        Assert.Equal("spk-A", outcome.SpeakerId);
        Assert.False(outcome.NeedsConfirmation);
    }

    [Fact]
    public void Near_split_is_assigned_but_flagged_for_confirmation()
    {
        // A owns 52%, B 48% -> margin 0.04 < 0.15, so a human must confirm.
        var segment = Segment(1, 0, 1000);
        var intervals = new[]
        {
            Interval(0, 0, 520),
            Interval(1, 520, 1000)
        };

        var outcome = Assert.Single(_service.Align(new[] { segment }, intervals, Map));

        Assert.Equal("spk-A", outcome.SpeakerId);
        Assert.True(outcome.NeedsConfirmation);
    }

    [Fact]
    public void Exact_tie_is_unknown_and_needs_confirmation()
    {
        var segment = Segment(1, 0, 1000);
        var intervals = new[]
        {
            Interval(0, 0, 500),
            Interval(1, 500, 1000)
        };

        var outcome = Assert.Single(_service.Align(new[] { segment }, intervals, Map));

        Assert.Null(outcome.SpeakerId);
        Assert.True(outcome.NeedsConfirmation);
    }

    [Fact]
    public void Segment_without_overlap_is_unknown_not_forced()
    {
        var segment = Segment(1, 10_000, 12_000);
        var intervals = new[] { Interval(0, 0, 1000) };

        var outcome = Assert.Single(_service.Align(new[] { segment }, intervals, Map));

        Assert.Null(outcome.SpeakerId);
        Assert.False(outcome.NeedsConfirmation);
    }

    [Fact]
    public void Overlap_shorter_than_minimum_is_ignored()
    {
        // 100 ms overlap < the 200 ms default.
        var segment = Segment(1, 0, 1000);
        var intervals = new[] { Interval(0, 900, 1200) };

        var outcome = Assert.Single(_service.Align(new[] { segment }, intervals, Map));

        Assert.Null(outcome.SpeakerId);
    }

    [Fact]
    public void Empty_intervals_yield_unknown()
    {
        var segment = Segment(1, 0, 1000);

        var outcome = Assert.Single(_service.Align(new[] { segment }, Array.Empty<DiarizationInterval>(), Map));

        Assert.Null(outcome.SpeakerId);
        Assert.Equal(-1, outcome.RawSpeakerIndex);
    }

    [Fact]
    public void Confidence_is_carried_from_the_winning_interval()
    {
        var segment = Segment(1, 0, 1000);
        var intervals = new[] { Interval(0, 0, 1000, confidence: 0.83f) };

        var outcome = Assert.Single(_service.Align(new[] { segment }, intervals, Map));

        Assert.Equal(0.83f, outcome.Confidence);
    }

    [Fact]
    public void Unmapped_raw_index_yields_unknown_speaker_but_keeps_the_index()
    {
        var segment = Segment(1, 0, 1000);
        var intervals = new[] { Interval(7, 0, 1000) };

        var outcome = Assert.Single(_service.Align(new[] { segment }, intervals, Map));

        Assert.Null(outcome.SpeakerId);
        Assert.Equal(7, outcome.RawSpeakerIndex);
    }

    [Fact]
    public void Every_segment_produces_exactly_one_outcome()
    {
        var segments = new[] { Segment(1, 0, 1000), Segment(2, 1000, 2000), Segment(3, 5000, 6000) };
        var intervals = new[] { Interval(0, 0, 2500) };

        var outcomes = _service.Align(segments, intervals, Map);

        Assert.Equal(3, outcomes.Count);
        Assert.Equal(new long[] { 1, 2, 3 }, outcomes.Select(o => o.SegmentId).ToArray());
    }

    private static SubtitleSegment Segment(long id, double startMs, double endMs) => new()
    {
        SegmentId = id,
        SessionId = "s",
        SequenceNumber = (int)id,
        StartOffset = TimeSpan.FromMilliseconds(startMs),
        EndOffset = TimeSpan.FromMilliseconds(endMs),
        OriginalText = "text",
        CorrectedText = "text"
    };

    private static DiarizationInterval Interval(int rawIndex, double startMs, double endMs, float? confidence = null) =>
        new(TimeSpan.FromMilliseconds(startMs), TimeSpan.FromMilliseconds(endMs), rawIndex, confidence);
}
