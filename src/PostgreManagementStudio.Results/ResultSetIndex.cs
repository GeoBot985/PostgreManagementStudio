namespace PostgreManagementStudio.Results;

/// <summary>
/// Append-only batch index with atomic snapshot swap and binary-search lookup.
/// </summary>
/// <remarks>
/// <para>
/// Concurrency model: a single writer appends batches; readers read a volatile snapshot
/// reference. The append path takes a brief lock only long enough to register the new
/// segment and update counters, then atomically swaps the snapshot pointer via
/// <see cref="Interlocked.Exchange(ref BatchSegment[]?, BatchSegment[]?)"/>. Readers never
/// block the writer and observe a consistent array of segments.
/// </para>
/// <list type="bullet">
/// <item>Append: O(1) amortised — one array snapshot allocation per append.</item>
/// <item>Random access: O(log n) — binary search across the snapshot.</item>
/// <item>Range read of k rows crossing b segments: O(log n + k + b).</item>
/// </list>
/// </remarks>
internal sealed class ResultSetIndex
{
    private readonly object _writeLock = new();
    private BatchSegment[]? _snapshot = Array.Empty<BatchSegment>();
    private long _loadedRowCount;

    public long LoadedRowCount => Interlocked.Read(ref _loadedRowCount);

    /// <summary>Adds a batch and atomically publishes a new snapshot. Writer-only.</summary>
    public void Append(BatchSegment segment)
    {
        if (segment.RowCount == 0)
            throw new ArgumentException("Batch must contain at least one row.", nameof(segment));

        lock (_writeLock)
        {
            if (_loadedRowCount != segment.StartRowIndex)
                throw new InvalidOperationException($"Batch start index {segment.StartRowIndex} does not match loaded row count {_loadedRowCount}.");
            var current = _snapshot!;
            var next = new BatchSegment[current.Length + 1];
            Array.Copy(current, next, current.Length);
            next[current.Length] = segment;
            _snapshot = next;
            _loadedRowCount = segment.EndRowIndex;
            Volatile.Write(ref _snapshot, next); // explicit publish
        }
    }

    /// <summary>
    /// Returns the batch containing the absolute row index, or -1 if no batch contains it
    /// (row has not yet been retained).
    /// </summary>
    public int FindSegmentIndex(long rowIndex)
    {
        var snapshot = Volatile.Read(ref _snapshot);
        if (snapshot is null || snapshot.Length == 0) return -1;
        // Binary search on StartRowIndex. Pick the segment whose start <= rowIndex
        // and whose start is the greatest such value.
        int lo = 0, hi = snapshot.Length - 1, found = -1;
        while (lo <= hi)
        {
            int mid = lo + ((hi - lo) >> 1);
            var seg = snapshot[mid];
            if (seg.StartRowIndex <= rowIndex)
            {
                found = mid;
                lo = mid + 1;
            }
            else hi = mid - 1;
        }
        if (found < 0) return -1;
        return snapshot[found].StartRowIndex + snapshot[found].RowCount > rowIndex ? found : -1;
    }

    /// <summary>Snapshot of the segment array at call time; safe to enumerate without locks.</summary>
    public BatchSegment[] Snapshot()
    {
        var s = Volatile.Read(ref _snapshot)!;
        return s;
    }

    /// <summary>Number of retained segments. Snapshot value.</summary>
    public int SegmentCount => Snapshot().Length;

    /// <summary>Releases all references and resets counters; called from <see cref="ResultSetStore.DisposeAsync"/>.</summary>
    public void Clear()
    {
        lock (_writeLock)
        {
            _snapshot = Array.Empty<BatchSegment>();
            _loadedRowCount = 0;
        }
    }
}