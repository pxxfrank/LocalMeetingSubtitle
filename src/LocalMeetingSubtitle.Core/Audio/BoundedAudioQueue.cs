namespace LocalMeetingSubtitle.Core.Audio;

/// <summary>
/// Bounded FIFO between the capture thread and the ASR thread. Never grows without bound:
/// when full, the oldest audio is dropped and counted so the UI can report the condition
/// instead of silently pretending transcription is healthy.
/// </summary>
public sealed class BoundedAudioQueue
{
    private readonly object _gate = new();
    private readonly Queue<float[]> _queue = new();
    private readonly long _capacitySamples;
    private long _currentSamples;
    private long _maxSamples;
    private long _droppedSamples;
    private long _enqueuedChunks;
    private long _dequeuedChunks;

    public BoundedAudioQueue(long capacitySamples)
    {
        if (capacitySamples <= 0) throw new ArgumentOutOfRangeException(nameof(capacitySamples));
        _capacitySamples = capacitySamples;
    }

    public long CapacitySamples => _capacitySamples;

    public long CurrentSamples { get { lock (_gate) return _currentSamples; } }
    public long MaxSamples { get { lock (_gate) return _maxSamples; } }
    public long DroppedSamples { get { lock (_gate) return _droppedSamples; } }
    public long EnqueuedChunks { get { lock (_gate) return _enqueuedChunks; } }
    public long DequeuedChunks { get { lock (_gate) return _dequeuedChunks; } }

    public int Count { get { lock (_gate) return _queue.Count; } }

    /// <summary>Enqueues a chunk. Returns false if the chunk did not fully fit and was dropped.</summary>
    public bool Enqueue(float[] chunk)
    {
        if (chunk.Length == 0) return true;
        lock (_gate)
        {
            if (_currentSamples + chunk.Length > _capacitySamples)
            {
                // Overloaded: drop the incoming chunk (keeps the oldest in-flight audio intact
                // so an in-progress utterance is not truncated mid-word).
                _droppedSamples += chunk.Length;
                return false;
            }
            _queue.Enqueue(chunk);
            _currentSamples += chunk.Length;
            _enqueuedChunks++;
            if (_currentSamples > _maxSamples) _maxSamples = _currentSamples;
            return true;
        }
    }

    public bool TryDequeue(out float[]? chunk)
    {
        lock (_gate)
        {
            if (_queue.Count == 0)
            {
                chunk = null;
                return false;
            }
            chunk = _queue.Dequeue();
            _currentSamples -= chunk.Length;
            _dequeuedChunks++;
            return true;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _queue.Clear();
            _currentSamples = 0;
        }
    }

    public double DroppedSeconds(int sampleRate) => sampleRate <= 0 ? 0 : (double)DroppedSamples / sampleRate;
}
