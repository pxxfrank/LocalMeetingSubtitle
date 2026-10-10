using LocalMeetingSubtitle.Core.Audio;

namespace LocalMeetingSubtitle.UnitTests;

/// <summary>
/// V0.5 Phase 2: the segmenter must report where each speech region sits on the fed stream
/// (absolute sample indices) and carry a context overlap across a max-length cut, without
/// changing the behaviour of the original <see cref="AudioSegmenter.Push"/> API.
/// </summary>
public sealed class OfflineSegmenterTimingTests
{
    private static float[] Constant(float value, int count)
    {
        var data = new float[count];
        Array.Fill(data, value);
        return data;
    }

    [Fact]
    public void PushSegments_ReportsAbsolutePositions()
    {
        var segmenter = new AudioSegmenter(16000, silenceRms: 0.010, minSilenceSeconds: 0.6,
            maxSegmentSeconds: 15.0, minSpeechSeconds: 0.35);

        var segments = new List<SpeechSegment>();
        segments.AddRange(segmenter.PushSegments(Constant(0f, 16000)));      // 0.0-1.0 s silence
        segments.AddRange(segmenter.PushSegments(Constant(0.3f, 16000)));    // 1.0-2.0 s speech
        segments.AddRange(segmenter.PushSegments(Constant(0f, 16000)));      // 2.0-3.0 s silence

        var segment = Assert.Single(segments);

        // The region begins at the stream origin (the segmenter keeps the leading silence; the
        // transcription engine is what trims it) and ends at the speech, with the trailing silence cut.
        Assert.Equal(0, segment.StartSample);
        Assert.InRange(segment.EndSample, 32000 - 320, 32000);
        Assert.Equal(segment.Samples.Length, segment.Length);
        Assert.Equal(segment.StartSample + segment.Samples.Length, segment.EndSample);
        Assert.False(segment.HasOverlapPrefix);
    }

    [Fact]
    public void Positions_ContinueAcrossPushCalls()
    {
        var segmenter = new AudioSegmenter(16000, silenceRms: 0.010, minSilenceSeconds: 0.6,
            maxSegmentSeconds: 15.0, minSpeechSeconds: 0.35);

        var first = new List<SpeechSegment>();
        first.AddRange(segmenter.PushSegments(Constant(0.3f, 16000)));
        first.AddRange(segmenter.PushSegments(Constant(0f, 16000)));
        Assert.Single(first);

        var second = new List<SpeechSegment>();
        second.AddRange(segmenter.PushSegments(Constant(0f, 16000)));
        second.AddRange(segmenter.PushSegments(Constant(0.3f, 16000)));
        second.AddRange(segmenter.PushSegments(Constant(0f, 16000)));

        var next = Assert.Single(second);
        Assert.True(next.StartSample > first[0].EndSample,
            $"the second region must sit later on the stream ({next.StartSample} vs {first[0].EndSample}).");
    }

    [Fact]
    public void LengthCut_CarriesOverlapIntoTheNextSegment()
    {
        // 1.0 s cap with a 0.4 s overlap: the next region must restart 0.4 s before the previous one ended.
        var segmenter = new AudioSegmenter(16000, silenceRms: 0.010, minSilenceSeconds: 0.6,
            maxSegmentSeconds: 1.0, minSpeechSeconds: 0.35, overlapSeconds: 0.4);

        var segments = segmenter.PushSegments(Constant(0.3f, 16000 * 3));

        Assert.True(segments.Count >= 3, $"expected several capped segments, got {segments.Count}.");
        Assert.False(segments[0].HasOverlapPrefix);
        Assert.True(segments[1].HasOverlapPrefix, "the segment after a length cut must be flagged as carrying overlap.");
        Assert.Equal(segments[0].EndSample - 6400, segments[1].StartSample);
        Assert.True(segments[1].StartSample < segments[0].EndSample, "the overlap region must be re-fed.");

        for (int i = 1; i < segments.Count; i++)
        {
            Assert.True(segments[i].StartSample > segments[i - 1].StartSample,
                $"positions must advance monotonically (index {i}).");
        }
    }

    [Fact]
    public void SilenceCut_DoesNotCarryOverlap()
    {
        var segmenter = new AudioSegmenter(16000, silenceRms: 0.010, minSilenceSeconds: 0.6,
            maxSegmentSeconds: 15.0, minSpeechSeconds: 0.35, overlapSeconds: 0.4);

        var segments = new List<SpeechSegment>();
        segments.AddRange(segmenter.PushSegments(Constant(0.3f, 16000)));
        segments.AddRange(segmenter.PushSegments(Constant(0f, 16000)));
        segments.AddRange(segmenter.PushSegments(Constant(0.3f, 16000)));
        segments.AddRange(segmenter.PushSegments(Constant(0f, 16000)));

        Assert.Equal(2, segments.Count);
        Assert.All(segments, s => Assert.False(s.HasOverlapPrefix));
    }

    [Fact]
    public void LegacyPush_IsUnchangedWhenOverlapIsDisabled()
    {
        var legacy = new AudioSegmenter(16000, 0.010, 0.6, 1.0, 0.35);
        var timed = new AudioSegmenter(16000, 0.010, 0.6, 1.0, 0.35, 0.0);

        var legacySegments = legacy.Push(Constant(0.3f, 16000 * 3));
        var timedSegments = timed.PushSegments(Constant(0.3f, 16000 * 3));

        Assert.Equal(timedSegments.Count, legacySegments.Count);
        for (int i = 0; i < legacySegments.Count; i++)
        {
            Assert.Equal(timedSegments[i].Samples, legacySegments[i]);
        }
    }

    [Fact]
    public void FlushSegment_KeepsTheAbsolutePosition()
    {
        var segmenter = new AudioSegmenter(16000);
        segmenter.PushSegments(Constant(0.3f, 8000));

        var tail = segmenter.FlushSegment();

        Assert.NotNull(tail);
        Assert.Equal(0, tail!.Value.StartSample);
        Assert.InRange(tail.Value.Length, 7000, 8100);
    }

    [Fact]
    public void Reset_RestartsTheTimeline()
    {
        var segmenter = new AudioSegmenter(16000);
        segmenter.PushSegments(Constant(0.3f, 8000));
        segmenter.FlushSegment();

        segmenter.Reset();
        var after = segmenter.PushSegments(Constant(0f, 16000));
        after = segmenter.PushSegments(Constant(0.3f, 16000));
        after = segmenter.PushSegments(Constant(0f, 16000));

        var segment = Assert.Single(after);
        Assert.True(segment.StartSample < 16000, $"the timeline must restart at zero after Reset, got {segment.StartSample}.");
    }
}
