using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text.Json.Serialization;
using Npgsql;

namespace PostgreManagementStudio.Postgres;

public enum ConnectionAuthenticationMode { Password, Integrated, ClientCertificate }
public enum EnvironmentClassification { Local, Development, Test, Staging, Production, Custom }
public enum ConnectionFailureCategory { Validation, Dns, Network, Timeout, Authentication, Authorisation, Ssl, ServerUnavailable, DatabaseUnavailable, PoolExhausted, Cancelled, Disposed, Unknown }
public enum ManagedConnectionState { Disconnected, ResolvingProfile, Connecting, Connected, Disconnecting, Reconnecting, Failed, Disposed }

public sealed record ConnectionProfile
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Host { get; init; }
    public int Port { get; init; } = 5432;
    public required string Database { get; init; }
    public required string Username { get; init; }
    [JsonIgnore, DebuggerBrowsable(DebuggerBrowsableState.Never)]
    public string? Password { get; init; }
    public string? CredentialReference { get; init; }
    public ConnectionAuthenticationMode AuthenticationMode { get; init; } = ConnectionAuthenticationMode.Password;
    public EnvironmentClassification Environment { get; init; } = EnvironmentClassification.Local;
    public string? CustomEnvironmentName { get; init; }
    public bool IsReadOnly { get; init; }
    public bool EnforceReadOnlyForProduction { get; init; } = true;
    public SslMode SslMode { get; init; } = SslMode.Prefer;
    public string? RootCertificate { get; init; }
    public string? ClientCertificate { get; init; }
    [JsonIgnore, DebuggerBrowsable(DebuggerBrowsableState.Never)]
    public string? ClientKey { get; init; }
    [JsonIgnore, DebuggerBrowsable(DebuggerBrowsableState.Never)]
    public string? ClientCertificatePassword { get; init; }
    public int ConnectionTimeoutSeconds { get; init; } = 15;
    public int CommandTimeoutSeconds { get; init; } = 30;
    public int KeepAliveSeconds { get; init; }
    public bool Pooling { get; init; } = true;
    public int MinimumPoolSize { get; init; }
    public int MaximumPoolSize { get; init; } = 20;
    public int ConnectionIdleLifetimeSeconds { get; init; } = 300;
    public string ApplicationName { get; init; } = "PostgreManagementStudio";
    public string? SearchPath { get; init; }
    public IReadOnlyDictionary<string, string> AdvancedOptions { get; init; } = new Dictionary<string, string>();

    public bool EffectiveReadOnly => IsReadOnly || (Environment == EnvironmentClassification.Production && EnforceReadOnlyForProduction);
    public string EnvironmentDisplayName => Environment == EnvironmentClassification.Custom
        ? CustomEnvironmentName ?? "Custom"
        : Environment.ToString();
    public override string ToString() => $"{Name} ({Username}@{Host}:{Port}/{Database}, {EnvironmentDisplayName}{(EffectiveReadOnly ? ", read-only" : "")}, SSL={SslMode})";
}

public sealed record ConnectionValidationError(string Field, string Message);
public sealed record ConnectionValidationResult(IReadOnlyList<ConnectionValidationError> Errors)
{
    public bool IsValid => Errors.Count == 0;
    public void ThrowIfInvalid()
    {
        if (!IsValid) throw new ConnectionProfileValidationException(Errors);
    }
}

public sealed class ConnectionProfileValidationException(IReadOnlyList<ConnectionValidationError> errors)
    : ArgumentException(string.Join(Environment.NewLine, errors.Select(x => $"{x.Field}: {x.Message}")))
{
    public IReadOnlyList<ConnectionValidationError> Errors { get; } = errors;
}

public static class ConnectionProfileValidator
{
    private static readonly HashSet<string> SupportedAdvancedOptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "Tcp Keepalive", "Tcp Keepalive Time", "Tcp Keepalive Interval",
        "Cancellation Timeout", "Max Auto Prepare", "Auto Prepare Min Usages",
        "Load Balance Hosts", "Target Session Attributes",
    };

    public static ConnectionValidationResult Validate(ConnectionProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var errors = new List<ConnectionValidationError>();
        Required(errors, nameof(profile.Id), profile.Id, 128);
        Required(errors, nameof(profile.Name), profile.Name, 128);
        Required(errors, nameof(profile.Host), profile.Host, 1024);
        Required(errors, nameof(profile.Database), profile.Database, 255);
        Required(errors, nameof(profile.Username), profile.Username, 255);
        if (!string.IsNullOrWhiteSpace(profile.CredentialReference)
            && (profile.CredentialReference.Length > 256 || profile.CredentialReference.Any(char.IsControl)))
            errors.Add(new(nameof(profile.CredentialReference), "Credential reference is invalid."));
        if (profile.Environment == EnvironmentClassification.Custom
            && (string.IsNullOrWhiteSpace(profile.CustomEnvironmentName) || profile.CustomEnvironmentName.Length > 64 || ContainsControl(profile.CustomEnvironmentName)))
            errors.Add(new(nameof(profile.CustomEnvironmentName), "A custom environment label of at most 64 characters is required."));
        if (profile.Port is < 1 or > 65535) errors.Add(new(nameof(profile.Port), "Port must be between 1 and 65535."));
        if (profile.ConnectionTimeoutSeconds is < 1 or > 120) errors.Add(new(nameof(profile.ConnectionTimeoutSeconds), "Connection timeout must be between 1 and 120 seconds."));
        if (profile.CommandTimeoutSeconds is < 1 or > 86_400) errors.Add(new(nameof(profile.CommandTimeoutSeconds), "Command timeout must be between 1 and 86400 seconds."));
        if (profile.KeepAliveSeconds is < 0 or > 300) errors.Add(new(nameof(profile.KeepAliveSeconds), "Keepalive must be between 0 and 300 seconds."));
        if (profile.MinimumPoolSize < 0) errors.Add(new(nameof(profile.MinimumPoolSize), "Minimum pool size cannot be negative."));
        if (profile.MaximumPoolSize is < 1 or > ConnectionResourceLimits.MaximumConnectionsPerProfile)
            errors.Add(new(nameof(profile.MaximumPoolSize), $"Maximum pool size must be between 1 and {ConnectionResourceLimits.MaximumConnectionsPerProfile}."));
        if (profile.MinimumPoolSize > profile.MaximumPoolSize) errors.Add(new(nameof(profile.MinimumPoolSize), "Minimum pool size cannot exceed maximum pool size."));
        if (profile.ConnectionIdleLifetimeSeconds is < 1 or > 3600) errors.Add(new(nameof(profile.ConnectionIdleLifetimeSeconds), "Connection idle lifetime must be between 1 and 3600 seconds."));
        if (profile.AuthenticationMode == ConnectionAuthenticationMode.Password && string.IsNullOrEmpty(profile.Password))
            errors.Add(new(nameof(profile.Password), "A password is required for password authentication."));
        if (profile.AuthenticationMode == ConnectionAuthenticationMode.ClientCertificate
            && (string.IsNullOrWhiteSpace(profile.ClientCertificate) || string.IsNullOrWhiteSpace(profile.ClientKey)))
            errors.Add(new(nameof(profile.ClientCertificate), "Client certificate authentication requires both certificate and private-key paths."));
        if (profile.SslMode == SslMode.Disable
            && (!string.IsNullOrWhiteSpace(profile.RootCertificate) || !string.IsNullOrWhiteSpace(profile.ClientCertificate) || !string.IsNullOrWhiteSpace(profile.ClientKey)))
            errors.Add(new(nameof(profile.SslMode), "Certificate options cannot be used when SSL is disabled."));
        Path(errors, nameof(profile.RootCertificate), profile.RootCertificate);
        Path(errors, nameof(profile.ClientCertificate), profile.ClientCertificate);
        Path(errors, nameof(profile.ClientKey), profile.ClientKey);
        if (ContainsControl(profile.SearchPath)) errors.Add(new(nameof(profile.SearchPath), "Search path cannot contain control characters."));
        foreach (var option in profile.AdvancedOptions)
        {
            if (!SupportedAdvancedOptions.Contains(option.Key)) errors.Add(new(nameof(profile.AdvancedOptions), $"Provider option '{Safe(option.Key)}' is unsupported."));
            if (ContainsControl(option.Key) || ContainsControl(option.Value)) errors.Add(new(nameof(profile.AdvancedOptions), "Advanced options cannot contain control characters."));
        }
        return new(errors);
    }

    private static void Required(ICollection<ConnectionValidationError> errors, string field, string? value, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value)) errors.Add(new(field, "A value is required."));
        else if (value.Length > maximum) errors.Add(new(field, $"Value cannot exceed {maximum} characters."));
        else if (ContainsControl(value)) errors.Add(new(field, "Control characters are not allowed."));
    }

    private static void Path(ICollection<ConnectionValidationError> errors, string field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        if (value.Length > 1024 || ContainsControl(value)) errors.Add(new(field, "Certificate path is invalid."));
        else if (!File.Exists(value)) errors.Add(new(field, "Certificate file does not exist or is not accessible."));
    }

    private static bool ContainsControl(string? value) => value?.Any(char.IsControl) == true;
    private static string Safe(string value) => value.Length <= 80 ? value : value[..80] + "…";
}

public sealed class EffectiveConnectionConfiguration
{
    internal EffectiveConnectionConfiguration(ConnectionProfile profile, string providerConnectionString)
    {
        Profile = profile;
        ProviderConnectionString = providerConnectionString;
        var identityBuilder = new NpgsqlConnectionStringBuilder(providerConnectionString)
        {
            Password = null,
            SslPassword = null,
        };
        Identity = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(identityBuilder.ConnectionString)));
    }

    public ConnectionProfile Profile { get; }
    public string Identity { get; }
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    internal string ProviderConnectionString { get; }
    public override string ToString() => Profile.ToString();
}

public static class EffectiveConnectionConfigurationBuilder
{
    public static EffectiveConnectionConfiguration FromConnectionString(string profileId, string connectionString, string applicationName, string? databaseOverride = null)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) throw new ConnectionProfileValidationException([new("ConnectionString", "A connection string is required.")]);
        NpgsqlConnectionStringBuilder builder;
        System.Data.Common.DbConnectionStringBuilder raw;
        try
        {
            raw = new() { ConnectionString = connectionString };
            builder = new(connectionString);
        }
        catch (Exception ex) { throw new ConnectionProfileValidationException([new("ConnectionString", $"Provider settings are malformed ({ex.GetType().Name}).")]); }
        if (Has(raw, "No Reset On Close", "NoResetOnClose") && builder.NoResetOnClose)
            throw new ConnectionProfileValidationException([new("No Reset On Close", "Disabling provider session reset is not supported.")]);
        if (Has(raw, "Include Error Detail", "IncludeErrorDetail") && builder.IncludeErrorDetail)
            throw new ConnectionProfileValidationException([new("Include Error Detail", "Unrestricted provider error detail is disabled to protect sensitive values.")]);

        var profile = new ConnectionProfile
        {
            Id = profileId.Trim(),
            Name = profileId.Trim(),
            Host = builder.Host?.Trim() ?? "",
            Port = builder.Port,
            Database = (databaseOverride ?? builder.Database)?.Trim() ?? "",
            Username = builder.Username?.Trim() ?? "",
            Password = builder.Password,
            AuthenticationMode = string.IsNullOrEmpty(builder.Password) ? ConnectionAuthenticationMode.Integrated : ConnectionAuthenticationMode.Password,
            SslMode = builder.SslMode,
            RootCertificate = Empty(builder.RootCertificate),
            ClientCertificate = Empty(builder.SslCertificate),
            ClientKey = Empty(builder.SslKey),
            ConnectionTimeoutSeconds = builder.Timeout,
            CommandTimeoutSeconds = builder.CommandTimeout == 0 ? 30 : builder.CommandTimeout,
            KeepAliveSeconds = builder.KeepAlive,
            Pooling = builder.Pooling,
            MinimumPoolSize = builder.MinPoolSize,
            MaximumPoolSize = Has(raw, "Maximum Pool Size", "MaxPoolSize") ? builder.MaxPoolSize : 20,
            ConnectionIdleLifetimeSeconds = builder.ConnectionIdleLifetime,
            ApplicationName = applicationName.Trim(),
            SearchPath = Empty(builder.SearchPath),
        };
        return Build(profile);
    }

    public static EffectiveConnectionConfiguration Build(ConnectionProfile profile)
    {
        profile = profile with
        {
            Id = profile.Id.Trim(),
            Name = profile.Name.Trim(),
            Host = profile.Host.Trim(),
            Database = profile.Database.Trim(),
            Username = profile.Username.Trim(),
            ApplicationName = profile.ApplicationName.Trim(),
            SearchPath = profile.SearchPath?.Trim(),
            AdvancedOptions = new ReadOnlyDictionary<string, string>(
                new Dictionary<string, string>(profile.AdvancedOptions, StringComparer.OrdinalIgnoreCase)),
        };
        ConnectionProfileValidator.Validate(profile).ThrowIfInvalid();
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = profile.Host,
            Port = profile.Port,
            Database = profile.Database,
            Username = profile.Username,
            Password = profile.Password,
            SslMode = profile.SslMode,
            RootCertificate = profile.RootCertificate,
            SslCertificate = profile.ClientCertificate,
            SslKey = profile.ClientKey,
            SslPassword = profile.ClientCertificatePassword,
            Timeout = profile.ConnectionTimeoutSeconds,
            CommandTimeout = profile.CommandTimeoutSeconds,
            KeepAlive = profile.KeepAliveSeconds,
            Pooling = profile.Pooling,
            MinPoolSize = profile.MinimumPoolSize,
            MaxPoolSize = profile.MaximumPoolSize,
            ConnectionIdleLifetime = profile.ConnectionIdleLifetimeSeconds,
            ApplicationName = profile.ApplicationName,
            SearchPath = profile.SearchPath,
            NoResetOnClose = false,
            IncludeErrorDetail = false,
            Options = profile.EffectiveReadOnly ? "-c default_transaction_read_only=on" : null,
        };
        foreach (var option in profile.AdvancedOptions) builder[option.Key] = option.Value;
        return new(profile, builder.ConnectionString);
    }

    private static string? Empty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static bool Has(System.Data.Common.DbConnectionStringBuilder builder, params string[] names)
        => names.Any(builder.ContainsKey);
}

public static class ConnectionFailureClassifier
{
    public static ConnectionFailureCategory Classify(Exception exception)
    {
        var chain = ExceptionChain(exception).ToArray();
        var postgresFailure = chain.OfType<PostgresException>().FirstOrDefault();
        if (postgresFailure is not null) return ClassifyPostgres(postgresFailure);
        if (exception is OperationCanceledException) return ConnectionFailureCategory.Cancelled;
        if (exception is AuthenticationException) return ConnectionFailureCategory.Ssl;
        if (exception is ArgumentException argument
            && (argument.Message.Contains("certificate", StringComparison.OrdinalIgnoreCase) || argument.Message.Contains("SSL", StringComparison.OrdinalIgnoreCase)))
            return ConnectionFailureCategory.Ssl;
        if (exception is ConnectionProfileValidationException or ArgumentException) return ConnectionFailureCategory.Validation;
        if (chain.Any(x => x is TimeoutException)) return ConnectionFailureCategory.Timeout;
        if (chain.OfType<SocketException>().FirstOrDefault() is { } socket)
            return socket.SocketErrorCode is SocketError.HostNotFound or SocketError.NoData ? ConnectionFailureCategory.Dns : ConnectionFailureCategory.Network;
        if (exception is NpgsqlException npgsql)
        {
            var message = string.Join(" ", chain.Select(x => x.Message));
            if (message.Contains("certificate", StringComparison.OrdinalIgnoreCase) || message.Contains("SSL", StringComparison.OrdinalIgnoreCase)) return ConnectionFailureCategory.Ssl;
            if (message.Contains("pool", StringComparison.OrdinalIgnoreCase)) return ConnectionFailureCategory.PoolExhausted;
            if (message.Contains("password", StringComparison.OrdinalIgnoreCase) || message.Contains("authentication", StringComparison.OrdinalIgnoreCase)) return ConnectionFailureCategory.Authentication;
            if (message.Contains("database", StringComparison.OrdinalIgnoreCase) && message.Contains("does not exist", StringComparison.OrdinalIgnoreCase)) return ConnectionFailureCategory.DatabaseUnavailable;
            if (message.Contains("timeout", StringComparison.OrdinalIgnoreCase) || message.Contains("timed out", StringComparison.OrdinalIgnoreCase)) return ConnectionFailureCategory.Timeout;
            return ConnectionFailureCategory.Network;
        }
        if (exception is ObjectDisposedException) return ConnectionFailureCategory.Disposed;
        return ConnectionFailureCategory.Unknown;
    }

    private static ConnectionFailureCategory ClassifyPostgres(PostgresException postgres) =>
        postgres.SqlState switch
        {
            "28P01" or "28000" => ConnectionFailureCategory.Authentication,
            "42501" => ConnectionFailureCategory.Authorisation,
            "3D000" => ConnectionFailureCategory.DatabaseUnavailable,
            "53300" => ConnectionFailureCategory.PoolExhausted,
            "57P01" or "57P02" or "57P03" => ConnectionFailureCategory.ServerUnavailable,
            _ => ConnectionFailureCategory.Unknown,
        };

    private static IEnumerable<Exception> ExceptionChain(Exception exception)
    {
        var pending = new Queue<Exception>();
        var seen = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
        pending.Enqueue(exception);
        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            if (!seen.Add(current)) continue;
            yield return current;
            if (current.InnerException is not null) pending.Enqueue(current.InnerException);
            if (current is AggregateException aggregate)
                foreach (var nested in aggregate.InnerExceptions) pending.Enqueue(nested);
        }
    }

    public static string UserMessage(ConnectionFailureCategory category) => category switch
    {
        ConnectionFailureCategory.Validation => "The connection profile is invalid. Review the highlighted settings.",
        ConnectionFailureCategory.Authentication => "PostgreSQL rejected the credentials. Review the username and password; the connection was not retried.",
        ConnectionFailureCategory.Authorisation => "PostgreSQL accepted the identity but denied the requested operation.",
        ConnectionFailureCategory.Ssl => "SSL or certificate validation failed. Review SSL mode, hostname, and certificate settings.",
        ConnectionFailureCategory.DatabaseUnavailable => "The selected database is unavailable. No fallback database was used.",
        ConnectionFailureCategory.PoolExhausted => "The connection pool is exhausted. Close idle work or increase the bounded profile limit.",
        ConnectionFailureCategory.Timeout => "The connection attempt timed out and was not retried.",
        ConnectionFailureCategory.Cancelled => "The connection attempt was cancelled.",
        ConnectionFailureCategory.ServerUnavailable or ConnectionFailureCategory.Network or ConnectionFailureCategory.Dns => "The PostgreSQL server could not be reached. Verify the host, port, and network.",
        ConnectionFailureCategory.Disposed => "The connection owner has been disposed.",
        _ => "The PostgreSQL connection could not be established.",
    };
}

public sealed record ConnectionTestResult(
    bool Succeeded,
    string ProfileId,
    string? ServerVersion,
    string? Database,
    string? Username,
    bool? IsEncrypted,
    bool? IsVerified,
    TimeSpan Elapsed,
    ConnectionFailureCategory? FailureCategory,
    string Message,
    string? SqlState = null,
    int? BackendProcessId = null);

public interface IConnectionProbe
{
    Task<ConnectionTestResult> TestAsync(EffectiveConnectionConfiguration configuration, CancellationToken cancellationToken = default);
}

public sealed class NpgsqlConnectionProbe(INpgsqlConnectionFactory? factory = null) : IConnectionProbe
{
    private readonly INpgsqlConnectionFactory _factory = factory ?? NpgsqlConnectionFactory.Shared;

    public async Task<ConnectionTestResult> TestAsync(EffectiveConnectionConfiguration configuration, CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            var testProfile = configuration.Profile with { Pooling = false, ApplicationName = "PostgreManagementStudio - Connection Test" };
            var testConfiguration = EffectiveConnectionConfigurationBuilder.Build(testProfile);
            await using var connection = _factory.Create(testConfiguration);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new NpgsqlCommand("""
                SELECT current_database(), current_user, version(),
                       COALESCE((SELECT ssl FROM pg_stat_ssl WHERE pid = pg_backend_pid()), false),
                       pg_backend_pid()
                """, connection);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                throw new NpgsqlException("PostgreSQL returned no validation row.");
            var encrypted = reader.GetBoolean(3);
            var verified = encrypted && testProfile.SslMode is SslMode.VerifyCA or SslMode.VerifyFull;
            return new(true, configuration.Profile.Id, reader.GetString(2), reader.GetString(0), reader.GetString(1), encrypted, verified, Stopwatch.GetElapsedTime(started), null, "Connection validated successfully.", BackendProcessId: reader.GetInt32(4));
        }
        catch (Exception ex)
        {
            var category = ConnectionFailureClassifier.Classify(ex);
            return new(false, configuration.Profile.Id, null, null, null, null, null, Stopwatch.GetElapsedTime(started), category, ConnectionFailureClassifier.UserMessage(category), (ex as PostgresException)?.SqlState);
        }
    }
}

public interface IConnectionDiagnostics
{
    void Record(ConnectionDiagnostic diagnostic);
}

public sealed class NullConnectionDiagnostics : IConnectionDiagnostics
{
    public static NullConnectionDiagnostics Instance { get; } = new();
    private NullConnectionDiagnostics() { }
    public void Record(ConnectionDiagnostic diagnostic) { }
}

public sealed class DiagnosticConnectionDiagnostics : IConnectionDiagnostics
{
    public void Record(ConnectionDiagnostic diagnostic)
    {
        Trace.WriteLine(
            $"connection_lifecycle attempt_id={diagnostic.AttemptId} profile_id={Safe(diagnostic.ProfileId)} " +
            $"operation={diagnostic.Operation} host={Safe(diagnostic.Host)} port={diagnostic.Port} " +
            $"database={Safe(diagnostic.Database)} username={Safe(diagnostic.Username)} ssl={diagnostic.SslMode} " +
            $"started={diagnostic.StartedAt:O} completed={diagnostic.CompletedAt:O} state={diagnostic.FinalState} " +
            $"failure={diagnostic.FailureCategory?.ToString() ?? "none"} retry_count={diagnostic.RetryCount} " +
            $"pool_wait_ms={diagnostic.PoolWait.TotalMilliseconds:F0} sqlstate={diagnostic.SqlState ?? "none"}");
    }

    private static string Safe(string value) => value.Replace('\r', '_').Replace('\n', '_').Replace(' ', '_');
}

public sealed class ConnectionLifecycleController(
    string profileId,
    IConnectionProbe probe,
    IConnectionDiagnostics? diagnostics = null) : IAsyncDisposable
{
    private readonly object _gate = new();
    private CancellationTokenSource? _attemptCancellation;
    private Guid? _attemptId;
    private ManagedConnectionState _state;
    private int _disposed;
    private readonly IConnectionDiagnostics _diagnostics = diagnostics ?? NullConnectionDiagnostics.Instance;

    public event EventHandler? StateChanged;
    public string ProfileId { get; } = profileId;
    public ManagedConnectionState State { get { lock (_gate) return _state; } }
    public Guid? AttemptId { get { lock (_gate) return _attemptId; } }
    public ConnectionTestResult? LastResult { get; private set; }
    public bool CanConnect => State is ManagedConnectionState.Disconnected or ManagedConnectionState.Failed;
    public bool CanDisconnect => State is ManagedConnectionState.Connecting or ManagedConnectionState.Connected or ManagedConnectionState.Reconnecting;

    public Task<ConnectionTestResult?> ConnectAsync(EffectiveConnectionConfiguration configuration, CancellationToken cancellationToken = default)
        => StartAsync(configuration, reconnecting: false, cancellationToken);

    public Task<ConnectionTestResult?> ReconnectAsync(EffectiveConnectionConfiguration configuration, CancellationToken cancellationToken = default)
        => StartAsync(configuration, reconnecting: true, cancellationToken);

    public void Disconnect()
    {
        CancellationTokenSource? cancellation = null;
        lock (_gate)
        {
            if (_state is ManagedConnectionState.Disconnected or ManagedConnectionState.Failed or ManagedConnectionState.Disposed) return;
            if (_state == ManagedConnectionState.Disconnecting) return;
            _state = ManagedConnectionState.Disconnecting;
            _attemptId = Guid.NewGuid();
            cancellation = _attemptCancellation;
        }
        cancellation?.Cancel();
        lock (_gate) if (_state == ManagedConnectionState.Disconnecting) _state = ManagedConnectionState.Disconnected;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        CancellationTokenSource? cancellation;
        lock (_gate) { cancellation = _attemptCancellation; _attemptId = Guid.NewGuid(); _state = ManagedConnectionState.Disposed; }
        cancellation?.Cancel();
        cancellation?.Dispose();
        await Task.CompletedTask;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private Task<ConnectionTestResult?> StartAsync(EffectiveConnectionConfiguration configuration, bool reconnecting, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (!reconnecting && _state is ManagedConnectionState.Connecting or ManagedConnectionState.Connected) return Task.FromResult<ConnectionTestResult?>(LastResult);
            if (reconnecting && _state != ManagedConnectionState.Connected && _state != ManagedConnectionState.Failed) return Task.FromResult<ConnectionTestResult?>(LastResult);
            _attemptCancellation?.Cancel();
            _attemptCancellation?.Dispose();
            _attemptCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _attemptId = Guid.NewGuid();
            _state = ManagedConnectionState.ResolvingProfile;
            var attempt = _attemptId.Value;
            var token = _attemptCancellation.Token;
            StateChanged?.Invoke(this, EventArgs.Empty);
            return RunAsync(attempt, configuration, reconnecting, token);
        }
    }

    private async Task<ConnectionTestResult?> RunAsync(Guid attempt, EffectiveConnectionConfiguration configuration, bool reconnecting, CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        lock (_gate)
        {
            if (_attemptId != attempt || _state == ManagedConnectionState.Disposed) return null;
            _state = reconnecting ? ManagedConnectionState.Reconnecting : ManagedConnectionState.Connecting;
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
        var result = await probe.TestAsync(configuration, cancellationToken).ConfigureAwait(false);
        ManagedConnectionState finalState;
        lock (_gate)
        {
            if (_attemptId != attempt || _state is ManagedConnectionState.Disposed or ManagedConnectionState.Disconnecting or ManagedConnectionState.Disconnected) return null;
            LastResult = result;
            _state = result.Succeeded ? ManagedConnectionState.Connected
                : result.FailureCategory == ConnectionFailureCategory.Cancelled ? ManagedConnectionState.Disconnected
                : ManagedConnectionState.Failed;
            finalState = _state;
        }
        _diagnostics.Record(new(
            attempt,
            ProfileId,
            reconnecting ? "Reconnect" : "Connect",
            configuration.Profile.Host,
            configuration.Profile.Port,
            configuration.Profile.Database,
            configuration.Profile.Username,
            configuration.Profile.SslMode,
            startedAt,
            DateTimeOffset.UtcNow,
            finalState,
            result.FailureCategory,
            0,
            TimeSpan.Zero,
            result.SqlState));
        StateChanged?.Invoke(this, EventArgs.Empty);
        return result;
    }
}

public interface IConnectionPoolInvalidator
{
    void Invalidate(EffectiveConnectionConfiguration configuration);
}

public sealed class NpgsqlConnectionPoolInvalidator(INpgsqlConnectionFactory factory) : IConnectionPoolInvalidator
{
    public void Invalidate(EffectiveConnectionConfiguration configuration) => factory.ClearPool(configuration);
}

public static class ConnectionResourceLimits
{
    public const int MaximumConnectionsPerProfile = 50;
    public const int MaximumRegisteredApplicationConnections = 200;
    public const int MaximumBackgroundConnections = 4;
    public const int MaximumAdministrativeConnections = 4;
}

public sealed class ConnectionProfileRegistry(IConnectionPoolInvalidator invalidator)
{
    private readonly object _gate = new();
    private readonly Dictionary<string, EffectiveConnectionConfiguration> _profiles = new(StringComparer.Ordinal);

    public IReadOnlyList<EffectiveConnectionConfiguration> Snapshots
    {
        get { lock (_gate) return _profiles.Values.ToArray(); }
    }

    public void Add(EffectiveConnectionConfiguration configuration)
    {
        lock (_gate)
        {
            if (_profiles.Values.Any(x => string.Equals(x.Profile.Name, configuration.Profile.Name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"A connection profile named '{configuration.Profile.Name}' already exists.");
            EnsureApplicationLimit(configuration);
            _profiles.Add(configuration.Profile.Id, configuration);
        }
    }

    public void Replace(EffectiveConnectionConfiguration configuration)
    {
        EffectiveConnectionConfiguration? old;
        lock (_gate)
        {
            if (!_profiles.TryGetValue(configuration.Profile.Id, out old)) throw new KeyNotFoundException("The connection profile no longer exists.");
            if (_profiles.Values.Any(x => x.Profile.Id != configuration.Profile.Id && string.Equals(x.Profile.Name, configuration.Profile.Name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"A connection profile named '{configuration.Profile.Name}' already exists.");
            EnsureApplicationLimit(configuration);
            _profiles[configuration.Profile.Id] = configuration;
        }
        if (old.Identity != configuration.Identity) invalidator.Invalidate(old);
    }

    public bool Delete(string profileId)
    {
        EffectiveConnectionConfiguration? old;
        lock (_gate) if (!_profiles.Remove(profileId, out old)) return false;
        invalidator.Invalidate(old);
        return true;
    }

    public bool TryResolve(string profileId, [NotNullWhen(true)] out EffectiveConnectionConfiguration? configuration)
    {
        lock (_gate) return _profiles.TryGetValue(profileId, out configuration);
    }

    private void EnsureApplicationLimit(EffectiveConnectionConfiguration candidate)
    {
        var reserved = _profiles.Values
            .Where(x => x.Profile.Id != candidate.Profile.Id && x.Profile.Pooling)
            .Sum(x => x.Profile.MaximumPoolSize);
        if (candidate.Profile.Pooling) reserved += candidate.Profile.MaximumPoolSize;
        if (reserved > ConnectionResourceLimits.MaximumRegisteredApplicationConnections)
            throw new InvalidOperationException(
                $"Registered profile pool limits cannot reserve more than {ConnectionResourceLimits.MaximumRegisteredApplicationConnections} application connections.");
    }
}

public static class ConnectionRetryPolicy
{
    public static bool CanRetry(ConnectionFailureCategory category, bool operationIsIdempotent, int retryCount)
        => operationIsIdempotent && retryCount < 2 && category is ConnectionFailureCategory.Network or ConnectionFailureCategory.Dns or ConnectionFailureCategory.ServerUnavailable;
}

public enum SessionCleanupDecision { ProviderReset, DiscardConnection }
public static class SessionResetPolicy
{
    public static SessionCleanupDecision Decide(bool connectionBroken, bool cleanupFailed, bool noResetOnClose, bool transactionAborted)
        => connectionBroken || cleanupFailed || noResetOnClose ? SessionCleanupDecision.DiscardConnection : SessionCleanupDecision.ProviderReset;
}

public sealed record ConnectionDiagnostic(
    Guid AttemptId,
    string ProfileId,
    string Operation,
    string Host,
    int Port,
    string Database,
    string Username,
    SslMode SslMode,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    ManagedConnectionState FinalState,
    ConnectionFailureCategory? FailureCategory,
    int RetryCount,
    TimeSpan PoolWait,
    string? SqlState);
