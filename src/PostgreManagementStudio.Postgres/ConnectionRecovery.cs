using System.Diagnostics;
using System.Net.Sockets;
using System.Security.Authentication;
using Npgsql;

namespace PostgreManagementStudio.Postgres;

public enum RecoveryConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Degraded,
    Reconnecting,
    Failed,
    Disposed,
}

public enum DatabaseFailureKind
{
    UserCancellation,
    CommandTimeout,
    ConnectionTimeout,
    DnsFailure,
    NetworkInterruption,
    TlsFailure,
    AuthenticationFailure,
    ServerShutdown,
    AdministratorTermination,
    BackendTermination,
    DatabaseUnavailable,
    TooManyConnections,
    ProtocolFailure,
    DriverFailure,
    UnknownTransient,
    UnknownPermanent,
}

public enum FailureOperationPhase { Connect, Command, Commit, Rollback, BackgroundRead }

public sealed record DatabaseFailure(
    DatabaseFailureKind Kind,
    bool IsTransient,
    string Message,
    string? SqlState = null)
{
    public override string ToString() => $"{Kind}: {Message}";
}

public static class DatabaseFailureClassifier
{
    public static DatabaseFailure Classify(Exception exception, FailureOperationPhase phase = FailureOperationPhase.Command)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var postgres = Find<PostgresException>(exception);
        if (exception is OperationCanceledException)
            return Failure(DatabaseFailureKind.UserCancellation, false, "The operation was cancelled by the user.");
        if (postgres is not null) return FromPostgres(postgres);
        if (Find<AuthenticationException>(exception) is not null)
            return Failure(DatabaseFailureKind.TlsFailure, false, "TLS or certificate validation failed.");
        if (Find<SocketException>(exception) is { } socket)
            return socket.SocketErrorCode is SocketError.HostNotFound or SocketError.NoData
                ? Failure(DatabaseFailureKind.DnsFailure, true, "The PostgreSQL host could not be resolved.")
                : Failure(DatabaseFailureKind.NetworkInterruption, true, "The network connection to PostgreSQL was interrupted.");
        if (Find<TimeoutException>(exception) is not null)
            return phase == FailureOperationPhase.Connect
                ? Failure(DatabaseFailureKind.ConnectionTimeout, true, "The PostgreSQL connection attempt timed out.")
                : Failure(DatabaseFailureKind.CommandTimeout, false, "The PostgreSQL command timed out.");
        if (Find<NpgsqlException>(exception) is { } npgsql)
        {
            var safe = Safe(npgsql.Message);
            if (Contains(safe, "certificate", "ssl", "tls"))
                return Failure(DatabaseFailureKind.TlsFailure, false, "TLS or certificate validation failed.");
            if (Contains(safe, "password", "authentication"))
                return Failure(DatabaseFailureKind.AuthenticationFailure, false, "PostgreSQL rejected the supplied credentials.");
            if (Contains(safe, "timeout", "timed out"))
                return phase == FailureOperationPhase.Connect
                    ? Failure(DatabaseFailureKind.ConnectionTimeout, true, "The PostgreSQL connection attempt timed out.")
                    : Failure(DatabaseFailureKind.CommandTimeout, false, "The PostgreSQL command timed out.");
            if (Contains(safe, "protocol"))
                return Failure(DatabaseFailureKind.ProtocolFailure, false, "The PostgreSQL protocol stream became invalid.");
            if (Contains(safe, "connection", "socket", "stream", "network", "broken pipe", "end of stream"))
                return Failure(DatabaseFailureKind.NetworkInterruption, true, "The network connection to PostgreSQL was interrupted.");
            return Failure(DatabaseFailureKind.DriverFailure, true, "The PostgreSQL driver reported a connection failure.");
        }
        return Failure(DatabaseFailureKind.UnknownPermanent, false, "An unexpected database failure occurred.");
    }

    public static DatabaseFailure FromSqlState(string? sqlState, string? message = null)
    {
        if (string.IsNullOrWhiteSpace(sqlState))
            return Failure(DatabaseFailureKind.UnknownTransient, true, "The PostgreSQL connection was lost.");
        var safe = Safe(message);
        return sqlState switch
        {
            "57014" => Failure(DatabaseFailureKind.UserCancellation, false, "The PostgreSQL command was cancelled.", sqlState),
            "57P01" when Contains(safe, "administrator") =>
                Failure(DatabaseFailureKind.AdministratorTermination, true, "PostgreSQL terminated the backend at an administrator's request.", sqlState),
            "57P01" or "57P02" or "57P03" =>
                Failure(DatabaseFailureKind.ServerShutdown, true, "PostgreSQL is shutting down or restarting.", sqlState),
            "57P04" => Failure(DatabaseFailureKind.DatabaseUnavailable, false, "The selected database was dropped or is unavailable.", sqlState),
            "57P05" => Failure(DatabaseFailureKind.BackendTermination, true, "PostgreSQL terminated the idle backend.", sqlState),
            "53300" => Failure(DatabaseFailureKind.TooManyConnections, true, "PostgreSQL has no available connection slots.", sqlState),
            "3D000" => Failure(DatabaseFailureKind.DatabaseUnavailable, false, "The selected database is unavailable.", sqlState),
            "28P01" or "28000" => Failure(DatabaseFailureKind.AuthenticationFailure, false, "PostgreSQL rejected the supplied credentials.", sqlState),
            "08P01" => Failure(DatabaseFailureKind.ProtocolFailure, false, "The PostgreSQL protocol stream became invalid.", sqlState),
            _ when sqlState.StartsWith("08", StringComparison.Ordinal) =>
                Failure(DatabaseFailureKind.NetworkInterruption, true, "The connection to PostgreSQL failed.", sqlState),
            _ => Failure(DatabaseFailureKind.UnknownPermanent, false, "PostgreSQL reported an unrecoverable database error.", sqlState),
        };
    }

    private static DatabaseFailure FromPostgres(PostgresException exception) =>
        FromSqlState(exception.SqlState, exception.MessageText);

    private static DatabaseFailure Failure(DatabaseFailureKind kind, bool transient, string message, string? sqlState = null) =>
        new(kind, transient, message, sqlState);

    private static T? Find<T>(Exception exception) where T : Exception
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is T typed) return typed;
        return null;
    }

    private static bool Contains(string? value, params string[] fragments) =>
        value is not null && fragments.Any(fragment => value.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    private static string Safe(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var builder = new NpgsqlConnectionStringBuilder();
        try
        {
            builder.ConnectionString = value;
            if (!string.IsNullOrEmpty(builder.Password)) return "Sensitive connection details were redacted.";
        }
        catch { }
        var redacted = System.Text.RegularExpressions.Regex.Replace(value,
            @"(?i)(password|pwd|token|secret|sslpassword)\s*=\s*[^;\s]+", "$1=<redacted>");
        return redacted.Length <= 512 ? redacted : redacted[..512] + "…";
    }
}

public sealed record ConnectionRecoverySnapshot(
    Guid LogicalSessionId,
    RecoveryConnectionState State,
    RecoveryConnectionState PreviousState,
    Guid GenerationId,
    DateTimeOffset? LastSuccessfulConnection,
    DateTimeOffset? LastFailure,
    DatabaseFailure? Failure,
    int ReconnectionAttemptCount,
    int? BackendProcessId,
    int SuppressedFailureCount,
    string StaleStateWarning);

public interface IConnectionRecoveryDiagnostics
{
    void Record(ConnectionRecoveryDiagnostic diagnostic);
}

public sealed record ConnectionRecoveryDiagnostic(
    Guid LogicalSessionId,
    Guid GenerationId,
    RecoveryConnectionState PreviousState,
    RecoveryConnectionState State,
    string Operation,
    DatabaseFailureKind? FailureKind,
    string? SqlState,
    int? PreviousBackendProcessId,
    int? BackendProcessId,
    int Attempt,
    TimeSpan Duration,
    int CancelledDependents);

public sealed class DiagnosticConnectionRecoveryDiagnostics : IConnectionRecoveryDiagnostics
{
    public void Record(ConnectionRecoveryDiagnostic value) => Trace.WriteLine(
        $"connection_recovery session_id={value.LogicalSessionId:N} generation_id={value.GenerationId:N} " +
        $"transition={value.PreviousState}->{value.State} operation={Safe(value.Operation)} " +
        $"failure={value.FailureKind?.ToString() ?? "none"} sqlstate={value.SqlState ?? "none"} " +
        $"backend_pid={value.PreviousBackendProcessId?.ToString() ?? "none"}->{value.BackendProcessId?.ToString() ?? "none"} " +
        $"attempt={value.Attempt} elapsed_ms={value.Duration.TotalMilliseconds:F0} cancelled_dependents={value.CancelledDependents}");

    private static string Safe(string value) => value.Replace('\r', '_').Replace('\n', '_').Replace(' ', '_');
}

public sealed class NullConnectionRecoveryDiagnostics : IConnectionRecoveryDiagnostics
{
    public static NullConnectionRecoveryDiagnostics Instance { get; } = new();
    private NullConnectionRecoveryDiagnostics() { }
    public void Record(ConnectionRecoveryDiagnostic diagnostic) { }
}

public sealed class ConnectionRecoverySession : IAsyncDisposable
{
    private const string StaleWarning =
        "The PostgreSQL backend session changed. Temporary objects, session settings, prepared statements, advisory locks, LISTEN registrations, cursors, transaction state, SET ROLE, search path, and session variables must be re-established.";
    private readonly object _gate = new();
    private readonly IConnectionProbe _probe;
    private readonly IConnectionRecoveryDiagnostics _diagnostics;
    private CancellationTokenSource _generation = new();
    private CancellationTokenSource? _attemptCancellation;
    private Task<ConnectionRecoverySnapshot>? _attempt;
    private Task<bool>? _healthCheck;
    private Guid _attemptId;
    private EffectiveConnectionConfiguration? _configuration;
    private RecoveryConnectionState _state;
    private RecoveryConnectionState _previousState;
    private Guid _generationId;
    private DateTimeOffset? _lastSuccess;
    private DateTimeOffset? _lastFailure;
    private DatabaseFailure? _failure;
    private int _reconnectAttempts;
    private int? _backendProcessId;
    private int _suppressedFailures;
    private int _disposed;

    public ConnectionRecoverySession(IConnectionProbe probe, IConnectionRecoveryDiagnostics? diagnostics = null)
    {
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _diagnostics = diagnostics ?? NullConnectionRecoveryDiagnostics.Instance;
    }

    public event EventHandler? StateChanged;
    public Guid LogicalSessionId { get; } = Guid.NewGuid();
    public ConnectionRecoverySnapshot Snapshot { get { lock (_gate) return SnapshotUnsafe(); } }
    public CancellationToken GenerationToken { get { lock (_gate) return _generation.Token; } }
    public EffectiveConnectionConfiguration? Configuration { get { lock (_gate) return _configuration; } }
    public bool CanReconnect
    {
        get
        {
            lock (_gate)
                return _configuration is not null
                    && _state is RecoveryConnectionState.Degraded
                        or RecoveryConnectionState.Failed
                        or RecoveryConnectionState.Disconnected;
        }
    }

    public Task<ConnectionRecoverySnapshot> ConnectAsync(
        EffectiveConnectionConfiguration configuration,
        CancellationToken cancellationToken = default) =>
        StartAsync(configuration, reconnect: false, cancellationToken);

    public Task<ConnectionRecoverySnapshot> ReconnectAsync(CancellationToken cancellationToken = default)
    {
        EffectiveConnectionConfiguration configuration;
        lock (_gate) configuration = _configuration ??
            throw new InvalidOperationException("No saved connection configuration is available for reconnect.");
        return StartAsync(configuration, reconnect: true, cancellationToken);
    }

    public void ReportFailure(DatabaseFailure failure) => ReportFailure(failure, Guid.Empty);

    public bool ReportFailure(DatabaseFailure failure, Guid expectedGenerationId)
    {
        ArgumentNullException.ThrowIfNull(failure);
        ConnectionRecoveryDiagnostic? diagnostic = null;
        lock (_gate)
        {
            if (_state == RecoveryConnectionState.Disposed) return false;
            if (expectedGenerationId != Guid.Empty && expectedGenerationId != _generationId) return false;
            if (_state is RecoveryConnectionState.Degraded or RecoveryConnectionState.Failed)
            {
                if (_failure?.Kind == failure.Kind) _suppressedFailures++;
                return false;
            }
            if (_state != RecoveryConnectionState.Connected) return false;
            var prior = _state;
            var priorPid = _backendProcessId;
            TransitionUnsafe(RecoveryConnectionState.Degraded);
            _lastFailure = DateTimeOffset.UtcNow;
            _failure = failure with { Message = SafeMessage(failure.Message) };
            _backendProcessId = null;
            var cancelled = EndGenerationUnsafe();
            diagnostic = new(LogicalSessionId, _generationId, prior, _state, "Failure",
                failure.Kind, failure.SqlState, priorPid, null, _reconnectAttempts, TimeSpan.Zero, cancelled);
        }
        RecordDiagnostic(diagnostic);
        RaiseStateChanged();
        return true;
    }

    public void ReportFailure(Exception exception, FailureOperationPhase phase = FailureOperationPhase.Command) =>
        ReportFailure(DatabaseFailureClassifier.Classify(exception, phase));

    public Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_state != RecoveryConnectionState.Connected || _configuration is null)
                return Task.FromResult(false);
            if (_healthCheck is { IsCompleted: false }) return _healthCheck;
            _healthCheck = RunHealthCheckAsync(
                _configuration,
                _generationId,
                cancellationToken);
            return _healthCheck;
        }
    }

    public void Disconnect()
    {
        ConnectionRecoveryDiagnostic? diagnostic;
        lock (_gate)
        {
            if (_state is RecoveryConnectionState.Disconnected or RecoveryConnectionState.Disposed) return;
            var prior = _state;
            var priorPid = _backendProcessId;
            _attemptId = Guid.NewGuid();
            _attemptCancellation?.Cancel();
            TransitionUnsafe(RecoveryConnectionState.Disconnected);
            _backendProcessId = null;
            var cancelled = EndGenerationUnsafe();
            diagnostic = new(LogicalSessionId, _generationId, prior, _state, "Disconnect", null, null,
                priorPid, null, _reconnectAttempts, TimeSpan.Zero, cancelled);
        }
        RecordDiagnostic(diagnostic);
        RaiseStateChanged();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        ConnectionRecoveryDiagnostic diagnostic;
        lock (_gate)
        {
            var prior = _state;
            var priorPid = _backendProcessId;
            _attemptId = Guid.NewGuid();
            _attemptCancellation?.Cancel();
            TransitionUnsafe(RecoveryConnectionState.Disposed);
            _backendProcessId = null;
            var cancelled = EndGenerationUnsafe();
            _configuration = null;
            _attempt = null;
            _healthCheck = null;
            diagnostic = new(LogicalSessionId, _generationId, prior, _state, "Dispose", null, null,
                priorPid, null, _reconnectAttempts, TimeSpan.Zero, cancelled);
        }
        RecordDiagnostic(diagnostic);
        _attemptCancellation?.Dispose();
        _generation.Dispose();
        await Task.CompletedTask;
        RaiseStateChanged();
    }

    public static bool IsValidTransition(RecoveryConnectionState from, RecoveryConnectionState to) => (from, to) switch
    {
        (RecoveryConnectionState.Disconnected, RecoveryConnectionState.Connecting or RecoveryConnectionState.Reconnecting or RecoveryConnectionState.Disposed) => true,
        (RecoveryConnectionState.Connecting, RecoveryConnectionState.Connected or RecoveryConnectionState.Failed or RecoveryConnectionState.Disconnected or RecoveryConnectionState.Disposed) => true,
        (RecoveryConnectionState.Connected, RecoveryConnectionState.Degraded or RecoveryConnectionState.Disconnected or RecoveryConnectionState.Reconnecting or RecoveryConnectionState.Disposed) => true,
        (RecoveryConnectionState.Degraded, RecoveryConnectionState.Reconnecting or RecoveryConnectionState.Failed or RecoveryConnectionState.Disconnected or RecoveryConnectionState.Disposed) => true,
        (RecoveryConnectionState.Reconnecting, RecoveryConnectionState.Connected or RecoveryConnectionState.Degraded or RecoveryConnectionState.Failed or RecoveryConnectionState.Disconnected or RecoveryConnectionState.Disposed) => true,
        (RecoveryConnectionState.Failed, RecoveryConnectionState.Reconnecting or RecoveryConnectionState.Connecting or RecoveryConnectionState.Disconnected or RecoveryConnectionState.Disposed) => true,
        _ => false,
    };

    private Task<ConnectionRecoverySnapshot> StartAsync(
        EffectiveConnectionConfiguration configuration,
        bool reconnect,
        CancellationToken cancellationToken)
    {
        Task<ConnectionRecoverySnapshot> attempt;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (_attempt is { IsCompleted: false }) return _attempt;
            var target = reconnect ? RecoveryConnectionState.Reconnecting : RecoveryConnectionState.Connecting;
            if (!IsValidTransition(_state, target))
            {
                if (_state == RecoveryConnectionState.Connected && !reconnect) return Task.FromResult(SnapshotUnsafe());
                throw new InvalidOperationException($"Invalid connection recovery transition: {_state} -> {target}.");
            }
            _configuration = configuration;
            if (reconnect) _reconnectAttempts++;
            TransitionUnsafe(target);
            _attemptCancellation?.Dispose();
            _attemptCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _attemptId = Guid.NewGuid();
            _attempt = RunAttemptAsync(_attemptId, configuration, reconnect, _attemptCancellation.Token);
            attempt = _attempt;
        }
        RaiseStateChanged();
        return attempt;
    }

    private async Task<ConnectionRecoverySnapshot> RunAttemptAsync(
        Guid attemptId,
        EffectiveConnectionConfiguration configuration,
        bool reconnect,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        ConnectionTestResult result;
        try
        {
            result = await _probe.TestAsync(configuration, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            result = new(false, configuration.Profile.Id, null, null, null, null, null,
                Stopwatch.GetElapsedTime(started), ConnectionFailureCategory.Cancelled,
                "The connection attempt was cancelled.");
        }
        catch (Exception ex)
        {
            var failure = DatabaseFailureClassifier.Classify(ex, FailureOperationPhase.Connect);
            result = new(false, configuration.Profile.Id, null, null, null, null, null,
                Stopwatch.GetElapsedTime(started), ToConnectionFailureCategory(failure.Kind),
                failure.Message, failure.SqlState);
        }
        ConnectionRecoveryDiagnostic diagnostic;
        ConnectionRecoverySnapshot snapshot;
        lock (_gate)
        {
            if (_attemptId != attemptId || _state is RecoveryConnectionState.Disconnected or RecoveryConnectionState.Disposed)
                return SnapshotUnsafe();
            var prior = _state;
            var priorPid = _backendProcessId;
            if (result.Succeeded)
            {
                var cancelled = EndGenerationUnsafe();
                _generation.Dispose();
                _generation = new CancellationTokenSource();
                _generationId = Guid.NewGuid();
                _lastSuccess = DateTimeOffset.UtcNow;
                _failure = null;
                _suppressedFailures = 0;
                _backendProcessId = result.BackendProcessId;
                TransitionUnsafe(RecoveryConnectionState.Connected);
                diagnostic = new(LogicalSessionId, _generationId, prior, _state,
                    reconnect ? "Reconnect" : "Connect", null, null, priorPid, _backendProcessId,
                    _reconnectAttempts, Stopwatch.GetElapsedTime(started), cancelled);
            }
            else
            {
                _lastFailure = DateTimeOffset.UtcNow;
                _failure = FromConnectionResult(result);
                var cancelled = EndGenerationUnsafe();
                var cancelledAttempt = result.FailureCategory == ConnectionFailureCategory.Cancelled;
                TransitionUnsafe(cancelledAttempt
                    ? (reconnect ? RecoveryConnectionState.Degraded : RecoveryConnectionState.Disconnected)
                    : RecoveryConnectionState.Failed);
                diagnostic = new(LogicalSessionId, _generationId, prior, _state,
                    reconnect ? "Reconnect" : "Connect", _failure.Kind, result.SqlState, priorPid, null,
                    _reconnectAttempts, Stopwatch.GetElapsedTime(started), cancelled);
            }
            snapshot = SnapshotUnsafe();
        }
        RecordDiagnostic(diagnostic);
        RaiseStateChanged();
        return snapshot;
    }

    private async Task<bool> RunHealthCheckAsync(
        EffectiveConnectionConfiguration configuration,
        Guid generationId,
        CancellationToken cancellationToken)
    {
        ConnectionTestResult result;
        try
        {
            result = await _probe.TestAsync(configuration, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception ex)
        {
            var failure = DatabaseFailureClassifier.Classify(ex, FailureOperationPhase.Connect);
            ReportFailure(failure, generationId);
            return false;
        }
        if (result.Succeeded) return true;
        if (result.FailureCategory == ConnectionFailureCategory.Cancelled) return false;
        ReportFailure(FromConnectionResult(result), generationId);
        return false;
    }

    private void RecordDiagnostic(ConnectionRecoveryDiagnostic diagnostic)
    {
        try
        {
            _diagnostics.Record(diagnostic);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"connection_recovery diagnostics_failure={ex.GetType().Name}");
        }
    }

    private void RaiseStateChanged()
    {
        var subscribers = StateChanged;
        if (subscribers is null) return;
        foreach (EventHandler subscriber in subscribers.GetInvocationList())
        {
            try
            {
                subscriber(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"connection_recovery subscriber_failure={ex.GetType().Name}");
            }
        }
    }

    private void TransitionUnsafe(RecoveryConnectionState next)
    {
        if (_state == next) return;
        if (!IsValidTransition(_state, next))
            throw new InvalidOperationException($"Invalid connection recovery transition: {_state} -> {next}.");
        _previousState = _state;
        _state = next;
    }

    private int EndGenerationUnsafe()
    {
        if (_generation.IsCancellationRequested) return 0;
        _generation.Cancel();
        return 1;
    }

    private ConnectionRecoverySnapshot SnapshotUnsafe() => new(
        LogicalSessionId, _state, _previousState, _generationId, _lastSuccess, _lastFailure,
        _failure, _reconnectAttempts, _backendProcessId, _suppressedFailures,
        _generationId != Guid.Empty && _reconnectAttempts > 0 ? StaleWarning : string.Empty);

    private static DatabaseFailure FromConnectionResult(ConnectionTestResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.SqlState))
            return DatabaseFailureClassifier.FromSqlState(result.SqlState, result.Message);
        var kind = result.FailureCategory switch
        {
            ConnectionFailureCategory.Cancelled => DatabaseFailureKind.UserCancellation,
            ConnectionFailureCategory.Timeout => DatabaseFailureKind.ConnectionTimeout,
            ConnectionFailureCategory.Dns => DatabaseFailureKind.DnsFailure,
            ConnectionFailureCategory.Network => DatabaseFailureKind.NetworkInterruption,
            ConnectionFailureCategory.Ssl => DatabaseFailureKind.TlsFailure,
            ConnectionFailureCategory.Authentication => DatabaseFailureKind.AuthenticationFailure,
            ConnectionFailureCategory.ServerUnavailable => DatabaseFailureKind.ServerShutdown,
            ConnectionFailureCategory.DatabaseUnavailable => DatabaseFailureKind.DatabaseUnavailable,
            ConnectionFailureCategory.PoolExhausted => DatabaseFailureKind.TooManyConnections,
            _ => DatabaseFailureKind.UnknownPermanent,
        };
        var transient = kind is DatabaseFailureKind.ConnectionTimeout or DatabaseFailureKind.DnsFailure
            or DatabaseFailureKind.NetworkInterruption or DatabaseFailureKind.ServerShutdown
            or DatabaseFailureKind.TooManyConnections;
        return new(kind, transient, SafeMessage(result.Message), result.SqlState);
    }

    private static ConnectionFailureCategory ToConnectionFailureCategory(DatabaseFailureKind kind) => kind switch
    {
        DatabaseFailureKind.UserCancellation => ConnectionFailureCategory.Cancelled,
        DatabaseFailureKind.ConnectionTimeout => ConnectionFailureCategory.Timeout,
        DatabaseFailureKind.DnsFailure => ConnectionFailureCategory.Dns,
        DatabaseFailureKind.NetworkInterruption => ConnectionFailureCategory.Network,
        DatabaseFailureKind.TlsFailure => ConnectionFailureCategory.Ssl,
        DatabaseFailureKind.AuthenticationFailure => ConnectionFailureCategory.Authentication,
        DatabaseFailureKind.ServerShutdown or DatabaseFailureKind.AdministratorTermination
            or DatabaseFailureKind.BackendTermination => ConnectionFailureCategory.ServerUnavailable,
        DatabaseFailureKind.DatabaseUnavailable => ConnectionFailureCategory.DatabaseUnavailable,
        DatabaseFailureKind.TooManyConnections => ConnectionFailureCategory.PoolExhausted,
        _ => ConnectionFailureCategory.Unknown,
    };

    private static string SafeMessage(string message)
    {
        var redacted = System.Text.RegularExpressions.Regex.Replace(message,
            @"(?i)(password|pwd|token|secret|sslpassword)\s*=\s*[^;\s]+", "$1=<redacted>");
        return redacted.Length <= 512 ? redacted : redacted[..512] + "…";
    }
}

public enum RecoveryOperationKind { HealthCheck, MetadataRead, StatusPoll, NotificationListener, UserSql }

public sealed record RecoveryRetryRequest(
    RecoveryOperationKind Operation,
    Guid GenerationId,
    bool IsReadOnly,
    bool IsIdempotent,
    bool TransactionActive,
    int MaximumRetries = 2);

public interface IRetryJitter
{
    TimeSpan Next(TimeSpan maximum);
}

public sealed class RandomRetryJitter : IRetryJitter
{
    public TimeSpan Next(TimeSpan maximum) =>
        TimeSpan.FromMilliseconds(Random.Shared.NextDouble() * Math.Max(0, maximum.TotalMilliseconds));
}

public static class RecoveryRetryPolicy
{
    public static bool IsEligible(RecoveryRetryRequest request, DatabaseFailure failure) =>
        request.Operation != RecoveryOperationKind.UserSql
        && request.IsReadOnly
        && request.IsIdempotent
        && !request.TransactionActive
        && request.MaximumRetries is > 0 and <= 5
        && failure.IsTransient;

    public static async Task<T> ExecuteAsync<T>(
        RecoveryRetryRequest request,
        Func<Guid> currentGeneration,
        Func<int, CancellationToken, Task<T>> operation,
        Func<Exception, DatabaseFailure> classify,
        IRetryJitter? jitter = null,
        CancellationToken cancellationToken = default)
    {
        jitter ??= new RandomRetryJitter();
        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (currentGeneration() != request.GenerationId)
                throw new OperationCanceledException("The connection generation changed.", cancellationToken);
            try { return await operation(attempt, cancellationToken).ConfigureAwait(false); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var failure = classify(ex);
                if (!IsEligible(request, failure) || attempt >= request.MaximumRetries) throw;
                var exponential = TimeSpan.FromMilliseconds(100 * Math.Pow(2, attempt));
                var delay = exponential + jitter.Next(TimeSpan.FromMilliseconds(50));
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
