namespace Thrum.Core.Dsp;

/// <summary>
/// High-performance circular buffer for single-producer single-consumer float audio streams.
/// Provides zero-allocation reads, writes, and historical window peeking for pre-roll capture.
/// </summary>
public sealed class AudioRingBuffer
{
    private readonly float[] _buffer;
    private readonly int _capacity;
    private int _head; // Write index
    private int _tail; // Read index
    private readonly object _lock = new();

    public int Capacity => _capacity;

    public AudioRingBuffer(int capacity = 65536)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be positive.");

        _capacity = capacity;
        _buffer = new float[capacity];
        _head = 0;
        _tail = 0;
    }

    public int AvailableRead
    {
        get
        {
            lock (_lock)
            {
                int diff = _head - _tail;
                return diff >= 0 ? diff : _capacity + diff;
            }
        }
    }

    public int AvailableWrite => _capacity - 1 - AvailableRead;

    public void Clear()
    {
        lock (_lock)
        {
            _head = 0;
            _tail = 0;
            Array.Clear(_buffer, 0, _buffer.Length);
        }
    }

    /// <summary>
    /// Writes samples into the ring buffer. Overwrites oldest data if capacity is exceeded.
    /// </summary>
    public void Write(ReadOnlySpan<float> samples)
    {
        if (samples.IsEmpty) return;

        lock (_lock)
        {
            int toWrite = samples.Length;
            if (toWrite >= _capacity)
            {
                // If larger than entire buffer, write only the trailing capacity - 1 samples
                samples = samples.Slice(toWrite - (_capacity - 1));
                toWrite = samples.Length;
            }

            int available = AvailableWrite;
            if (toWrite > available)
            {
                // Advance tail to drop oldest samples
                int drop = toWrite - available;
                _tail = (_tail + drop) % _capacity;
            }

            for (int i = 0; i < toWrite; i++)
            {
                _buffer[_head] = samples[i];
                _head = (_head + 1) % _capacity;
            }
        }
    }

    /// <summary>
    /// Reads and consumes samples from the buffer.
    /// </summary>
    public int Read(Span<float> destination)
    {
        lock (_lock)
        {
            int available = AvailableRead;
            int count = Math.Min(available, destination.Length);
            for (int i = 0; i < count; i++)
            {
                destination[i] = _buffer[_tail];
                _tail = (_tail + 1) % _capacity;
            }
            return count;
        }
    }

    /// <summary>
    /// Copies the most recent <paramref name="destination"/>.Length samples without consuming them.
    /// Ideal for retrieving pre-roll audio around an onset.
    /// </summary>
    public int PeekLatest(Span<float> destination)
    {
        lock (_lock)
        {
            int available = AvailableRead;
            int count = Math.Min(available, destination.Length);
            if (count == 0) return 0;

            int start = (_head - count + _capacity) % _capacity;
            for (int i = 0; i < count; i++)
            {
                destination[i] = _buffer[(start + i) % _capacity];
            }
            return count;
        }
    }

    /// <summary>
    /// Copies a specific window of samples relative to the head (most recent samples).
    /// offsetFromHead: 0 = ending at latest sample, positive number = samples further back in the past.
    /// </summary>
    public int PeekWindow(int offsetFromHead, Span<float> destination)
    {
        lock (_lock)
        {
            int available = AvailableRead;
            if (offsetFromHead < 0 || offsetFromHead >= available)
                return 0;

            int count = Math.Min(destination.Length, available - offsetFromHead);
            int start = (_head - offsetFromHead - count + _capacity) % _capacity;
            for (int i = 0; i < count; i++)
            {
                destination[i] = _buffer[(start + i) % _capacity];
            }
            return count;
        }
    }
}
