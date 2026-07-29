using Microsoft.Extensions.Logging;
using PostgreManagementStudio.Core;

namespace PostgreManagementStudio.Results;

/// <summary>
/// One query execution. Owns the per-result-set stores, accumulates notices,
/// tracks the terminal state, and aggregates memory accounting across stores.
/// </summary>
internal sealed class ResultSession : IResultSession
{
    private readonly ResultStorageOptions _options;
    private readonly ILogger? _logger;
    private readonly Guid _id = Guid.NewGuid();
    private readonly DateTimeOffset _createdAt = DateTimeOffset.UtcNow;
    private DateTimeOffset? _startedAt;

    // Stores, ordered by their creation index. Plain reference array swapped atomically.
    private readonly object _stateLock = new();
    private ResultSetStore[] _stores = Array.Empty<ResultSetStore>();
    private readonly Dictionary<int, ResultSetStore> _storesByIndex = new();
    private readonly List<DatabaseNotice> _notices = new();

    // Session-level state.
    private int _status; // ResultSessionStatus as int
    private long _receivedRowCount; // aggregate
    private long _rowsAffected;
    private long _memoryBytes;
    private int _truncatedFlag;
    private ResultTruncationReason _truncationReason;
    private DatabaseError? _error;
    private TimeSpan? _elapsed;
    private int _disposed;

    internal ResultSession(ResultStorageOptions options, ILogger? logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _logger = logger;
        _status = (int)ResultSessionStatus.Created;
        _logger?.LogInformation("Result session created ({SessionId})", _id);
    }

    // -----------------------------------------------------------------------
    // IResultSession
    // -----------------------------------------------------------------------

    public Guid Id => _id;
    public ResultSessionStatus Status => (ResultSessionStatus)Volatile.Read(ref _status);
    public IReadOnlyList<IResultSetStore> ResultSets
    {
        get
        {
            ThrowIfDisposed();
            return (IResultSetStore[])Volatile.Read(ref _stores).Clone();
        }
    }
    public IReadOnlyList<DatabaseNotice> Notices
    {
        get
        {
            ThrowIfDisposed();
            lock (_stateLock) return _notices.ToArray();
        }
    }
    public DatabaseError? Error
    {
        get
        {
            ThrowIfDisposed();
            lock (_stateLock) return _error;
        }
    }
    public TimeSpan? Elapsed
    {
        get
        {
            ThrowIfDisposed();
            lock (_stateLock) return _elapsed;
        }
    }
    public long EstimatedMemoryBytes => Interlocked.Read(ref _memoryBytes);

    public long ReceivedRowCount => Interlocked.Read(ref _receivedRowCount);
    public long RowsAffected => Interlocked.Read(ref _rowsAffected);

    public long RetainedRowCount
    {
        get
        {
            long total = 0;
            var snap = Volatile.Read(ref _stores);
            for (int i = 0; i < snap.Length; i++) total += snap[i].LoadedRowCount;
            return total;
        }
    }

    public bool WasTruncated
    {
        get
        {
            if (Volatile.Read(ref _truncatedFlag) != 0) return true;
            var snap = Volatile.Read(ref _stores);
            for (int i = 0; i < snap.Length; i++) if (snap[i].WasTruncated) return true;
            return false;
        }
    }

    public ResultTruncationReason? TruncationReason
    {
        get
        {
            if (Volatile.Read(ref _truncatedFlag) != 0) return _truncationReason;
            var snap = Volatile.Read(ref _stores);
            for (int i = 0; i < snap.Length; i++) if (snap[i].WasTruncated) return snap[i].TruncationReason;
            return null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        ResultSetStore[] stores;
        lock (_stateLock)
        {
            var current = (ResultSessionStatus)Volatile.Read(ref _status);
            if (current == ResultSessionStatus.Disposed) return;
            if (!LifecycleGuards.IsValid(current, ResultSessionStatus.Disposed))
                throw new InvalidOperationException($"Cannot dispose session in status {current}.");
            Volatile.Write(ref _status, (int)ResultSessionStatus.Disposed);
            stores = _stores;
            _stores = Array.Empty<ResultSetStore>();
            _storesByIndex.Clear();
            _notices.Clear();
            Interlocked.Exchange(ref _memoryBytes, 0);
        }
        for (var index = stores.Length - 1; index >= 0; index--)
        {
            try { await stores[index].DisposeAsync().ConfigureAwait(false); }
            catch (Exception ex) { _logger?.LogWarning(ex, "Error disposing result set {Index}", stores[index].ResultSetIndex); }
        }
        _logger?.LogTrace("Result session disposed ({SessionId})", _id);
    }

    // -----------------------------------------------------------------------
    // Internal mutators — used by ResultSessionBuilder.
    // -----------------------------------------------------------------------

    internal DateTimeOffset CreatedAt => _createdAt;
    internal ResultSetStore[] StoresInternal
    {
        get { lock (_stateLock) return (ResultSetStore[])Volatile.Read(ref _stores).Clone(); }
    }
    internal ResultStorageOptions Options => _options;

    internal void Start(DateTimeOffset startedAt)
    {
        lock (_stateLock)
        {
            if (_startedAt is null)
            {
                _startedAt = startedAt;
                var current = (ResultSessionStatus)Volatile.Read(ref _status);
                if (current == ResultSessionStatus.Created)
                {
                    LifecycleGuards.EnsureValid(current, ResultSessionStatus.Running);
                    Volatile.Write(ref _status, (int)ResultSessionStatus.Running);
                }
            }
        }
    }

    internal ResultSetStore CreateStore(int resultSetIndex, ResultSetSchema schema)
    {
        lock (_stateLock)
        {
            ThrowIfDisposed();
            if (_storesByIndex.ContainsKey(resultSetIndex))
                throw new DuplicateResultSetIndexException(resultSetIndex);
            var store = new ResultSetStore(resultSetIndex, schema, _options, _logger);
            _storesByIndex[resultSetIndex] = store;
            var newStores = new ResultSetStore[_stores.Length + 1];
            Array.Copy(_stores, newStores, _stores.Length);
            newStores[_stores.Length] = store;
            _stores = newStores;
            Volatile.Write(ref _stores, newStores);
            // Account for the new schema memory.
            Interlocked.Add(ref _memoryBytes, store.EstimatedMemoryBytes);
            _logger?.LogInformation("Result set created ({SessionId}, index {ResultSetIndex}, columns {ColumnCount})",
                _id, resultSetIndex, schema.Columns.Count);
            return store;
        }
    }

    internal ResultSetStore GetWriter(int resultSetIndex)
    {
        lock (_stateLock)
        {
            if (!_storesByIndex.TryGetValue(resultSetIndex, out var store))
                throw new InvalidBatchException($"No result set with index {resultSetIndex} has been created for this session.");
            return store;
        }
    }

    internal void AddNotice(DatabaseNotice notice)
    {
        ArgumentNullException.ThrowIfNull(notice);
        lock (_stateLock)
        {
            ThrowIfDisposed();
            _notices.Add(notice);
        }
    }

    internal void OnBatchRetained(ResultSetStore store, long batchBytes)
    {
        Interlocked.Add(ref _memoryBytes, batchBytes);
        if (Volatile.Read(ref _truncatedFlag) == 0 && EstimatedMemoryBytes > _options.MaximumSessionMemoryBytes)
        {
            // Walk stores and mark the most recently written one as truncated by session memory.
            var snap = Volatile.Read(ref _stores);
            for (int i = snap.Length - 1; i >= 0; i--)
            {
                if (ReferenceEquals(snap[i], store))
                {
                    snap[i].MarkSessionTruncated(ResultTruncationReason.SessionMemoryLimitReached);
                    break;
                }
            }
            Volatile.Write(ref _truncatedFlag, 1);
            _truncationReason = ResultTruncationReason.SessionMemoryLimitReached;
            _logger?.LogWarning(
                "Session memory limit triggered truncation ({SessionId}, reason SessionMemoryLimitReached)",
                _id);
        }
    }

    internal void AddReceivedRows(long received)
        => Interlocked.Add(ref _receivedRowCount, received);

    internal void AddRowsAffected(long rowsAffected)
    {
        if (rowsAffected > 0) Interlocked.Add(ref _rowsAffected, rowsAffected);
    }

    internal void OnResultSetCompleted(ResultSetStore store) { _ = store; }

    internal void Complete(TimeSpan elapsed, int resultSetCount)
    {
        lock (_stateLock)
        {
            var current = (ResultSessionStatus)Volatile.Read(ref _status);
            if (current is ResultSessionStatus.Completed or ResultSessionStatus.Disposed) return;
            LifecycleGuards.EnsureValid(current, ResultSessionStatus.Completed);
            Volatile.Write(ref _status, (int)ResultSessionStatus.Completed);
            _elapsed = elapsed;
        }
        _logger?.LogInformation(
            "Session completed ({SessionId}, elapsed {ElapsedMs} ms, result sets {ResultSetCount}, retained {Retained}, received {Received})",
            _id, elapsed.TotalMilliseconds, resultSetCount, RetainedRowCount, _receivedRowCount);
    }

    internal void Cancel(TimeSpan elapsed)
    {
        lock (_stateLock)
        {
            var current = (ResultSessionStatus)Volatile.Read(ref _status);
            if (current is ResultSessionStatus.Cancelled or ResultSessionStatus.Disposed) return;
            if (current == ResultSessionStatus.Failed)
                throw new InvalidOperationException("Cannot cancel a failed session.");
            LifecycleGuards.EnsureValid(current, ResultSessionStatus.Cancelled);
            Volatile.Write(ref _status, (int)ResultSessionStatus.Cancelled);
            _elapsed = elapsed;
        }
        _logger?.LogInformation("Session cancelled ({SessionId}, elapsed {ElapsedMs} ms)", _id, elapsed.TotalMilliseconds);
    }

    internal void Fail(DatabaseError error, TimeSpan? elapsed)
    {
        ArgumentNullException.ThrowIfNull(error);
        lock (_stateLock)
        {
            var current = (ResultSessionStatus)Volatile.Read(ref _status);
            if (current is ResultSessionStatus.Failed or ResultSessionStatus.Disposed) return;
            if (current == ResultSessionStatus.Completed)
                throw new InvalidOperationException("Cannot fail a completed session.");
            LifecycleGuards.EnsureValid(current, ResultSessionStatus.Failed);
            Volatile.Write(ref _status, (int)ResultSessionStatus.Failed);
            _error = error;
            _elapsed ??= elapsed;
        }
        _logger?.LogWarning("Session failed ({SessionId}, sqlstate {SqlState})", _id, error.SqlState);
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedResultStoreException($"Result session {_id} has been disposed.");
    }
}
