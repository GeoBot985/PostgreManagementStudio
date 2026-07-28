using PostgreManagementStudio.Core;

namespace PostgreManagementStudio.Application;

public enum QueryDocumentExecutionState { Idle, Running, Completed, Failed, Cancelled }

public sealed class QueryDocument
{
    private readonly ResultExecutionService _executionService;
    private CancellationTokenSource? _cancellation;
    public QueryDocument(ResultExecutionService executionService, string title) { _executionService = executionService; Title = title; }
    public string Title { get; }
    public string SqlText { get; set; } = "SELECT version();";
    public string ConnectionString { get; set; } = string.Empty;
    public string Database { get; set; } = "postgres";
    public bool IsDirty { get; private set; }
    public QueryDocumentExecutionState State { get; private set; }
    public IResultSession? Session { get; private set; }
    public string Message { get; private set; } = "Idle.";
    public bool CanExecute => State != QueryDocumentExecutionState.Running && !string.IsNullOrWhiteSpace(ConnectionString) && !string.IsNullOrWhiteSpace(SqlText);
    public void MarkDirty(bool dirty = true) => IsDirty = dirty;
    public void Cancel() => _cancellation?.Cancel();
    public async Task<IResultSession?> ExecuteAsync(string? selectedSql = null, CancellationToken cancellationToken = default)
    {
        var sql = string.IsNullOrWhiteSpace(selectedSql) ? SqlText : selectedSql;
        if (string.IsNullOrWhiteSpace(sql)) throw new ArgumentException("SQL cannot be empty.", nameof(selectedSql));
        if (string.IsNullOrWhiteSpace(ConnectionString)) throw new InvalidOperationException("Select a PostgreSQL connection before executing.");
        if (State == QueryDocumentExecutionState.Running) throw new InvalidOperationException("This query tab is already executing.");
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); State = QueryDocumentExecutionState.Running; Message = "Running…";
        try { Session = await _executionService.ExecuteAndBuildAsync(new QueryRequest(sql, ConnectionString, new QueryExecutionOptions()), _cancellation.Token); State = Session.Status switch { ResultSessionStatus.Cancelled => QueryDocumentExecutionState.Cancelled, ResultSessionStatus.Failed => QueryDocumentExecutionState.Failed, _ => QueryDocumentExecutionState.Completed }; Message = BuildMessage(Session); return Session; }
        catch (OperationCanceledException) { State = QueryDocumentExecutionState.Cancelled; Message = "Query cancelled by user."; return Session; }
        finally { _cancellation.Dispose(); _cancellation = null; }
    }
    private static string BuildMessage(IResultSession session) => session.Status == ResultSessionStatus.Cancelled ? $"Query cancelled by user. Execution time: {session.Elapsed}." : session.Status == ResultSessionStatus.Failed ? $"ERROR: {session.Error?.Message}\nSQLSTATE: {session.Error?.SqlState}\nExecution time: {session.Elapsed}." : $"Command completed successfully. {session.ReceivedRowCount:N0} rows returned. Execution time: {session.Elapsed}.";
}
