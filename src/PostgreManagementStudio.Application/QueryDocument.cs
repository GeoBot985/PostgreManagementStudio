using PostgreManagementStudio.Core;

namespace PostgreManagementStudio.Application;

public sealed class QueryDocument : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly ResultExecutionService _executionService;
    private readonly QueryExecutionLifecycle _lifecycle = new();
    private readonly IQueryExecutionTelemetry _telemetry;
    private CancellationTokenSource? _cancellation;
    private Task<IResultSession?>? _activeExecution;
    private Guid _connectionGenerationId;
    private int _disposed;

    public QueryDocument(ResultExecutionService executionService, string title, IQueryExecutionTelemetry? telemetry = null)
    {
        _executionService = executionService ?? throw new ArgumentNullException(nameof(executionService));
        _telemetry = telemetry ?? NullQueryExecutionTelemetry.Instance;
        Title = string.IsNullOrWhiteSpace(title) ? throw new ArgumentException("A title is required.", nameof(title)) : title;
    }

    public event EventHandler? ExecutionStateChanged;
    public Guid TabId { get; } = Guid.NewGuid();
    public string Title { get; }
    public string SqlText { get; set; } = "SELECT version();";
    public string ConnectionProfileId { get; set; } = "environment:PMS_CONNECTION_STRING";
    public string ConnectionString { get; set; } = string.Empty;
    public string Database { get; set; } = "postgres";
    public Guid ConnectionGenerationId { get { lock (_gate) return _connectionGenerationId; } }
    public QueryTransactionMode TransactionMode { get; set; }
    public QueryTransactionRecoveryState TransactionRecoveryState { get; private set; }
    public bool BackendStateMayBeStale { get; private set; }
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan CancellationTimeout { get; set; } = TimeSpan.FromSeconds(5);
    public bool IsDirty { get; private set; }
    public QueryDocumentExecutionState State => _lifecycle.State;
    public bool IsExecuting => _lifecycle.IsActive;
    public bool CanExecute => !IsExecuting && Volatile.Read(ref _disposed) == 0 && !string.IsNullOrWhiteSpace(ConnectionProfileId) && !string.IsNullOrWhiteSpace(ConnectionString) && !string.IsNullOrWhiteSpace(SqlText);
    public bool CanCancel => State is QueryDocumentExecutionState.Preparing or QueryDocumentExecutionState.Executing;
    public IResultSession? Session { get; private set; }
    public QueryExecutionContextSnapshot? LastExecutionContext { get; private set; }
    public string Message { get; private set; } = "Idle.";

    public void MarkDirty(bool dirty = true) => IsDirty = dirty;

    public void ReplaceConnection(
        string connectionProfileId,
        string connectionString,
        string database,
        Guid generationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionProfileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(database);
        if (generationId == Guid.Empty) throw new ArgumentException("A successful physical connection generation is required.", nameof(generationId));
        lock (_gate)
        {
            ThrowIfDisposed();
            var generationChanged = _connectionGenerationId != Guid.Empty && _connectionGenerationId != generationId;
            ConnectionProfileId = connectionProfileId;
            ConnectionString = connectionString;
            Database = database.Trim();
            _connectionGenerationId = generationId;
            BackendStateMayBeStale |= generationChanged;
            TransactionMode = QueryTransactionMode.Implicit;
            if (!_lifecycle.IsActive && _lifecycle.State != QueryDocumentExecutionState.Idle) _lifecycle.Reset();
            Message = generationChanged
                ? "Reconnected. Backend-session state is stale and must be re-established."
                : "Connected.";
        }
        RaiseStateChanged();
    }

    public bool InvalidateConnection(
        Guid generationId,
        string failureMessage,
        TransactionFailureWindow failureWindow = TransactionFailureWindow.DuringCommandExecution)
    {
        CancellationTokenSource? cancellation;
        lock (_gate)
        {
            if (generationId == Guid.Empty || generationId != _connectionGenerationId) return false;
            var transaction = TransactionRecoveryPolicy.Assess(
                TransactionMode == QueryTransactionMode.UserManaged,
                failureWindow);
            TransactionRecoveryState = transaction.State;
            TransactionMode = QueryTransactionMode.Implicit;
            BackendStateMayBeStale = true;
            ConnectionString = string.Empty;
            cancellation = _cancellation;
            if (_lifecycle.ExecutionId is { } executionId && _lifecycle.IsActive)
                _lifecycle.Finish(executionId, QueryDocumentExecutionState.ConnectionLost);
            Message = $"{SecretRedactor.Redact(failureMessage)} {transaction.Message}";
        }
        cancellation?.Cancel();
        RaiseStateChanged();
        return true;
    }

    public Task<IResultSession?> ExecuteAsync(string? selectedSql = null, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var sql = string.IsNullOrWhiteSpace(selectedSql) ? SqlText : selectedSql;
        if (string.IsNullOrWhiteSpace(sql)) throw new ArgumentException("SQL cannot be empty.", nameof(selectedSql));

        lock (_gate)
        {
            if (_lifecycle.IsActive) throw new InvalidOperationException("This query tab is already executing.");
            if (_lifecycle.State != QueryDocumentExecutionState.Idle) _lifecycle.Reset();

            var executionId = _lifecycle.Prepare();
            var connectionGenerationId = _connectionGenerationId;
            var startedAt = DateTimeOffset.UtcNow;
            var connectionString = QueryExecutionContextFactory.ResolveConnectionString(ConnectionString, Database);
            var context = QueryExecutionContextFactory.Capture(
                executionId,
                TabId,
                ConnectionProfileId,
                connectionString,
                Database,
                TransactionMode,
                sql,
                startedAt);
            var options = new QueryExecutionOptions(
                commandTimeout: CommandTimeout,
                cancellationTimeout: CancellationTimeout,
                transactionMode: TransactionMode,
                executionScopeId: TransactionMode == QueryTransactionMode.UserManaged ? TabId : null);
            _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            LastExecutionContext = context;
            Message = "Preparing query execution…";
            _activeExecution = RunExecutionAsync(context, connectionString, options, connectionGenerationId, _cancellation);
            RaiseStateChanged();
            return _activeExecution;
        }
    }

    public void Cancel()
    {
        CancellationTokenSource? cancellation;
        lock (_gate)
        {
            var executionId = _lifecycle.ExecutionId;
            if (executionId is null || !_lifecycle.RequestCancellation(executionId.Value)) return;
            Message = "Cancellation requested…";
            cancellation = _cancellation;
        }
        cancellation?.Cancel();
        RaiseStateChanged();
    }

    public async Task<bool> CancelAsync(CancellationToken cancellationToken = default)
    {
        Task? active;
        lock (_gate) active = _activeExecution;
        if (active is null || active.IsCompleted) return true;
        Cancel();
        var timeout = Task.Delay(CancellationTimeout, cancellationToken);
        if (await Task.WhenAny(active, timeout).ConfigureAwait(false) == active)
        {
            await active.ConfigureAwait(false);
            return true;
        }
        lock (_gate) Message = $"Cancellation did not complete within {CancellationTimeout.TotalSeconds:N0} seconds. Execution resources are being abandoned safely.";
        RaiseStateChanged();
        return false;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Cancel();
        Task? active;
        IResultSession? session;
        lock (_gate) { active = _activeExecution; session = Session; }
        if (active is not null)
        {
            try { await Task.WhenAny(active, Task.Delay(CancellationTimeout)).ConfigureAwait(false); }
            catch { /* terminal execution diagnostics already own the failure */ }
        }
        if (session is not null) await session.DisposeAsync().ConfigureAwait(false);
        await _executionService.CloseExecutionScopeAsync(TabId).ConfigureAwait(false);
    }

    private async Task<IResultSession?> RunExecutionAsync(
        QueryExecutionContextSnapshot context,
        string connectionString,
        QueryExecutionOptions options,
        Guid connectionGenerationId,
        CancellationTokenSource cancellation)
    {
        IResultSession? session = null;
        var cancellationRequested = false;
        try
        {
            if (!_lifecycle.MarkExecuting(context.ExecutionId)) return null;
            Message = "Executing…";
            RaiseStateChanged();

            var prior = Session;
            session = await _executionService.ExecuteAndBuildAsync(
                new QueryRequest(context.Sql, connectionString, options),
                cancellation.Token).ConfigureAwait(false);

            bool obsoleteGeneration;
            lock (_gate)
            {
                obsoleteGeneration = _connectionGenerationId != connectionGenerationId;
                if (obsoleteGeneration)
                    Message = "An obsolete connection generation completed after reconnection; its result was ignored.";
            }
            if (obsoleteGeneration)
            {
                await session.DisposeAsync().ConfigureAwait(false);
                session = null;
                return null;
            }

            cancellationRequested = cancellation.IsCancellationRequested;
            var terminal = session.Status switch
            {
                ResultSessionStatus.Cancelled => QueryDocumentExecutionState.Cancelled,
                ResultSessionStatus.Failed when session.Error?.Kind == DatabaseErrorKind.ConnectionLost => QueryDocumentExecutionState.ConnectionLost,
                ResultSessionStatus.Failed => QueryDocumentExecutionState.Failed,
                _ when cancellationRequested => QueryDocumentExecutionState.Cancelled,
                _ => QueryDocumentExecutionState.Completed,
            };
            if (_lifecycle.Finish(context.ExecutionId, terminal))
            {
                if (terminal == QueryDocumentExecutionState.Completed)
                {
                    Session = session;
                    if (prior is not null && !ReferenceEquals(prior, session))
                        await prior.DisposeAsync().ConfigureAwait(false);
                }
                Message = BuildMessage(session, context);
            }
            else
            {
                await session.DisposeAsync().ConfigureAwait(false);
                session = null;
            }
            return session;
        }
        catch (OperationCanceledException)
        {
            cancellationRequested = true;
            if (_lifecycle.Finish(context.ExecutionId, QueryDocumentExecutionState.Cancelled))
                Message = "Query cancelled by user.";
            return session;
        }
        catch (Exception ex)
        {
            var error = new DatabaseError(SecretRedactor.Redact(ex.Message), null, null, null, null, null, null, null, null, null, null, DatabaseErrorKind.Application);
            if (_lifecycle.Finish(context.ExecutionId, QueryDocumentExecutionState.Failed))
                Message = QueryErrorPresentation.Format(error);
            return session;
        }
        finally
        {
            var finalState = _lifecycle.ExecutionId == context.ExecutionId ? _lifecycle.State : QueryDocumentExecutionState.Cancelled;
            if (_lifecycle.ExecutionId == context.ExecutionId)
            {
                lock (_gate)
                {
                    _activeExecution = null;
                    _cancellation = null;
                }
            }
            cancellation.Dispose();
            _telemetry.Record(new(
                context.ExecutionId,
                context.EditorTabId,
                context.ConnectionProfileId,
                context.Database,
                context.StartedAt,
                DateTimeOffset.UtcNow,
                finalState,
                session?.RetainedRowCount ?? 0,
                session?.ReceivedRowCount ?? 0,
                cancellationRequested,
                session?.Error?.Kind == DatabaseErrorKind.Timeout ? "command" : null,
                session?.Error?.SqlState));
            RaiseStateChanged();
        }
    }

    private static string BuildMessage(IResultSession session, QueryExecutionContextSnapshot context)
    {
        var target = $"{context.ServerIdentity} / {context.Database} as {context.Username}";
        if (session.Status == ResultSessionStatus.Cancelled)
            return $"Query cancelled by user. Target: {target}. Execution time: {session.Elapsed}.";
        if (session.Status == ResultSessionStatus.Failed)
            return $"{QueryErrorPresentation.Format(session.Error!)}{Environment.NewLine}Target: {target}.{Environment.NewLine}Execution time: {session.Elapsed}.";
        var truncation = session.WasTruncated
            ? $" Display limited to {session.RetainedRowCount:N0} of {session.ReceivedRowCount:N0} rows ({session.TruncationReason})."
            : string.Empty;
        return $"Command completed successfully against {target}. {session.ReceivedRowCount:N0} rows received; {session.RowsAffected:N0} rows affected.{truncation} Execution time: {session.Elapsed}.";
    }

    private void RaiseStateChanged()
    {
        if (ExecutionStateChanged is not { } subscribers) return;
        foreach (EventHandler subscriber in subscribers.GetInvocationList())
        {
            try { subscriber(this, EventArgs.Empty); }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine(
                    $"query_state_subscriber_failed subscriber={subscriber.Method.DeclaringType?.FullName}.{subscriber.Method.Name} type={ex.GetType().FullName}");
            }
        }
    }
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
}
