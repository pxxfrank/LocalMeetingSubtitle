using LocalMeetingSubtitle.Core.Audio;

namespace LocalMeetingSubtitle.UnitTests;

public sealed class BoundedAudioQueueTests
{
    [Fact]
    public void Enqueue_GrowsOnlyUpToCapacity_ThenDrops()
    {
        var queue = new BoundedAudioQueue(capacitySamples: 10);

        Assert.True(queue.Enqueue(new float[6]));
        Assert.Equal(6, queue.CurrentSamples);
        Assert.Equal(6, queue.MaxSamples);

        // 6 + 6 > 10: the incoming chunk is dropped, not the queue grown.
        Assert.False(queue.Enqueue(new float[6]));
        Assert.Equal(6, queue.CurrentSamples);
        Assert.Equal(6, queue.DroppedSamples);
        Assert.Equal(1, queue.Count);

        Assert.True(queue.Enqueue(new float[4]));
        Assert.Equal(10, queue.CurrentSamples);
        Assert.Equal(10, queue.MaxSamples);
        Assert.Equal(2, queue.Count);
    }

    [Fact]
    public void EmptyChunks_AreIgnoredButReportedSuccessful()
    {
        var queue = new BoundedAudioQueue(capacitySamples: 10);

        Assert.True(queue.Enqueue(Array.Empty<float>()));

        Assert.Equal(0, queue.CurrentSamples);
        Assert.Equal(0, queue.EnqueuedChunks);
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public void Counters_TrackEnqueueAndDequeue()
    {
        var queue = new BoundedAudioQueue(capacitySamples: 100);

        queue.Enqueue(new float[10]);
        queue.Enqueue(new float[20]);
        queue.Enqueue(new float[30]);
        queue.Enqueue(new float[100]); // dropped

        Assert.Equal(3, queue.EnqueuedChunks);
        Assert.Equal(100, queue.DroppedSamples);
        Assert.Equal(60, queue.CurrentSamples);
        Assert.Equal(60, queue.MaxSamples);

        Assert.True(queue.TryDequeue(out var first));
        Assert.Equal(10, first!.Length);
        Assert.Equal(1, queue.DequeuedChunks);
        Assert.Equal(50, queue.CurrentSamples);
        Assert.Equal(60, queue.MaxSamples); // high-water mark is retained

        Assert.True(queue.TryDequeue(out var second));
        Assert.Equal(20, second!.Length);
        Assert.True(queue.TryDequeue(out var third));
        Assert.Equal(30, third!.Length);

        Assert.False(queue.TryDequeue(out var empty));
        Assert.Null(empty);
        Assert.Equal(3, queue.DequeuedChunks);
        Assert.Equal(0, queue.CurrentSamples);
    }

    [Fact]
    public void TryDequeue_IsFifo()
    {
        var queue = new BoundedAudioQueue(capacitySamples: 100);
        var a = new float[] { 1 };
        var b = new float[] { 2 };
        queue.Enqueue(a);
        queue.Enqueue(b);

        queue.TryDequeue(out var first);
        queue.TryDequeue(out var second);

        Assert.Same(a, first);
        Assert.Same(b, second);
    }

    [Fact]
    public void Clear_EmptiesQueueButKeepsLifetimeCounters()
    {
        var queue = new BoundedAudioQueue(capacitySamples: 100);
        queue.Enqueue(new float[40]);
        queue.Enqueue(new float[50]);

        queue.Clear();

        Assert.Equal(0, queue.CurrentSamples);
        Assert.Equal(0, queue.Count);
        Assert.Equal(2, queue.EnqueuedChunks);
        Assert.Equal(90, queue.MaxSamples);
    }

    [Fact]
    public void DroppedSeconds_ConvertsUsingSampleRate()
    {
        var queue = new BoundedAudioQueue(capacitySamples: 100);
        queue.Enqueue(new float[100]);
        queue.Enqueue(new float[16000]); // fully dropped

        Assert.Equal(1.0, queue.DroppedSeconds(16000), 5);
        Assert.Equal(0.0, queue.DroppedSeconds(0), 5);
    }

    [Fact]
    public void NonPositiveCapacity_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BoundedAudioQueue(0));
    }
}
