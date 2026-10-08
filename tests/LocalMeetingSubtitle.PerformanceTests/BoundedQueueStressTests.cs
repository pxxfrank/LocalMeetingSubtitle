using System.Diagnostics;
using LocalMeetingSubtitle.Core.Audio;
using Xunit.Abstractions;

namespace LocalMeetingSubtitle.PerformanceTests;

[Trait("Category", "Performance")]
public sealed class BoundedQueueStressTests
{
    private readonly ITestOutputHelper _output;

    public BoundedQueueStressTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void SustainedEnqueueDequeue_StaysBoundedAndCountersConsistent()
    {
        const long capacity = 1024;
        const int chunk = 64;
        const int iterations = 400_000;

        var queue = new BoundedAudioQueue(capacity);
        var random = new Random(1234);

        long accepted = 0;
        long dequeued = 0;
        long dropped = 0;
        long enqueueAttempts = 0;
        long maxObserved = 0;

        var stopwatch = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            if (random.Next(2) == 0)
            {
                enqueueAttempts++;
                if (queue.Enqueue(new float[chunk]))
                {
                    accepted += chunk;
                }
                else
                {
                    dropped += chunk;
                }
            }
            else if (queue.TryDequeue(out var dequeuedChunk))
            {
                dequeued += dequeuedChunk!.Length;
            }

            long current = queue.CurrentSamples;
            if (current > maxObserved)
            {
                maxObserved = current;
            }
        }
        stopwatch.Stop();

        _output.WriteLine($"iterations={iterations} elapsed_ms={stopwatch.Elapsed.TotalMilliseconds:F0}");
        _output.WriteLine($"accepted={accepted} dequeued={dequeued} dropped={dropped} current={queue.CurrentSamples}");
        _output.WriteLine($"chunks: enqueued={queue.EnqueuedChunks} dequeued={queue.DequeuedChunks} count={queue.Count}");
        _output.WriteLine($"max_samples={queue.MaxSamples} max_observed={maxObserved} capacity={capacity}");

        // Bounded memory: never grows past capacity.
        Assert.True(maxObserved <= capacity, $"observed {maxObserved} > capacity {capacity}.");
        Assert.True(queue.MaxSamples <= capacity);

        // Counters are mutually consistent.
        Assert.Equal(queue.EnqueuedChunks * chunk, accepted);
        Assert.Equal(queue.DroppedSamples, dropped);
        Assert.Equal(queue.DequeuedChunks * chunk, dequeued);
        Assert.Equal(accepted - dequeued, queue.CurrentSamples);
        Assert.Equal(queue.EnqueuedChunks - queue.DequeuedChunks, queue.Count);
        Assert.Equal(enqueueAttempts * chunk, accepted + dropped);
    }
}
