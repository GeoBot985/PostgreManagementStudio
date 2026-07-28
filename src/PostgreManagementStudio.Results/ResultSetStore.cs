using Microsoft.Extensions.Logging;
using PostgreManagementStudio.Core;

namespace PostgreManagementStudio.Results;

/// <summary>
/// Append-only result-set store with random-access reads and configurable memory limits.
/// Implements both the public read-only contract and the internal writer used by the
/// session builder. Reads take a volatile snapshot of the batch index and never block
/// the writer.
/// </summary>
internal sealed class ResultSetStore : IResultSetStore, IResultSetWriter
{
    private readonly ResultSetSchema _schema;
    private readonly ResultStorageOptions _options;
    private readonly ILogger? _logger;
    private readonly ResultSetIndex _index = new();

    // Lifecycle
    private int _status; // ResultSetStatus as int for Interlocked
    private long _memoryBytes; // estimated
    private long _receivedRowCount; // monotonic
    private long _finalRowCount = -1;
    private int _truncatedFlag; // 0 = no, 1 = yes (lazy)
    private ResultTruncationReason _truncationReason;
    private DatabaseError? _failureError;

    // Disposal guard
    private int _disposed;

    private readonly object _stateLock = new();

    public ResultSetStore(int resultSetIndex, ResultSetSchema schema, ResultStorageOptions options, ILogger? logger)
    {
        if (resultSetIndex < 0) throw new ArgumentOutOfRangeException(nameof(resultSetIndex));
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(options);
        ResultSetIndex = resultSetIndex;
        _schema = schema;
        _options = options;
        _logger = logger;
        _memoryBytes = ResultSizeEstimator.EstimateSchemaBytes(schema);
    }

    // -----------------------------------------------------------------------
    // IResultSetStore
    // -----------------------------------------------------------------------

    public int ResultSetIndex { get; }

    public ResultSetSchema Schema => _schema;

    public ResultSetStatus Status => (ResultSetStatus)Volatile.Read(ref _status);

    public long LoadedRowCount => _index.LoadedRowCount;

    public long ReceivedRowCount => Interlocked.Read(ref _receivedRowCount);

    public long FinalRowCount => Interlocked.Read(ref _finalRowCount);

    public bool WasTruncated => Volatile.Read(ref _truncatedFlag) != 0;

    public ResultTruncationReason? TruncationReason
    {
        get
        {
            var r = _truncationReason;
            return Volatile.Read(ref _truncatedFlag) != 0 ? r : null;
        }
    }

    public long EstimatedMemoryBytes => Interlocked.Read(ref _memoryBytes);

    public ValueTask<ResultRow> GetRowAsync(long rowIndex, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        if (rowIndex < 0)
            throw new ResultRowUnavailableException(rowIndex, LoadedRowCount, FinalRowCount, Status);

        var snapshot = _index.Snapshot();
        // Binary search to find the segment.
        var segIdx = _index.FindSegmentIndex(rowIndex);
        if (segIdx < 0 || segIdx >= snapshot.Length)
            throw new ResultRowUnavailableException(rowIndex, LoadedRowCount, FinalRowCount, Status);

        var seg = snapshot[segIdx];
        var inner = (int)(rowIndex - seg.StartRowIndex);
        return ValueTask.FromResult(seg.Rows[inner]);
    }

    public ValueTask<IReadOnlyList<ResultRow>> GetRowsAsync(long startRowIndex, int count, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        if (startRowIndex < 0) throw new ArgumentOutOfRangeException(nameof(startRowIndex));
        if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));

        var loaded = LoadedRowCount;
        if (startRowIndex >= loaded)
            return ValueTask.FromResult<IReadOnlyList<ResultRow>>(Array.Empty<ResultRow>());

        var snapshot = _index.Snapshot();
        var segIdx = _index.FindSegmentIndex(startRowIndex);
        if (segIdx < 0)
            return ValueTask.FromResult<IReadOnlyList<ResultRow>>(Array.Empty<ResultRow>());

        var effectiveCount = (int)Math.Min(count, loaded - startRowIndex);
        var result = new ResultRow[effectiveCount];
        var written = 0;
        var currentIndex = startRowIndex;
        for (int i = segIdx; i < snapshot.Length && written < effectiveCount; i++)
        {
            var seg = snapshot[i];
            if (seg.StartRowIndex + seg.RowCount <= currentIndex) continue;
            var offsetInSeg = (int)(currentIndex - seg.StartRowIndex);
            var take = Math.Min(seg.RowCount - offsetInSeg, effectiveCount - written);
            Array.Copy(seg.Rows, offsetInSeg, result, written, take);
            written += take;
            currentIndex += take;
        }
        if (written < effectiveCount)
        {
            var trimmed = new ResultRow[written];
            Array.Copy(result, trimmed, written);
            return ValueTask.FromResult<IReadOnlyList<ResultRow>>(trimmed);
        }
        return ValueTask.FromResult<IReadOnlyList<ResultRow>>(result);
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return ValueTask.CompletedTask;
        lock (_stateLock)
        {
            var current = (ResultSetStatus)Volatile.Read(ref _status);
            if (current == ResultSetStatus.Disposed) return ValueTask.CompletedTask;
            // Disposed is valid from any non-terminal state and from the success terminals.
            if (!LifecycleGuards.IsValid(current, ResultSetStatus.Disposed))
                throw new InvalidOperationException($"Cannot dispose result set in status {current}.");
            Volatile.Write(ref _status, (int)ResultSetStatus.Disposed);
            _index.Clear();
            Interlocked.Exchange(ref _memoryBytes, 0);
        }
        _logger?.LogTrace("Result set disposed ({ResultSetIndex})", ResultSetIndex);
        return ValueTask.CompletedTask;
    }

    // -----------------------------------------------------------------------
    // IResultSetWriter — internal to Results
    // -----------------------------------------------------------------------

    public ValueTask AppendBatchAsync(ResultRowBatch batch, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(batch);

        // Validate columns once; rejected here if mismatch so the caller can react.
        if (batch.Rows.Count > 0)
        {
            var expected = _schema.Columns.Count;
            for (int i = 0; i < batch.Rows.Count; i++)
            {
                if (batch.Rows[i].Cells.Count != expected)
                    throw new InvalidBatchException(
                        $"Row {batch.StartRowIndex + i} has {batch.Rows[i].Cells.Count} cells but schema expects {expected}.");
            }
        }

        lock (_stateLock)
        {
            var current = (ResultSetStatus)Volatile.Read(ref _status);
            if (current is ResultSetStatus.Created or ResultSetStatus.Receiving)
            {
                // valid — proceed
            }
            else
            {
                throw new ResultSetTerminalException(
                    $"Cannot append to result set {ResultSetIndex} in status {current}.");
            }

            // Update received count for every batch — even truncated ones — so ReceivedRowCount
            // reflects what the executor produced.
            Interlocked.Add(ref _receivedRowCount, batch.Rows.Count);

            // If we are already truncated, do not retain further rows.
            if (Volatile.Read(ref _truncatedFlag) != 0)
            {
                return ValueTask.CompletedTask;
            }

            // Validate batch start index when about to retain.
            var loaded = _index.LoadedRowCount;
            if (batch.StartRowIndex != loaded)
                throw new InvalidBatchException(
                    $"Batch start index {batch.StartRowIndex} does not match loaded row count {loaded} for result set {ResultSetIndex}.");

            // Empty batches are tolerated (e.g. from the executor flushing). They must still be
            // in-order. We do not retain them.
            if (batch.Rows.Count == 0) return ValueTask.CompletedTask;

            // Promote status: first successful append moves Created -> Receiving.
            if (current == ResultSetStatus.Created)
            {
                LifecycleGuards.EnsureValid(current, ResultSetStatus.Receiving);
                Volatile.Write(ref _status, (int)ResultSetStatus.Receiving);
            }

            // A row limit may fall inside a provider batch. Retain the prefix up
            // to the exact limit instead of discarding the whole first batch.
            var truncateAfterAppend = false;
            var availableRows = _options.MaximumRowsPerResultSet - loaded;
            if (batch.Rows.Count > availableRows)
            {
                _truncationReason = ResultTruncationReason.MaximumRowsReached;
                if (availableRows <= 0)
                {
                    MarkTruncated();
                    return ValueTask.CompletedTask;
                }

                batch = new ResultRowBatch(
                    batch.StartRowIndex,
                    batch.Rows.Take(checked((int)availableRows)).ToArray());
                truncateAfterAppend = true;
            }

            // Result-set memory remains batch-atomic: retaining a partial value
            // batch could exceed the configured byte bound.
            if (EstimatedMemoryBytes + ComputeBatchBytes(batch) > _options.MaximumResultSetMemoryBytes)
            {
                _truncationReason = ResultTruncationReason.ResultSetMemoryLimitReached;
                MarkTruncated();
                return ValueTask.CompletedTask;
            }

            // Compute memory and append.
            var batchBytes = ComputeBatchBytes(batch);
            var newMemory = Interlocked.Add(ref _memoryBytes, batchBytes);
            var rowsArray = new ResultRow[batch.Rows.Count];
            for (int i = 0; i < rowsArray.Length; i++) rowsArray[i] = batch.Rows[i];
            var seg = new BatchSegment(batch.StartRowIndex, rowsArray, batchBytes);
            try
            {
                _index.Append(seg);
            }
            catch (InvalidOperationException)
            {
                // Reject overlap by restoring previous memory and rethrowing as InvalidBatch.
                Interlocked.Add(ref _memoryBytes, -batchBytes);
                throw new InvalidBatchException(
                    $"Batch start index {batch.StartRowIndex} conflicts with retained rows for result set {ResultSetIndex}.");
            }

            _logger?.LogTrace("Batch appended ({ResultSetIndex}, start {Start}, size {Size})",
                ResultSetIndex, batch.StartRowIndex, batch.Rows.Count);
            if (truncateAfterAppend) MarkTruncated();
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask CompleteAsync(long finalRowCount, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        if (finalRowCount < 0) throw new ArgumentOutOfRangeException(nameof(finalRowCount));

        lock (_stateLock)
        {
            var current = (ResultSetStatus)Volatile.Read(ref _status);
            if (current == ResultSetStatus.Completed) return ValueTask.CompletedTask;
            if (current is not (ResultSetStatus.Created or ResultSetStatus.Receiving))
                throw new ResultSetTerminalException(
                    $"Cannot complete result set {ResultSetIndex} in status {current}.");

            LifecycleGuards.EnsureValid(current, ResultSetStatus.Completed);
            Volatile.Write(ref _status, (int)ResultSetStatus.Completed);
            Interlocked.Exchange(ref _finalRowCount, finalRowCount);

            var loaded = _index.LoadedRowCount;
            if (loaded != finalRowCount && !WasTruncated)
            {
                _logger?.LogWarning(
                    "Result set {ResultSetIndex} reported {Final} rows but {Loaded} were retained",
                    ResultSetIndex, finalRowCount, loaded);
            }
        }
        _logger?.LogInformation(
            "Result set completed ({ResultSetIndex}, retained {Retained}, final {Final})",
            ResultSetIndex, _index.LoadedRowCount, finalRowCount);
        return ValueTask.CompletedTask;
    }

    public ValueTask CancelAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _disposed, 0) == 1)
        {
            // Already disposed; treat as no-op per idempotent semantics.
            return ValueTask.CompletedTask;
        }
        lock (_stateLock)
        {
            var current = (ResultSetStatus)Volatile.Read(ref _status);
            if (current is ResultSetStatus.Cancelled or ResultSetStatus.Disposed) return ValueTask.CompletedTask;
            if (current == ResultSetStatus.Failed)
                throw new ResultSetTerminalException("Cannot cancel a failed result set.");
            LifecycleGuards.EnsureValid(current, ResultSetStatus.Cancelled);
            Volatile.Write(ref _status, (int)ResultSetStatus.Cancelled);
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask FailAsync(DatabaseError error, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(error);
        lock (_stateLock)
        {
            var current = (ResultSetStatus)Volatile.Read(ref _status);
            if (current is ResultSetStatus.Failed or ResultSetStatus.Disposed) return ValueTask.CompletedTask;
            if (current == ResultSetStatus.Completed)
                throw new ResultSetTerminalException("Cannot fail a completed result set.");
            LifecycleGuards.EnsureValid(current, ResultSetStatus.Failed);
            Volatile.Write(ref _status, (int)ResultSetStatus.Failed);
            // First error wins.
            if (_failureError is null) _failureError = error;
        }
        return ValueTask.CompletedTask;
    }

    // -----------------------------------------------------------------------
    // Internals
    // -----------------------------------------------------------------------

    private void MarkTruncated()
    {
        Volatile.Write(ref _truncatedFlag, 1);
        _logger?.LogWarning(
            "Truncation triggered ({ResultSetIndex}, reason {Reason}, retained {Retained}, received {Received})",
            ResultSetIndex, _truncationReason, _index.LoadedRowCount, ReceivedRowCount);
    }

    private long ComputeBatchBytes(ResultRowBatch batch)
    {
        long bytes = ResultSizeEstimator.EstimateBatchOverheadBytes(batch.Rows.Count);
        for (int i = 0; i < batch.Rows.Count; i++)
        {
            var row = batch.Rows[i];
            bytes += ResultSizeEstimator.EstimateRowOverheadBytes(row);
            for (int c = 0; c < row.Cells.Count; c++) bytes += ResultSizeEstimator.EstimateCellBytes(row.Cells[c]);
        }
        return bytes;
    }

    /// <summary>
    /// Session-level memory helper used by <see cref="ResultSession"/> to enforce the
    /// <c>MaximumSessionMemoryBytes</c> limit. The session subtracts a batch that pushes
    /// the total over its limit and asks the store to truncate with the session reason.
    /// </summary>
    internal bool TryReserveSessionMemory(long projectedSessionBytes, long batchBytes)
        => projectedSessionBytes <= _options.MaximumSessionMemoryBytes;

    internal void MarkSessionTruncated(ResultTruncationReason reason)
    {
        lock (_stateLock)
        {
            if (Volatile.Read(ref _truncatedFlag) != 0) return;
            Volatile.Write(ref _truncatedFlag, 1);
            _truncationReason = reason;
            _logger?.LogWarning(
                "Session memory limit triggered truncation ({ResultSetIndex}, reason {Reason})",
                ResultSetIndex, reason);
        }
    }

    internal ResultTruncationReason SessionLevelTruncationReason => ResultTruncationReason.SessionMemoryLimitReached;

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedResultStoreException(
                $"Result set {ResultSetIndex} has been disposed.");
    }
}
