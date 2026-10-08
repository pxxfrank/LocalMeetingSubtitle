using LocalMeetingSubtitle.Core.Audio;

namespace LocalMeetingSubtitle.UnitTests;

public sealed class AudioSegmenterTests
{
    private static float[] Constant(float value, int count)
    {
        var data = new float[count];
        Array.Fill(data, value);
        return data;
    }

    [Fact]
    public void SilenceThenSpeechThenSilence_YieldsOneSegment()
    {
        var segmenter = new AudioSegmenter(16000, silenceRms: 0.010, minSilenceSeconds: 0.6,
            maxSegmentSeconds: 15.0, minSpeechSeconds: 0.35);

        var completed = new List<float[]>();
        completed.AddRange(segmenter.Push(Constant(0f, 16000)));      // leading silence
        completed.AddRange(segmenter.Push(Constant(0.3f, 16000)));    // speech
        completed.AddRange(segmenter.Push(Constant(0f, 16000)));      // trailing silence

        var segment = Assert.Single(completed);
        Assert.True(segment.Length > 0);
    }

    [Fact]
    public void LongSpeech_IsCappedAtMaxSegmentSeconds()
    {
        var segmenter = new AudioSegmenter(16000, silenceRms: 0.010, minSilenceSeconds: 0.6,
            maxSegmentSeconds: 1.0, minSpeechSeconds: 0.35);

        var segments = segmenter.Push(Constant(0.3f, 16000 * 3));

        Assert.True(segments.Count >= 2, $"expected multiple segments, got {segments.Count}.");
        Assert.All(segments, segment => Assert.True(segment.Length <= 16000,
            $"segment length {segment.Length} exceeds max (16000)."));
    }

    [Fact]
    public void Flush_ReturnsTrailingSpeech()
    {
        var segmenter = new AudioSegmenter(16000);
        segmenter.Push(Constant(0.3f, 8000)); // 0.5 s of speech, never closed by silence

        var tail = segmenter.Flush();

        Assert.NotNull(tail);
        Assert.InRange(tail!.Length, 7000, 8100);
    }

    [Fact]
    public void Flush_BelowMinimumSpeech_ReturnsNull()
    {
        var segmenter = new AudioSegmenter(16000);
        segmenter.Push(Constant(0.3f, 1600)); // 0.1 s < 0.35 s minimum

        Assert.Null(segmenter.Flush());
    }

    [Fact]
    public void Flush_OnEmptySegmenter_ReturnsNull()
    {
        var segmenter = new AudioSegmenter(16000);

        Assert.Null(segmenter.Flush());
    }

    [Fact]
    public void Reset_DiscardsBufferedSpeech()
    {
        var segmenter = new AudioSegmenter(16000);
        segmenter.Push(Constant(0.3f, 8000));

        segmenter.Reset();

        Assert.Null(segmenter.Flush());
    }
}
