namespace PostgreManagementStudio.Core;

// ---------------------------------------------------------------------------
// Sprint 002 exception types. All derive from ResultStoreException so the
// visual layer can catch the base class once and present a consistent error.
// ---------------------------------------------------------------------------

/// <summary>Base type for all result-store failures. Distinct from database execution errors.</summary>
public class ResultStoreException : Exception
{
    public ResultStoreException(string message) : base(message) { }
    public ResultStoreException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Thrown when a read requests a row that has not been retained (or never will be).
/// Carries the relevant counts and the store's status so the visual layer can explain the failure.
/// </summary>
public sealed class ResultRowUnavailableException : ResultStoreException
{
    public long RequestedRowIndex { get; }
    public long LoadedRowCount { get; }
    public long FinalRowCount { get; }
    public ResultSetStatus Status { get; }

    public ResultRowUnavailableException(long requestedRowIndex, long loadedRowCount, long finalRowCount, ResultSetStatus status)
        : base(FormatMessage(requestedRowIndex, loadedRowCount, finalRowCount, status))
    {
        RequestedRowIndex = requestedRowIndex;
        LoadedRowCount = loadedRowCount;
        FinalRowCount = finalRowCount;
        Status = status;
    }

    private static string FormatMessage(long idx, long loaded, long final, ResultSetStatus status)
        => status switch
        {
            ResultSetStatus.Disposed => $"Row {idx} is unavailable; the result set has been disposed.",
            ResultSetStatus.Cancelled => $"Row {idx} is unavailable; the result set was cancelled after {loaded} rows.",
            ResultSetStatus.Failed    => $"Row {idx} is unavailable; the result set failed after {loaded} rows.",
            ResultSetStatus.Completed when idx >= loaded && idx < final
                                       => $"Row {idx} is outside the retained prefix of {loaded} rows (server reported {final}).",
            _ => $"Row {idx} is unavailable; loaded={loaded}, final={final}, status={status}."
        };
}

/// <summary>
/// Thrown when the writer receives a batch that violates ordering, start index, or column count rules.
/// </summary>
public sealed class InvalidBatchException : ResultStoreException
{
    public InvalidBatchException(string message) : base(message) { }
}

/// <summary>Thrown when a writer attempts to mutate a result set that has reached a terminal state.</summary>
public sealed class ResultSetTerminalException : ResultStoreException
{
    public ResultSetTerminalException(string message) : base(message) { }
}

/// <summary>Thrown when a builder receives a duplicate result-set index.</summary>
public sealed class DuplicateResultSetIndexException : ResultStoreException
{
    public int DuplicateIndex { get; }
    public DuplicateResultSetIndexException(int duplicateIndex)
        : base($"Result set index {duplicateIndex} was already created for this session.")
    {
        DuplicateIndex = duplicateIndex;
    }
}

/// <summary>Thrown when reading from a disposed result set or session.</summary>
public sealed class ObjectDisposedResultStoreException : ResultStoreException
{
    public ObjectDisposedResultStoreException(string message) : base(message) { }
}