using System.Runtime.CompilerServices;
using PostgreManagementStudio.Application;
using PostgreManagementStudio.Core;

namespace PostgreManagementStudio.Core.Tests;

public sealed class QueryDocumentTests
{
    private const string ConnectionA = "Host=server-a;Port=5432;Database=db_a;Username=user_a;SSL Mode=Require;Password=secret-a";
    private const string ConnectionB = "Host=server-b;Port=5433;Database=db_b;Username=user_b;Password=secret-b";

    [Fact]
    public void LifecycleRejectsInvalidTransitionsAndStaleCompletions()
    {
        var lifecycle = new QueryExecutionLifecycle();
        Assert.False(lifecycle.RequestCancellation(Guid.NewGuid()));
        var first = lifecycle.Prepare();
        Assert.True(lifecycle.MarkExecuting(first));
        Assert.False(lifecycle.Finish(Guid.NewGuid(), QueryDocumentExecutionState.Completed));
        Assert.True(lifecycle.RequestCancellation(first));
        Assert.True(lifecycle.RequestCancellation(first));
        Assert.False(lifecycle.Finish(first, QueryDocumentExecutionState.Completed));
        Assert.True(lifecycle.Finish(first, QueryDocumentExecutionState.Cancelled));
        Assert.False(lifecycle.RequestCancellation(first));
        lifecycle.Reset();
        Assert.Equal(QueryDocumentExecutionState.Idle, lifecycle.State);
    }

    [Fact]
    public async Task EmptySqlAndUnresolvedConnectionAreRejectedBeforeExecution()
    {
        var doc = Document(new NoOpExecutor(), "");
        doc.SqlText = " ";
        await Assert.ThrowsAsync<ArgumentException>(() => doc.ExecuteAsync());
        doc.SqlText = "SELECT 1";
        await Assert.ThrowsAsync<InvalidOperationException>(() => doc.ExecuteAsync());
        doc.ConnectionString = ConnectionA;
        doc.ConnectionProfileId = "";
        await Assert.ThrowsAsync<InvalidOperationException>(() => doc.ExecuteAsync());
    }

    [Fact]
    public async Task DuplicateExecutionIsRejectedAndCancellationIsIdempotent()
    {
        var executor = new ControlledExecutor();
        var doc = Document(executor, ConnectionA);
        var running = doc.ExecuteAsync();
        await executor.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(QueryDocumentExecutionState.Executing, doc.State);
        await Assert.ThrowsAsync<InvalidOperationException>(() => doc.ExecuteAsync());
        doc.Cancel();
        doc.Cancel();
        Assert.True(await doc.CancelAsync());
        await running;
        Assert.Equal(QueryDocumentExecutionState.Cancelled, doc.State);
        Assert.False(doc.CanCancel);
    }

    [Fact]
    public async Task ExecutionUsesImmutableConnectionAndDatabaseSnapshot()
    {
        var executor = new ControlledExecutor(ignoreCancellation: true);
        var doc = Document(executor, ConnectionA);
        doc.ConnectionProfileId = "profile-a";
        doc.Database = "db_a";
        doc.SqlText = "SELECT 41";
        var running = doc.ExecuteAsync();
        await executor.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        doc.ConnectionString = ConnectionB;
        doc.ConnectionProfileId = "profile-b";
        doc.Database = "db_b";
        doc.SqlText = "SELECT 99";
        executor.Release();
        await running;

        var requestValues = new System.Data.Common.DbConnectionStringBuilder { ConnectionString = executor.Request!.ConnectionString };
        Assert.Equal("server-a", requestValues["host"]);
        Assert.Equal("db_a", requestValues["database"]);
        Assert.Equal("SELECT 41", executor.Request.Sql);
        Assert.Equal("profile-a", doc.LastExecutionContext!.ConnectionProfileId);
        Assert.Equal("server-a:5432", doc.LastExecutionContext.ServerIdentity);
        Assert.Equal("db_a", doc.LastExecutionContext.Database);
        Assert.Equal("user_a", doc.LastExecutionContext.Username);
        Assert.Equal(QueryDocumentExecutionState.Completed, doc.State);
    }

    [Fact]
    public async Task CancellationTimeoutIsControlledAndLateCompletionCannotBecomeCompleted()
    {
        var executor = new ControlledExecutor(ignoreCancellation: true);
        var doc = Document(executor, ConnectionA);
        doc.CancellationTimeout = TimeSpan.FromMilliseconds(25);
        var running = doc.ExecuteAsync();
        await executor.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(await doc.CancelAsync());
        Assert.Contains("did not complete", doc.Message);
        executor.Release();
        await running;
        Assert.Equal(QueryDocumentExecutionState.Cancelled, doc.State);
    }

    [Fact]
    public async Task TelemetryContainsContextAndCountsButNoSqlOrConnectionString()
    {
        var telemetry = new RecordingTelemetry();
        var doc = new QueryDocument(new ResultExecutionService(new NoOpExecutor()), "Query 1", telemetry)
        {
            ConnectionString = ConnectionA,
            ConnectionProfileId = "profile-a",
            Database = "db_a",
            SqlText = "SELECT 'sensitive literal'",
        };
        await doc.ExecuteAsync();
        var item = Assert.Single(telemetry.Items);
        Assert.Equal(doc.TabId, item.EditorTabId);
        Assert.Equal("profile-a", item.ConnectionProfileId);
        Assert.Equal("db_a", item.Database);
        Assert.Equal(QueryDocumentExecutionState.Completed, item.FinalState);
        Assert.DoesNotContain("sensitive", item.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret-a", item.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TabsExecuteIndependentlyAndActiveTabCannotClose()
    {
        var executor = new PerRequestGateExecutor();
        var manager = new QueryTabManager(new ResultExecutionService(executor));
        var first = manager.Open(ConnectionA, "db_a");
        var second = manager.Open(ConnectionB, "db_b");
        first.SqlText = "SELECT 1";
        second.SqlText = "SELECT 2";

        var firstRun = first.ExecuteAsync();
        var secondRun = second.ExecuteAsync();
        await executor.BothStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(manager.TryClose(first, true));
        Assert.NotEqual(first.LastExecutionContext!.ExecutionId, second.LastExecutionContext!.ExecutionId);
        Assert.Equal("db_a", first.LastExecutionContext.Database);
        Assert.Equal("db_b", second.LastExecutionContext.Database);

        executor.Release();
        await Task.WhenAll(firstRun, secondRun);
        Assert.True(manager.TryClose(first, true));
        Assert.Single(manager.Documents);
    }

    [Fact]
    public void DirtyTabsNeedDiscardConsent()
    {
        var manager = new QueryTabManager(new ResultExecutionService(new NoOpExecutor()));
        var first = manager.Open(ConnectionA);
        manager.Open(ConnectionB, "other");
        first.MarkDirty();
        Assert.False(manager.TryClose(first, false));
        Assert.True(manager.TryClose(first, true));
    }

    [Fact]
    public void ErrorPresentationPreservesDiagnosticsAndRedactsSecrets()
    {
        var error = new DatabaseError(
            "duplicate value Password=hunter2",
            "ERROR",
            "23505",
            "Key already exists",
            "Choose another key",
            12,
            "public",
            "items",
            "id",
            "items_pkey",
            "_bt_check_unique",
            DatabaseErrorKind.Constraint,
            SourceFile: "nbtinsert.c",
            SourceLine: 666);
        var normal = QueryErrorPresentation.Format(error);
        Assert.Contains("SQLSTATE: 23505", normal);
        Assert.Contains("Constraint: items_pkey", normal);
        Assert.Contains("Password=<redacted>", normal);
        Assert.Equal("Password=<redacted>;Host=local", SecretRedactor.Redact("Password=\"two words\";Host=local"));
        Assert.DoesNotContain("nbtinsert.c", normal);
        Assert.Contains("nbtinsert.c", QueryErrorPresentation.Format(error, diagnosticMode: true));
    }

    [Theory]
    [InlineData(TransactionFailureWindow.BeforeCommandTransmission, QueryTransactionRecoveryState.ServerRolledBackUncommittedWork)]
    [InlineData(TransactionFailureWindow.DuringCommandExecution, QueryTransactionRecoveryState.ServerRolledBackUncommittedWork)]
    [InlineData(TransactionFailureWindow.AfterExecutionBeforeAcknowledgement, QueryTransactionRecoveryState.ServerRolledBackUncommittedWork)]
    [InlineData(TransactionFailureWindow.DuringCommit, QueryTransactionRecoveryState.OutcomeUnknown)]
    [InlineData(TransactionFailureWindow.DuringRollback, QueryTransactionRecoveryState.ServerRolledBackUncommittedWork)]
    public void TransactionRecoveryPolicyNeverRetriesAndClassifiesOutcome(
        TransactionFailureWindow window,
        QueryTransactionRecoveryState expected)
    {
        var result = TransactionRecoveryPolicy.Assess(true, window);
        Assert.Equal(expected, result.State);
        Assert.True(result.MustClearLocalTransaction);
        Assert.False(result.MayRetry);
    }

    [Fact]
    public async Task ConnectionGenerationInvalidationCancelsWorkAndCommitOutcomeIsUnknown()
    {
        var executor = new ControlledExecutor();
        await using var doc = Document(executor, "");
        var generation = Guid.NewGuid();
        doc.ReplaceConnection("profile-a", ConnectionA, "db_a", generation);
        doc.TransactionMode = QueryTransactionMode.UserManaged;
        var running = doc.ExecuteAsync();
        await executor.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(doc.InvalidateConnection(generation, "backend terminated",
            TransactionFailureWindow.DuringCommit));
        await running;

        Assert.Equal(QueryDocumentExecutionState.ConnectionLost, doc.State);
        Assert.Equal(QueryTransactionRecoveryState.OutcomeUnknown, doc.TransactionRecoveryState);
        Assert.False(doc.CanExecute);
        Assert.True(doc.BackendStateMayBeStale);
    }

    [Fact]
    public void ObsoleteGenerationFailureCannotInvalidateReplacementConnection()
    {
        var execution = new ResultExecutionService(new NoOpExecutor());
        var doc = new QueryDocument(execution, "Query 1");
        var oldGeneration = Guid.NewGuid();
        var newGeneration = Guid.NewGuid();
        doc.ReplaceConnection("profile-a", ConnectionA, "db_a", oldGeneration);
        doc.ReplaceConnection("profile-b", ConnectionB, "db_b", newGeneration);

        Assert.False(doc.InvalidateConnection(oldGeneration, "late failure"));
        Assert.Equal(ConnectionB, doc.ConnectionString);
        Assert.Equal(newGeneration, doc.ConnectionGenerationId);
    }

    [Fact]
    public async Task FailedExecutionPreservesLastSuccessfulResults()
    {
        var executor = new SuccessThenConnectionLossExecutor();
        await using var doc = Document(executor, "");
        doc.ReplaceConnection("profile-a", ConnectionA, "db_a", Guid.NewGuid());
        var successful = await doc.ExecuteAsync();
        Assert.NotNull(successful);
        Assert.Same(successful, doc.Session);

        var failed = await doc.ExecuteAsync();

        Assert.NotNull(failed);
        Assert.Equal(ResultSessionStatus.Failed, failed!.Status);
        Assert.Same(successful, doc.Session);
        await failed.DisposeAsync();
    }

    [Fact]
    public async Task ReconnectionNeverReplaysPreviouslySubmittedSql()
    {
        var executor = new CountingExecutor();
        await using var doc = Document(executor, "");
        var firstGeneration = Guid.NewGuid();
        doc.ReplaceConnection("profile-a", ConnectionA, "db_a", firstGeneration);
        await doc.ExecuteAsync();
        Assert.Equal(1, executor.Attempts);

        doc.InvalidateConnection(firstGeneration, "connection lost");
        doc.ReplaceConnection("profile-a", ConnectionA, "db_a", Guid.NewGuid());
        await Task.Yield();

        Assert.Equal(1, executor.Attempts);
        Assert.True(doc.CanExecute);
    }

    [Fact]
    public async Task FaultyStateSubscriberCannotAbortDatabaseExecution()
    {
        await using var doc = Document(new CountingExecutor(), ConnectionA);
        doc.ExecutionStateChanged += (_, _) => throw new FormatException("broken view formatting");

        var session = await doc.ExecuteAsync();

        Assert.NotNull(session);
        Assert.Equal(ResultSessionStatus.Completed, session!.Status);
        Assert.Equal(QueryDocumentExecutionState.Completed, doc.State);
    }

    private static QueryDocument Document(IQueryExecutor executor, string connection)
        => new(new ResultExecutionService(executor), "Query 1")
        {
            ConnectionString = connection,
            SqlText = "SELECT 1",
        };

    private sealed class NoOpExecutor : IQueryExecutor
    {
        public async IAsyncEnumerable<QueryExecutionEvent> ExecuteAsync(QueryRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield return new ExecutionStarted(DateTimeOffset.UtcNow);
            yield return new ExecutionCompleted(TimeSpan.Zero, 0);
        }
    }

    private sealed class ControlledExecutor(bool ignoreCancellation = false) : IQueryExecutor
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public QueryRequest? Request { get; private set; }
        public void Release() => _release.TrySetResult();

        public async IAsyncEnumerable<QueryExecutionEvent> ExecuteAsync(QueryRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Request = request;
            yield return new ExecutionStarted(DateTimeOffset.UtcNow);
            Started.TrySetResult();
            if (ignoreCancellation) await _release.Task;
            else await _release.Task.WaitAsync(cancellationToken);
            yield return new ExecutionCompleted(TimeSpan.Zero, 0);
        }
    }

    private sealed class PerRequestGateExecutor : IQueryExecutor
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _started;
        public TaskCompletionSource BothStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Release() => _release.TrySetResult();

        public async IAsyncEnumerable<QueryExecutionEvent> ExecuteAsync(QueryRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new ExecutionStarted(DateTimeOffset.UtcNow);
            if (Interlocked.Increment(ref _started) == 2) BothStarted.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
            yield return new ExecutionCompleted(TimeSpan.Zero, 0);
        }
    }

    private sealed class RecordingTelemetry : IQueryExecutionTelemetry
    {
        public List<QueryExecutionDiagnostic> Items { get; } = new();
        public void Record(QueryExecutionDiagnostic diagnostic) => Items.Add(diagnostic);
    }

    private sealed class SuccessThenConnectionLossExecutor : IQueryExecutor
    {
        private int _attempt;

        public async IAsyncEnumerable<QueryExecutionEvent> ExecuteAsync(
            QueryRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield return new ExecutionStarted(DateTimeOffset.UtcNow);
            if (Interlocked.Increment(ref _attempt) == 1)
            {
                yield return new ExecutionCompleted(TimeSpan.Zero, 0);
                yield break;
            }
            yield return new ExecutionFailed(new DatabaseError(
                "connection terminated", "FATAL", "57P01", null, null, null,
                null, null, null, null, null, DatabaseErrorKind.ConnectionLost));
        }
    }

    private sealed class CountingExecutor : IQueryExecutor
    {
        public int Attempts { get; private set; }
        public async IAsyncEnumerable<QueryExecutionEvent> ExecuteAsync(
            QueryRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            Attempts++;
            yield return new ExecutionStarted(DateTimeOffset.UtcNow);
            yield return new ExecutionCompleted(TimeSpan.Zero, 0);
        }
    }
}
