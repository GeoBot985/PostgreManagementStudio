using System.Net.Sockets;
using PostgreManagementStudio.Postgres;

namespace PostgreManagementStudio.Postgres.Tests;

public sealed class ConnectionRecoveryTests
{
    private static readonly EffectiveConnectionConfiguration Configuration =
        EffectiveConnectionConfigurationBuilder.FromConnectionString(
            "recovery-tests",
            "Host=localhost;Database=postgres;Username=postgres;Password=test-only",
            "PostgreManagementStudio Recovery Tests");

    [Theory]
    [InlineData(RecoveryConnectionState.Disconnected, RecoveryConnectionState.Connecting, true)]
    [InlineData(RecoveryConnectionState.Connecting, RecoveryConnectionState.Connected, true)]
    [InlineData(RecoveryConnectionState.Connected, RecoveryConnectionState.Degraded, true)]
    [InlineData(RecoveryConnectionState.Degraded, RecoveryConnectionState.Reconnecting, true)]
    [InlineData(RecoveryConnectionState.Reconnecting, RecoveryConnectionState.Connected, true)]
    [InlineData(RecoveryConnectionState.Failed, RecoveryConnectionState.Reconnecting, true)]
    [InlineData(RecoveryConnectionState.Connected, RecoveryConnectionState.Connecting, false)]
    [InlineData(RecoveryConnectionState.Disconnected, RecoveryConnectionState.Connected, false)]
    [InlineData(RecoveryConnectionState.Disposed, RecoveryConnectionState.Connecting, false)]
    public void TransitionTableIsExplicit(
        RecoveryConnectionState from,
        RecoveryConnectionState to,
        bool expected) =>
        Assert.Equal(expected, ConnectionRecoverySession.IsValidTransition(from, to));

    [Fact]
    public async Task ConcurrentReconnectRequestsShareOnePhysicalAttempt()
    {
        var probe = new ControlledProbe();
        probe.Enqueue(Success(101));
        await using var session = new ConnectionRecoverySession(probe);
        var firstConnection = session.ConnectAsync(Configuration);
        probe.Release();
        await firstConnection;
        session.ReportFailure(new(DatabaseFailureKind.NetworkInterruption, true, "network interrupted"));

        probe.ResetGate();
        probe.Enqueue(Success(202));
        var first = session.ReconnectAsync();
        var second = session.ReconnectAsync();

        Assert.Same(first, second);
        Assert.Equal(2, probe.CallCount);
        probe.Release();
        var snapshot = await first;
        Assert.Equal(RecoveryConnectionState.Connected, snapshot.State);
        Assert.Equal(202, snapshot.BackendProcessId);
        Assert.Equal(1, snapshot.ReconnectionAttemptCount);
    }

    [Fact]
    public async Task SuccessfulReconnectReplacesGenerationAndCancelsDependents()
    {
        var probe = new QueueProbe(Success(11), Success(22));
        var diagnostics = new RecordingDiagnostics();
        await using var session = new ConnectionRecoverySession(probe, diagnostics);
        var connected = await session.ConnectAsync(Configuration);
        var oldToken = session.GenerationToken;

        session.ReportFailure(new(DatabaseFailureKind.ServerShutdown, true, "server restarted"));
        var reconnected = await session.ReconnectAsync();

        Assert.True(oldToken.IsCancellationRequested);
        Assert.NotEqual(connected.GenerationId, reconnected.GenerationId);
        Assert.Equal(22, reconnected.BackendProcessId);
        Assert.Contains("Temporary objects", reconnected.StaleStateWarning);
        Assert.Contains(diagnostics.Items, item => item.Operation == "Reconnect"
            && item.PreviousBackendProcessId is null
            && item.BackendProcessId == 22);
    }

    [Fact]
    public async Task AuthenticationFailureIsTerminalAndDoesNotExposeSecret()
    {
        var probe = new QueueProbe(Failure(ConnectionFailureCategory.Authentication,
            "password=redaction-sentinel was rejected", "28P01"));
        await using var session = new ConnectionRecoverySession(probe);

        var snapshot = await session.ConnectAsync(Configuration);

        Assert.Equal(RecoveryConnectionState.Failed, snapshot.State);
        Assert.Equal(DatabaseFailureKind.AuthenticationFailure, snapshot.Failure?.Kind);
        Assert.False(snapshot.Failure?.IsTransient);
        Assert.DoesNotContain("redaction-sentinel", snapshot.Failure?.Message);
    }

    [Fact]
    public async Task ReconnectAuthenticationFailureBecomesFailedWithoutReplacingGeneration()
    {
        var probe = new QueueProbe(
            Success(10),
            Failure(ConnectionFailureCategory.Authentication, "password=redaction-sentinel", "28P01"));
        await using var session = new ConnectionRecoverySession(probe);
        var connected = await session.ConnectAsync(Configuration);
        session.ReportFailure(new(DatabaseFailureKind.NetworkInterruption, true, "lost"));

        var failed = await session.ReconnectAsync();

        Assert.Equal(RecoveryConnectionState.Failed, failed.State);
        Assert.Equal(connected.GenerationId, failed.GenerationId);
        Assert.Equal(DatabaseFailureKind.AuthenticationFailure, failed.Failure?.Kind);
        Assert.DoesNotContain("redaction-sentinel", failed.Failure?.Message);
    }

    [Fact]
    public async Task ReconnectHonoursCancellationAndRemainsRecoverable()
    {
        var probe = new ControlledProbe();
        probe.Enqueue(Success(10));
        await using var session = new ConnectionRecoverySession(probe);
        var initial = session.ConnectAsync(Configuration);
        probe.Release();
        await initial;
        session.ReportFailure(new(DatabaseFailureKind.NetworkInterruption, true, "lost"));

        probe.ResetGate();
        probe.Enqueue(Success(20));
        using var cancellation = new CancellationTokenSource();
        var reconnect = session.ReconnectAsync(cancellation.Token);
        await probe.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();
        var snapshot = await reconnect;

        Assert.Equal(RecoveryConnectionState.Degraded, snapshot.State);
        Assert.True(session.CanReconnect);
    }

    [Fact]
    public async Task DisconnectCancelsInFlightProbeAndStaleCompletionCannotReconnect()
    {
        var probe = new ControlledProbe(ignoreCancellation: true);
        probe.Enqueue(Success(55));
        await using var session = new ConnectionRecoverySession(probe);
        var attempt = session.ConnectAsync(Configuration);
        await probe.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        session.Disconnect();
        probe.Release();
        var snapshot = await attempt;

        Assert.Equal(RecoveryConnectionState.Disconnected, snapshot.State);
        Assert.Equal(Guid.Empty, snapshot.GenerationId);
        Assert.Null(snapshot.BackendProcessId);
    }

    [Fact]
    public async Task InitialSessionCannotReconnectAndLateHealthFailureAfterDisconnectIsIgnored()
    {
        var probe = new ControlledProbe(ignoreCancellation: true);
        await using var session = new ConnectionRecoverySession(probe);
        Assert.False(session.CanReconnect);

        probe.Enqueue(Success(56));
        var connection = session.ConnectAsync(Configuration);
        probe.Release();
        await connection;

        probe.ResetGate();
        probe.Enqueue(Failure(ConnectionFailureCategory.Network, "late health failure"));
        var health = session.CheckHealthAsync();
        await probe.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        session.Disconnect();
        probe.Release();

        Assert.False(await health);
        Assert.Equal(RecoveryConnectionState.Disconnected, session.Snapshot.State);
        Assert.True(session.CanReconnect);
    }

    [Fact]
    public async Task DuplicateFailureStormIsSuppressed()
    {
        await using var session = new ConnectionRecoverySession(new QueueProbe(Success(1)));
        await session.ConnectAsync(Configuration);
        var failure = new DatabaseFailure(DatabaseFailureKind.NetworkInterruption, true, "lost");

        session.ReportFailure(failure);
        session.ReportFailure(failure);
        session.ReportFailure(failure);

        Assert.Equal(RecoveryConnectionState.Degraded, session.Snapshot.State);
        Assert.Equal(2, session.Snapshot.SuppressedFailureCount);
    }

    [Fact]
    public async Task FaultyStateSubscriberCannotBlockRecoveryOrOtherSubscribers()
    {
        await using var session = new ConnectionRecoverySession(new QueueProbe(Success(2101)));
        var observed = 0;
        session.StateChanged += (_, _) => throw new InvalidOperationException("subscriber failed");
        session.StateChanged += (_, _) => observed++;

        var snapshot = await session.ConnectAsync(Configuration);

        Assert.Equal(RecoveryConnectionState.Connected, snapshot.State);
        Assert.Equal(2, observed);
    }

    [Fact]
    public async Task FaultyDiagnosticsSinkCannotBlockRecovery()
    {
        await using var session = new ConnectionRecoverySession(
            new QueueProbe(Success(2102)),
            new ThrowingDiagnostics());

        var snapshot = await session.ConnectAsync(Configuration);

        Assert.Equal(RecoveryConnectionState.Connected, snapshot.State);
        Assert.Equal(2102, snapshot.BackendProcessId);
    }

    [Fact]
    public async Task HealthCheckDetectsIdleServerFailureAndCancelsGeneration()
    {
        var probe = new QueueProbe(
            Success(1),
            Failure(ConnectionFailureCategory.Network, "server unavailable"));
        await using var session = new ConnectionRecoverySession(probe);
        await session.ConnectAsync(Configuration);
        var generationToken = session.GenerationToken;

        Assert.False(await session.CheckHealthAsync());

        Assert.Equal(RecoveryConnectionState.Degraded, session.Snapshot.State);
        Assert.True(generationToken.IsCancellationRequested);
        Assert.Equal(DatabaseFailureKind.NetworkInterruption, session.Snapshot.Failure?.Kind);
    }

    [Fact]
    public async Task ConcurrentHealthChecksDoNotOverlap()
    {
        var probe = new ControlledProbe();
        probe.Enqueue(Success(1));
        await using var session = new ConnectionRecoverySession(probe);
        var connection = session.ConnectAsync(Configuration);
        probe.Release();
        await connection;

        probe.ResetGate();
        probe.Enqueue(Success(2));
        var first = session.CheckHealthAsync();
        var second = session.CheckHealthAsync();
        Assert.Same(first, second);
        probe.Release();

        Assert.True(await first);
        Assert.Equal(2, probe.CallCount);
    }

    [Theory]
    [InlineData("57P01", "terminating connection due to administrator command", DatabaseFailureKind.AdministratorTermination, true)]
    [InlineData("57P02", null, DatabaseFailureKind.ServerShutdown, true)]
    [InlineData("57P04", null, DatabaseFailureKind.DatabaseUnavailable, false)]
    [InlineData("53300", null, DatabaseFailureKind.TooManyConnections, true)]
    [InlineData("28P01", null, DatabaseFailureKind.AuthenticationFailure, false)]
    [InlineData("08P01", null, DatabaseFailureKind.ProtocolFailure, false)]
    public void SqlStateClassificationIsGranular(
        string sqlState,
        string? message,
        DatabaseFailureKind kind,
        bool transient)
    {
        var failure = DatabaseFailureClassifier.FromSqlState(sqlState, message);
        Assert.Equal(kind, failure.Kind);
        Assert.Equal(transient, failure.IsTransient);
    }

    [Fact]
    public void NestedSocketFailureIsClassifiedWithoutEchoingExceptionDetails()
    {
        var exception = new InvalidOperationException(
            "Password=redaction-sentinel",
            new SocketException((int)SocketError.ConnectionReset));
        var failure = DatabaseFailureClassifier.Classify(exception);
        Assert.Equal(DatabaseFailureKind.NetworkInterruption, failure.Kind);
        Assert.DoesNotContain("redaction-sentinel", failure.Message);
    }

    [Fact]
    public async Task RetryPolicyNeverRetriesUserSqlAndStopsAtBound()
    {
        var generation = Guid.NewGuid();
        var attempts = 0;
        var request = new RecoveryRetryRequest(
            RecoveryOperationKind.UserSql, generation, true, true, false, 2);
        await Assert.ThrowsAsync<IOException>(() => RecoveryRetryPolicy.ExecuteAsync(
            request,
            () => generation,
            (_, _) =>
            {
                attempts++;
                return Task.FromException<int>(new IOException("lost"));
            },
            _ => new(DatabaseFailureKind.NetworkInterruption, true, "lost"),
            new ZeroJitter()));
        Assert.Equal(1, attempts);

        attempts = 0;
        request = request with { Operation = RecoveryOperationKind.MetadataRead, MaximumRetries = 1 };
        await Assert.ThrowsAsync<IOException>(() => RecoveryRetryPolicy.ExecuteAsync(
            request,
            () => generation,
            (_, _) =>
            {
                attempts++;
                return Task.FromException<int>(new IOException("lost"));
            },
            _ => new(DatabaseFailureKind.NetworkInterruption, true, "lost"),
            new ZeroJitter()));
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task RetryPolicyRejectsObsoleteGenerationBeforeWork()
    {
        var expected = Guid.NewGuid();
        var invoked = false;
        await Assert.ThrowsAsync<OperationCanceledException>(() => RecoveryRetryPolicy.ExecuteAsync(
            new(RecoveryOperationKind.HealthCheck, expected, true, true, false),
            Guid.NewGuid,
            (_, _) =>
            {
                invoked = true;
                return Task.FromResult(1);
            },
            _ => new(DatabaseFailureKind.NetworkInterruption, true, "lost"),
            new ZeroJitter()));
        Assert.False(invoked);
    }

    [Fact]
    public async Task RepeatedFailureReconnectCyclesDisposeCleanly()
    {
        var results = Enumerable.Range(1, 8).Select(Success).ToArray();
        var probe = new QueueProbe(results);
        var session = new ConnectionRecoverySession(probe);
        await session.ConnectAsync(Configuration);
        for (var cycle = 1; cycle < results.Length; cycle++)
        {
            session.ReportFailure(new(DatabaseFailureKind.NetworkInterruption, true, "lost"));
            Assert.Equal(RecoveryConnectionState.Connected, (await session.ReconnectAsync()).State);
        }

        await session.DisposeAsync();
        await session.DisposeAsync();
        Assert.Equal(RecoveryConnectionState.Disposed, session.Snapshot.State);
        Assert.Equal(results.Length, probe.CallCount);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => session.ConnectAsync(Configuration));
    }

    private static ConnectionTestResult Success(int pid) =>
        new(true, "recovery-tests", "17", "postgres", "postgres", true, true,
            TimeSpan.FromMilliseconds(1), null, "Connected.", BackendProcessId: pid);

    private static ConnectionTestResult Failure(
        ConnectionFailureCategory category,
        string message,
        string? sqlState = null) =>
        new(false, "recovery-tests", null, null, null, null, null,
            TimeSpan.FromMilliseconds(1), category, message, sqlState);

    private sealed class QueueProbe(params ConnectionTestResult[] results) : IConnectionProbe
    {
        private readonly Queue<ConnectionTestResult> _results = new(results);
        public int CallCount { get; private set; }
        public Task<ConnectionTestResult> TestAsync(
            EffectiveConnectionConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(_results.Dequeue());
        }
    }

    private sealed class ControlledProbe(bool ignoreCancellation = false) : IConnectionProbe
    {
        private TaskCompletionSource _gate = NewGate();
        private readonly Queue<ConnectionTestResult> _results = [];
        public TaskCompletionSource Started { get; private set; } = NewGate();
        public int CallCount { get; private set; }
        public void Enqueue(ConnectionTestResult result) => _results.Enqueue(result);
        public void Release() => _gate.TrySetResult();
        public void ResetGate()
        {
            _gate = NewGate();
            Started = NewGate();
        }

        public async Task<ConnectionTestResult> TestAsync(
            EffectiveConnectionConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            Started.TrySetResult();
            if (ignoreCancellation) await _gate.Task;
            else await _gate.Task.WaitAsync(cancellationToken);
            return _results.Dequeue();
        }

        private static TaskCompletionSource NewGate() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class RecordingDiagnostics : IConnectionRecoveryDiagnostics
    {
        public List<ConnectionRecoveryDiagnostic> Items { get; } = [];
        public void Record(ConnectionRecoveryDiagnostic diagnostic) => Items.Add(diagnostic);
    }

    private sealed class ThrowingDiagnostics : IConnectionRecoveryDiagnostics
    {
        public void Record(ConnectionRecoveryDiagnostic diagnostic) =>
            throw new InvalidOperationException("diagnostics failed");
    }

    private sealed class ZeroJitter : IRetryJitter
    {
        public TimeSpan Next(TimeSpan maximum) => TimeSpan.Zero;
    }
}
