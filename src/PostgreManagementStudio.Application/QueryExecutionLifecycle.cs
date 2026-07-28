using System.Data.Common;
using PostgreManagementStudio.Core;

namespace PostgreManagementStudio.Application;

public enum QueryDocumentExecutionState
{
    Idle,
    Preparing,
    Executing,
    Cancelling,
    Completed,
    Failed,
    Cancelled,
    ConnectionLost,
}

public sealed record QueryExecutionContextSnapshot(
    Guid ExecutionId,
    Guid EditorTabId,
    string ConnectionProfileId,
    string ServerIdentity,
    string Database,
    string Username,
    string SslMode,
    QueryTransactionMode TransactionMode,
    string Sql,
    DateTimeOffset StartedAt);

public sealed record QueryExecutionDiagnostic(
    Guid ExecutionId,
    Guid EditorTabId,
    string ConnectionProfileId,
    string Database,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    QueryDocumentExecutionState FinalState,
    long DisplayedRows,
    long ServerRows,
    bool CancellationRequested,
    string? TimeoutCategory,
    string? SqlState);

public interface IQueryExecutionTelemetry
{
    void Record(QueryExecutionDiagnostic diagnostic);
}

public sealed class NullQueryExecutionTelemetry : IQueryExecutionTelemetry
{
    public static NullQueryExecutionTelemetry Instance { get; } = new();
    public void Record(QueryExecutionDiagnostic diagnostic) { }
}

public sealed class DiagnosticQueryExecutionTelemetry : IQueryExecutionTelemetry
{
    public void Record(QueryExecutionDiagnostic diagnostic)
    {
        System.Diagnostics.Trace.WriteLine(
            $"query_execution execution_id={diagnostic.ExecutionId} tab_id={diagnostic.EditorTabId} " +
            $"profile_id={SecretRedactor.Redact(diagnostic.ConnectionProfileId)} database={SecretRedactor.Redact(diagnostic.Database)} " +
            $"started={diagnostic.StartedAt:O} finished={diagnostic.FinishedAt:O} state={diagnostic.FinalState} " +
            $"displayed_rows={diagnostic.DisplayedRows} server_rows={diagnostic.ServerRows} " +
            $"cancel_requested={diagnostic.CancellationRequested} timeout={diagnostic.TimeoutCategory ?? "none"} " +
            $"sqlstate={diagnostic.SqlState ?? "none"}");
    }
}

public sealed class QueryExecutionLifecycle
{
    private readonly object _gate = new();
    private QueryDocumentExecutionState _state;
    private Guid? _executionId;

    public QueryDocumentExecutionState State { get { lock (_gate) return _state; } }
    public Guid? ExecutionId { get { lock (_gate) return _executionId; } }
    public bool IsActive => State is QueryDocumentExecutionState.Preparing or QueryDocumentExecutionState.Executing or QueryDocumentExecutionState.Cancelling;

    public Guid Prepare()
    {
        lock (_gate)
        {
            EnsureState(QueryDocumentExecutionState.Idle);
            _executionId = Guid.NewGuid();
            _state = QueryDocumentExecutionState.Preparing;
            return _executionId.Value;
        }
    }

    public bool MarkExecuting(Guid executionId) => Transition(executionId, QueryDocumentExecutionState.Executing, QueryDocumentExecutionState.Preparing);

    public bool RequestCancellation(Guid executionId)
    {
        lock (_gate)
        {
            if (_executionId != executionId) return false;
            if (_state == QueryDocumentExecutionState.Cancelling) return true;
            if (_state is not QueryDocumentExecutionState.Preparing and not QueryDocumentExecutionState.Executing) return false;
            _state = QueryDocumentExecutionState.Cancelling;
            return true;
        }
    }

    public bool Finish(Guid executionId, QueryDocumentExecutionState terminalState)
    {
        if (terminalState is not QueryDocumentExecutionState.Completed
            and not QueryDocumentExecutionState.Failed
            and not QueryDocumentExecutionState.Cancelled
            and not QueryDocumentExecutionState.ConnectionLost)
            throw new ArgumentOutOfRangeException(nameof(terminalState));

        lock (_gate)
        {
            if (_executionId != executionId) return false;
            if (_state is not QueryDocumentExecutionState.Preparing
                and not QueryDocumentExecutionState.Executing
                and not QueryDocumentExecutionState.Cancelling) return false;
            if (_state == QueryDocumentExecutionState.Cancelling && terminalState == QueryDocumentExecutionState.Completed) return false;
            _state = terminalState;
            return true;
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            if (_state is QueryDocumentExecutionState.Preparing or QueryDocumentExecutionState.Executing or QueryDocumentExecutionState.Cancelling)
                throw new InvalidOperationException($"Cannot reset an active execution in state {_state}.");
            _state = QueryDocumentExecutionState.Idle;
            _executionId = null;
        }
    }

    private bool Transition(Guid executionId, QueryDocumentExecutionState next, params QueryDocumentExecutionState[] allowed)
    {
        lock (_gate)
        {
            if (_executionId != executionId) return false;
            if (!allowed.Contains(_state)) throw new InvalidOperationException($"Invalid execution transition {_state} -> {next}.");
            _state = next;
            return true;
        }
    }

    private void EnsureState(QueryDocumentExecutionState expected)
    {
        if (_state != expected) throw new InvalidOperationException($"Expected execution state {expected}, but was {_state}.");
    }
}

public static class QueryExecutionContextFactory
{
    public static string ResolveConnectionString(string connectionString, string database)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) throw new InvalidOperationException("The selected connection profile has no usable connection.");
        if (string.IsNullOrWhiteSpace(database)) throw new InvalidOperationException("The intended database cannot be resolved.");
        try
        {
            var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };
            builder["Database"] = database.Trim();
            return builder.ConnectionString;
        }
        catch (ArgumentException)
        {
            return connectionString;
        }
    }

    public static QueryExecutionContextSnapshot Capture(
        Guid executionId,
        Guid editorTabId,
        string connectionProfileId,
        string connectionString,
        string database,
        QueryTransactionMode transactionMode,
        string sql,
        DateTimeOffset startedAt)
    {
        if (string.IsNullOrWhiteSpace(connectionProfileId)) throw new InvalidOperationException("The selected connection profile cannot be resolved.");
        if (string.IsNullOrWhiteSpace(connectionString)) throw new InvalidOperationException("The selected connection profile has no usable connection.");
        var values = Parse(connectionString);
        var host = Value(values, "Host", "Server", "Data Source") ?? "unknown";
        var port = Value(values, "Port") ?? "5432";
        var selectedDatabase = string.IsNullOrWhiteSpace(database) ? Value(values, "Database", "Initial Catalog") : database;
        if (string.IsNullOrWhiteSpace(selectedDatabase)) throw new InvalidOperationException("The intended database cannot be resolved.");
        return new(
            executionId,
            editorTabId,
            connectionProfileId,
            $"{host}:{port}",
            selectedDatabase,
            Value(values, "Username", "User ID", "UserName") ?? "unknown",
            Value(values, "SSL Mode", "SslMode") ?? "Prefer",
            transactionMode,
            sql,
            startedAt);
    }

    private static Dictionary<string, string> Parse(string connectionString)
    {
        try
        {
            var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };
            return builder.Keys.Cast<string>().ToDictionary(key => key, key => Convert.ToString(builder[key]) ?? string.Empty, StringComparer.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return new(StringComparer.OrdinalIgnoreCase) { ["Host"] = "unresolved" };
        }
    }

    private static string? Value(IReadOnlyDictionary<string, string> values, params string[] names)
        => names.Select(name => values.TryGetValue(name, out var value) ? value : null).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}

public static class QueryErrorPresentation
{
    public static string Format(DatabaseError error, bool diagnosticMode = false)
    {
        var lines = new List<string> { $"ERROR: {SecretRedactor.Redact(error.Message)}" };
        Add(lines, "SQLSTATE", error.SqlState);
        Add(lines, "Severity", error.Severity);
        Add(lines, "Detail", error.Detail);
        Add(lines, "Hint", error.Hint);
        if (error.Position is not null) lines.Add($"Position: {error.Position}");
        Add(lines, "Schema", error.SchemaName);
        Add(lines, "Table", error.TableName);
        Add(lines, "Column", error.ColumnName);
        Add(lines, "Constraint", error.ConstraintName);
        Add(lines, "Routine", error.Routine);
        if (diagnosticMode)
        {
            Add(lines, "Source file", error.SourceFile);
            if (error.SourceLine is not null) lines.Add($"Source line: {error.SourceLine}");
        }
        return string.Join(Environment.NewLine, lines);
    }

    private static void Add(ICollection<string> lines, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) lines.Add($"{label}: {SecretRedactor.Redact(value)}");
    }
}

public static class SecretRedactor
{
    private static readonly string[] Keys = ["Password", "Pwd", "Access Token", "Token", "Client Secret"];

    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var redacted = text;
        foreach (var key in Keys)
            redacted = System.Text.RegularExpressions.Regex.Replace(
                redacted,
                $@"(?i)({System.Text.RegularExpressions.Regex.Escape(key)}\s*=\s*)(?:""[^""]*""|'[^']*'|[^;\s]+)",
                "$1<redacted>");
        return redacted;
    }
}
