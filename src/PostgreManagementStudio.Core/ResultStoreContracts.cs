namespace PostgreManagementStudio.Core;

// ---------------------------------------------------------------------------
// Sprint 002 result-storage contracts. Provider-neutral; no Npgsql, no WPF.
// ---------------------------------------------------------------------------

/// <summary>Lifecycle states for a <see cref="IResultSession"/>.</summary>
public enum ResultSessionStatus
{
    Created,
    Running,
    Completed,
    Cancelled,
    Failed,
    Disposed
}

/// <summary>Lifecycle states for an <see cref="IResultSetStore"/>.</summary>
public enum ResultSetStatus
{
    Created,
    Receiving,
    Completed,
    Cancelled,
    Failed,
    Disposed
}

/// <summary>Reason a result set or session stopped retaining further rows.</summary>
public enum ResultTruncationReason
{
    MaximumRowsReached,
    ResultSetMemoryLimitReached,
    SessionMemoryLimitReached
}

/// <summary>
/// Configurable in-memory limits for result retention. All limits must be positive.
/// <see cref="ResultStorageOptions.Default"/> provides the development defaults used by the suite.
/// </summary>
public sealed record ResultStorageOptions
{
    public ResultStorageOptions(long maximumSessionMemoryBytes, long maximumResultSetMemoryBytes, long maximumRowsPerResultSet)
    {
        if (maximumSessionMemoryBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumSessionMemoryBytes));
        if (maximumResultSetMemoryBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumResultSetMemoryBytes));
        if (maximumRowsPerResultSet <= 0) throw new ArgumentOutOfRangeException(nameof(maximumRowsPerResultSet));
        MaximumSessionMemoryBytes = maximumSessionMemoryBytes;
        MaximumResultSetMemoryBytes = maximumResultSetMemoryBytes;
        MaximumRowsPerResultSet = maximumRowsPerResultSet;
    }

    public long MaximumSessionMemoryBytes { get; }
    public long MaximumResultSetMemoryBytes { get; }
    public long MaximumRowsPerResultSet { get; }

    /// <summary>Development defaults: 256 MiB per session, 128 MiB per result set, 1 000 000 rows per result set.</summary>
    public static ResultStorageOptions Default { get; } = new(
        maximumSessionMemoryBytes: 256L * 1024 * 1024,
        maximumResultSetMemoryBytes: 128L * 1024 * 1024,
        maximumRowsPerResultSet: 1_000_000L);
}

/// <summary>
/// Read-only view over one query execution: a session of result-set stores plus notices,
/// terminal state, error information, elapsed time, and partial data when execution ended early.
/// </summary>
public interface IResultSession : IAsyncDisposable
{
    Guid Id { get; }
    ResultSessionStatus Status { get; }
    IReadOnlyList<IResultSetStore> ResultSets { get; }
    IReadOnlyList<DatabaseNotice> Notices { get; }
    DatabaseError? Error { get; }
    TimeSpan? Elapsed { get; }
    long EstimatedMemoryBytes { get; }
    long ReceivedRowCount { get; }
    long RetainedRowCount { get; }
    long RowsAffected { get; }
    bool WasTruncated { get; }
    ResultTruncationReason? TruncationReason { get; }
}

/// <summary>
/// Read-only view over a single retained result set. Reads are random-access and thread-safe.
/// </summary>
public interface IResultSetStore : IAsyncDisposable
{
    int ResultSetIndex { get; }
    ResultSetSchema Schema { get; }
    ResultSetStatus Status { get; }

    /// <summary>Number of rows currently retained in memory.</summary>
    long LoadedRowCount { get; }

    /// <summary>Cumulative rows observed across all batches for this store (received = retained + truncated-dropped).</summary>
    long ReceivedRowCount { get; }

    /// <summary>Server-reported row count when the result set completed; -1 if still receiving or terminated early.</summary>
    long FinalRowCount { get; }

    bool WasTruncated { get; }
    ResultTruncationReason? TruncationReason { get; }
    long EstimatedMemoryBytes { get; }

    /// <summary>
    /// Returns the retained row at <paramref name="rowIndex"/>.
    /// Throws <see cref="ResultRowUnavailableException"/> if the row has not been retained yet
    /// (or never will be) or the store is disposed.
    /// </summary>
    ValueTask<ResultRow> GetRowAsync(long rowIndex, CancellationToken cancellationToken);

    /// <summary>
    /// Returns up to <paramref name="count"/> retained rows starting at <paramref name="startRowIndex"/>.
    /// If the range extends past <see cref="LoadedRowCount"/>, returns the loaded prefix only — never blocks
    /// waiting for later batches and never fabricates rows.
    /// </summary>
    ValueTask<IReadOnlyList<ResultRow>> GetRowsAsync(long startRowIndex, int count, CancellationToken cancellationToken);
}

/// <summary>
/// Internal append-only mutator for an <see cref="IResultSetStore"/>. The visual layer sees
/// the read-only <see cref="IResultSetStore"/>; only the session builder uses this contract.
/// </summary>
internal interface IResultSetWriter
{
    ValueTask AppendBatchAsync(ResultRowBatch batch, CancellationToken cancellationToken);
    ValueTask CompleteAsync(long finalRowCount, CancellationToken cancellationToken);
    ValueTask CancelAsync(CancellationToken cancellationToken);
    ValueTask FailAsync(DatabaseError error, CancellationToken cancellationToken);
}

/// <summary>
/// Builds a fully-retained <see cref="IResultSession"/> by consuming the Sprint 001 execution event stream.
/// </summary>
public interface IResultSessionBuilder
{
    Task<IResultSession> ExecuteAndBuildAsync(QueryRequest request, CancellationToken cancellationToken);
}
