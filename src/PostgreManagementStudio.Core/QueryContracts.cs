using System.Diagnostics;

namespace PostgreManagementStudio.Core;

public enum QueryTransactionMode { Implicit, UserManaged }

public interface IPostgresVersionQuery
{
    Task<string> ExecuteAsync(string connectionString, CancellationToken cancellationToken = default);
}

public sealed record QueryExecutionOptions
{
    public QueryExecutionOptions(
        int rowBatchSize = 256,
        TimeSpan? commandTimeout = null,
        TimeSpan? cancellationTimeout = null,
        QueryTransactionMode transactionMode = QueryTransactionMode.Implicit,
        Guid? executionScopeId = null)
    {
        if (rowBatchSize <= 0) throw new ArgumentOutOfRangeException(nameof(rowBatchSize));
        if (commandTimeout is { } timeout && timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(commandTimeout));
        if (cancellationTimeout is { } cancellation && cancellation <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(cancellationTimeout));
        RowBatchSize = rowBatchSize;
        CommandTimeout = commandTimeout;
        CancellationTimeout = cancellationTimeout ?? TimeSpan.FromSeconds(5);
        TransactionMode = transactionMode;
        ExecutionScopeId = executionScopeId;
        if (transactionMode == QueryTransactionMode.UserManaged && executionScopeId is null)
            throw new ArgumentException("User-managed transactions require an execution scope.", nameof(executionScopeId));
    }
    public int RowBatchSize { get; }
    public TimeSpan? CommandTimeout { get; }
    public TimeSpan CancellationTimeout { get; }
    public QueryTransactionMode TransactionMode { get; }
    public Guid? ExecutionScopeId { get; }
}

public sealed record QueryRequest
{
    public QueryRequest(string sql, string connectionString, QueryExecutionOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(sql)) throw new ArgumentException("SQL is required.", nameof(sql));
        if (string.IsNullOrWhiteSpace(connectionString)) throw new ArgumentException("A connection string is required.", nameof(connectionString));
        Sql = sql; ConnectionString = connectionString; Options = options ?? new();
    }
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    public string Sql { get; }
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    public string ConnectionString { get; }
    public QueryExecutionOptions Options { get; }
    public override string ToString() => $"QueryRequest (transaction={Options.TransactionMode}, SQL and connection redacted)";
}

public interface IQueryExecutor
{
    IAsyncEnumerable<QueryExecutionEvent> ExecuteAsync(QueryRequest request, CancellationToken cancellationToken = default);
}

public interface IQueryExecutionScopeManager
{
    ValueTask CloseScopeAsync(Guid executionScopeId);
}

public abstract record QueryExecutionEvent;
public sealed record ExecutionStarted(DateTimeOffset StartedAt) : QueryExecutionEvent;
public sealed record ResultSetStarted(int ResultSetIndex, ResultSetSchema Schema) : QueryExecutionEvent;
public sealed record RowBatchReceived(int ResultSetIndex, ResultRowBatch Batch) : QueryExecutionEvent;
public sealed record ResultSetCompleted(int ResultSetIndex, long RowCount) : QueryExecutionEvent;
public sealed record DatabaseNoticeReceived(DatabaseNotice Notice) : QueryExecutionEvent;
public sealed record CommandCompleted(string CommandTag, long? RowsAffected) : QueryExecutionEvent;
public sealed record ExecutionFailed(DatabaseError Error) : QueryExecutionEvent;
public sealed record ExecutionCancelled(TimeSpan Elapsed) : QueryExecutionEvent;
public sealed record ExecutionCompleted(TimeSpan Elapsed, int ResultSetCount) : QueryExecutionEvent;

public sealed record ResultColumn(int Ordinal, string Name, string PostgreSqlTypeName, uint? PostgreSqlTypeOid, Type? ClrType, bool? IsNullable);
public sealed record ResultSetSchema(IReadOnlyList<ResultColumn> Columns);
public sealed record ResultCell(object? Value, bool IsNull);
public sealed record ResultRow(IReadOnlyList<ResultCell> Cells);
public sealed record ResultRowBatch(long StartRowIndex, IReadOnlyList<ResultRow> Rows);
public sealed record DatabaseNotice(string? Severity, string? SqlState, string Message, string? Detail, string? Hint, DateTimeOffset ReceivedAt);
public enum DatabaseErrorKind { Query, Constraint, Authentication, Timeout, ConnectionLost, Provider, Application }

public sealed record DatabaseError(
    string Message,
    string? Severity,
    string? SqlState,
    string? Detail,
    string? Hint,
    int? Position,
    string? SchemaName,
    string? TableName,
    string? ColumnName,
    string? ConstraintName,
    string? Routine,
    DatabaseErrorKind Kind = DatabaseErrorKind.Query,
    int? InternalPosition = null,
    string? SourceFile = null,
    int? SourceLine = null);
