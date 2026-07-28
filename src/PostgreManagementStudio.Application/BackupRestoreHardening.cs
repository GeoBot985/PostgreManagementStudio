using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace PostgreManagementStudio.Application;

public enum BackupRestoreOperationType { Backup, Restore }
public enum BackupRestoreOperationState
{
    Idle, Validating, Preparing, WaitingForConfirmation, Starting, Running, Cancelling,
    Completed, CompletedWithWarnings, Failed, Cancelled, Disposed,
}
public enum BackupRestoreFailureCategory
{
    Validation, ToolNotFound, ToolIncompatible, ProcessStartFailed, Authentication,
    PermissionDenied, ConnectionFailed, DatabaseNotFound, DatabaseAlreadyExists,
    FileNotFound, FileAccessDenied, DestinationNotWritable, InsufficientSpace,
    InvalidBackup, CorruptBackup, UnsupportedFormat, UnsupportedArchiveVersion,
    RestoreConflict, ObjectAlreadyExists, DependencyFailure, Cancelled,
    ProcessTerminated, Unknown,
}
public enum RestoreTransactionSemantics { NonTransactional, SingleTransaction, PartiallyTransactional }

public sealed record PostgreSqlToolVersion(string Name, string Path, string VersionText, int Major);
public sealed record ValidatedPostgreSqlTools(
    PostgreSqlTools Paths,
    PostgreSqlToolVersion PgDump,
    PostgreSqlToolVersion PgRestore,
    PostgreSqlToolVersion Psql);

public sealed record ToolCompatibilityResult(bool Supported, IReadOnlyList<string> Warnings, string? Error);

public static class PostgreSqlToolCompatibility
{
    public static ToolCompatibilityResult ForBackup(int toolMajor, int? serverMajor)
    {
        if (toolMajor < 9) return new(false, [], "The pg_dump version is unsupported.");
        if (serverMajor is null) return new(true, ["The server version is unknown; tool compatibility could not be fully verified."], null);
        if (toolMajor < serverMajor)
            return new(false, [], $"pg_dump {toolMajor} cannot safely dump PostgreSQL {serverMajor}. Use the same or a newer major version.");
        var warnings = toolMajor > serverMajor + 1
            ? new[] { $"pg_dump {toolMajor} is newer than PostgreSQL {serverMajor}; verify compatibility in the target environment." }
            : Array.Empty<string>();
        return new(true, warnings, null);
    }

    public static ToolCompatibilityResult ForRestore(int toolMajor, int? serverMajor)
    {
        if (toolMajor < 9) return new(false, [], "The restore tool version is unsupported.");
        if (serverMajor is null) return new(true, ["The target server version is unknown; compatibility could not be fully verified."], null);
        var warnings = toolMajor != serverMajor
            ? new[] { $"Restore tool major {toolMajor} differs from target PostgreSQL {serverMajor}." }
            : Array.Empty<string>();
        return new(true, warnings, null);
    }
}

public sealed class PostgreSqlToolDiscoveryService(
    PostgreSqlToolLocator locator,
    IExternalProcessRunner runner)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private (string? Directory, DateTimeOffset Expires, ValidatedPostgreSqlTools Tools)? _cache;

    public async Task<ValidatedPostgreSqlTools> DiscoverAsync(
        string? configuredDirectory = null,
        CancellationToken cancellationToken = default)
    {
        if (_cache is { } cached && cached.Expires > DateTimeOffset.UtcNow
            && string.Equals(cached.Directory, configuredDirectory, StringComparison.OrdinalIgnoreCase))
            return cached.Tools;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cache is { } inside && inside.Expires > DateTimeOffset.UtcNow
                && string.Equals(inside.Directory, configuredDirectory, StringComparison.OrdinalIgnoreCase))
                return inside.Tools;
            var paths = await locator.LocateAsync(configuredDirectory, cancellationToken).ConfigureAwait(false)
                ?? throw new BackupRestoreException(BackupRestoreFailureCategory.ToolNotFound,
                    "Required PostgreSQL tools could not be located.");
            var dump = await VersionAsync(paths.PgDump, "pg_dump", cancellationToken).ConfigureAwait(false);
            var restore = await VersionAsync(paths.PgRestore, "pg_restore", cancellationToken).ConfigureAwait(false);
            var psql = await VersionAsync(paths.Psql, "psql", cancellationToken).ConfigureAwait(false);
            var tools = new ValidatedPostgreSqlTools(paths, dump, restore, psql);
            _cache = (configuredDirectory, DateTimeOffset.UtcNow.AddMinutes(5), tools);
            return tools;
        }
        finally { _gate.Release(); }
    }

    public void Invalidate()
    {
        _cache = null;
        locator.Invalidate();
    }

    private async Task<PostgreSqlToolVersion> VersionAsync(
        string path,
        string expectedName,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(Path.GetFileNameWithoutExtension(path), expectedName, StringComparison.OrdinalIgnoreCase))
            throw new BackupRestoreException(BackupRestoreFailureCategory.ToolNotFound,
                $"The selected {expectedName} path does not identify the expected executable.");
        var result = await runner.RunAsync(new(path, ["--version"]), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var text = string.Join(" ", result.Output.Select(x => x.Line));
        var major = PostgreSqlToolVersionParser.Major(text)
            ?? throw new BackupRestoreException(BackupRestoreFailureCategory.ToolIncompatible,
                $"{expectedName} returned an unrecognised version.");
        return new(expectedName, Path.GetFullPath(path), text, major);
    }
}

public sealed record BackupOperationPlan(
    Guid OperationId,
    string ConnectionProfileId,
    string ServerIdentity,
    DatabaseConnection Connection,
    BackupFormat Format,
    string Destination,
    string TemporaryDestination,
    bool AllowOverwrite,
    BackupOptions Options,
    PostgreSqlToolVersion Tool,
    PostgreSqlToolVersion InspectionTool,
    int? ServerMajorVersion,
    IReadOnlyList<string> CompatibilityWarnings,
    DateTimeOffset CreatedAt)
{
    public override string ToString() =>
        $"Backup {OperationId}: {Connection} -> {Path.GetFileName(Destination)} ({Format}, {Tool.Name} {Tool.Major})";
}

public sealed record RestoreOperationPlan(
    Guid OperationId,
    string ConnectionProfileId,
    string ServerIdentity,
    DatabaseConnection Connection,
    string Source,
    BackupFormat DetectedFormat,
    RestoreOptions Options,
    PostgreSqlToolVersion Tool,
    int? ServerMajorVersion,
    IReadOnlyList<string> CompatibilityWarnings,
    bool IsDestructive,
    RestoreTransactionSemantics TransactionSemantics,
    DateTimeOffset CreatedAt)
{
    public override string ToString() =>
        $"Restore {OperationId}: {Path.GetFileName(Source)} -> {Connection} ({DetectedFormat}, {Tool.Name} {Tool.Major})";
}

public static class BackupPlanValidator
{
    public static void ValidateConnection(DatabaseConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrWhiteSpace(connection.Host);
        ArgumentException.ThrowIfNullOrWhiteSpace(connection.Database);
        ArgumentException.ThrowIfNullOrWhiteSpace(connection.Username);
        if (connection.Port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(connection.Port));
        if (connection.Host.Any(char.IsControl) || connection.Database.Any(char.IsControl)
            || connection.Username.Any(char.IsControl))
            throw new ArgumentException("Connection fields cannot contain control characters.");
    }

    public static void ValidateRestoreOptions(RestoreOptions options)
    {
        ValidateConnection(options.Connection);
        if (options.DataOnly && options.SchemaOnly) throw new ArgumentException("Data-only and schema-only cannot both be selected.");
        if (options.SingleTransaction && options.Jobs > 1)
            throw new ArgumentException("Parallel restore cannot use a single transaction.");
        if (options.Format == BackupFormat.PlainSql && options.Jobs is not null)
            throw new ArgumentException("Plain SQL restore cannot use parallel jobs.");
        if (options.CreateDatabase && options.Format == BackupFormat.PlainSql)
            throw new ArgumentException("Create-database restore requires an archive with verifiable database metadata.");
        if (options.CreateDatabase && new[] { "postgres", "template0", "template1" }
            .Contains(options.Connection.Database, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Create-database restore cannot target a protected maintenance database.");
    }
}

public static class BackupOperationPlanFactory
{
    public static BackupOperationPlan CreateBackup(
        string profileId,
        string serverIdentity,
        BackupOptions options,
        ValidatedPostgreSqlTools tools,
        int? serverMajorVersion,
        bool allowOverwrite = false)
    {
        BackupPlanValidator.ValidateConnection(options.Connection);
        var destination = BackupSafetyValidator.ValidateDestination(options.Destination, options.Format, allowOverwrite);
        var compatibility = PostgreSqlToolCompatibility.ForBackup(tools.PgDump.Major, serverMajorVersion);
        if (!compatibility.Supported)
            throw new BackupRestoreException(BackupRestoreFailureCategory.ToolIncompatible, compatibility.Error!);
        var id = Guid.NewGuid();
        var temporary = BackupAtomicOutput.TemporaryPath(destination, id, options.Format);
        var snapshot = options with { Destination = temporary, Connection = options.Connection with { } };
        return new(id, profileId, serverIdentity, snapshot.Connection, options.Format, destination,
            temporary, allowOverwrite, snapshot, tools.PgDump, tools.PgRestore, serverMajorVersion,
            compatibility.Warnings.ToArray(), DateTimeOffset.UtcNow);
    }

    public static RestoreOperationPlan CreateRestore(
        string profileId,
        string serverIdentity,
        RestoreOptions options,
        BackupInspectionResult inspection,
        ValidatedPostgreSqlTools tools,
        int? serverMajorVersion)
    {
        BackupPlanValidator.ValidateRestoreOptions(options);
        if (!inspection.IsValid)
            throw new BackupRestoreException(BackupRestoreFailureCategory.InvalidBackup,
                inspection.Warning ?? "The backup file is invalid.");
        if (inspection.Format != options.Format)
            throw new BackupRestoreException(BackupRestoreFailureCategory.UnsupportedFormat,
                $"Detected {inspection.Format} input does not match the selected {options.Format} format.");
        if (options.CreateDatabase
            && (string.IsNullOrWhiteSpace(inspection.SourceDatabase)
                || !string.Equals(inspection.SourceDatabase, options.Connection.Database,
                    StringComparison.Ordinal)))
            throw new BackupRestoreException(BackupRestoreFailureCategory.Validation,
                "Create-database restore requires the archive database name to exactly match the confirmed target.");
        var tool = inspection.Format == BackupFormat.PlainSql ? tools.Psql : tools.PgRestore;
        var compatibility = PostgreSqlToolCompatibility.ForRestore(tool.Major, serverMajorVersion);
        if (!compatibility.Supported)
            throw new BackupRestoreException(BackupRestoreFailureCategory.ToolIncompatible, compatibility.Error!);
        var source = Path.GetFullPath(options.Source);
        var snapshot = options with { Source = source, Connection = options.Connection with { } };
        var destructive = true;
        var semantics = options.SingleTransaction
            ? RestoreTransactionSemantics.SingleTransaction
            : inspection.Format == BackupFormat.PlainSql
                ? RestoreTransactionSemantics.PartiallyTransactional
                : RestoreTransactionSemantics.NonTransactional;
        return new(Guid.NewGuid(), profileId, serverIdentity, snapshot.Connection, source,
            inspection.Format, snapshot, tool, serverMajorVersion, compatibility.Warnings.ToArray(),
            destructive, semantics, DateTimeOffset.UtcNow);
    }
}

public sealed record RestoreConfirmationToken(
    Guid OperationId,
    string TargetFingerprint,
    DateTimeOffset ConfirmedAt);

public static class RestoreConfirmation
{
    public static RestoreConfirmationToken Create(RestoreOperationPlan plan) =>
        new(plan.OperationId, TargetFingerprint(plan), DateTimeOffset.UtcNow);

    public static string Summary(RestoreOperationPlan plan) =>
        $"Server: {plan.Connection.Host}:{plan.Connection.Port}\n" +
        $"Database: {plan.Connection.Database}\n" +
        $"Source: {plan.Source}\n" +
        $"Destructive options: {(plan.Options.Clean ? "clean existing objects; " : "")}" +
        $"{(plan.Options.CreateDatabase ? "create database; " : "")}" +
        $"{(plan.IsDestructive ? "existing objects may be removed." : "existing objects are not explicitly cleaned.")}";

    public static bool Matches(RestoreOperationPlan plan, RestoreConfirmationToken token) =>
        token.OperationId == plan.OperationId
        && CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(token.TargetFingerprint),
            Encoding.UTF8.GetBytes(TargetFingerprint(plan)));

    private static string TargetFingerprint(RestoreOperationPlan plan) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{plan.ServerIdentity}|{plan.Connection.Host}|{plan.Connection.Port}|{plan.Connection.Database}|" +
            $"{plan.Source}|{plan.Options.Clean}|{plan.Options.CreateDatabase}|{plan.Options.SingleTransaction}")));
}

public sealed record BackupRestoreValidationResult(bool Succeeded, int? ServerMajorVersion, string Message);

public interface IBackupRestoreConnectionValidator
{
    Task<BackupRestoreValidationResult> ValidateAsync(
        DatabaseConnection connection,
        bool databaseMustExist,
        CancellationToken cancellationToken = default);
}

public sealed record BackupRestoreExecutionResult(
    Guid OperationId,
    BackupRestoreOperationType OperationType,
    BackupRestoreOperationState State,
    BackupRestoreFailureCategory? FailureCategory,
    string Message,
    int ExitCode,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<ProcessOutputEntry> Output,
    long OutputSize,
    bool AtomicCommit,
    bool PartialOutputRemains,
    bool TargetMayBePartiallyModified,
    bool ValidationSucceeded,
    bool VerificationSucceeded,
    bool CancellationRequested,
    bool TerminationEscalated,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt);

public sealed class BackupRestoreException(
    BackupRestoreFailureCategory category,
    string message,
    Exception? innerException = null) : InvalidOperationException(message, innerException)
{
    public BackupRestoreFailureCategory Category { get; } = category;
}

public static class BackupRestoreErrorClassifier
{
    public static BackupRestoreFailureCategory Classify(Exception exception)
    {
        if (exception is BackupRestoreException known) return known.Category;
        if (exception is OperationCanceledException) return BackupRestoreFailureCategory.Cancelled;
        if (exception is BackupProcessStartException) return BackupRestoreFailureCategory.ProcessStartFailed;
        if (exception is FileNotFoundException or DirectoryNotFoundException) return BackupRestoreFailureCategory.FileNotFound;
        if (exception is UnauthorizedAccessException) return BackupRestoreFailureCategory.FileAccessDenied;
        if (exception is IOException) return BackupRestoreFailureCategory.DestinationNotWritable;
        if (exception is ArgumentException) return BackupRestoreFailureCategory.Validation;
        return BackupRestoreFailureCategory.Unknown;
    }

    public static BackupRestoreFailureCategory ClassifyProcess(
        int exitCode,
        IEnumerable<ProcessOutputEntry> output)
    {
        if (exitCode == 0) return BackupRestoreFailureCategory.Unknown;
        var text = string.Join("\n", output.Select(x => x.Line)).ToLowerInvariant();
        if (text.Contains("password authentication failed") || text.Contains("no password supplied"))
            return BackupRestoreFailureCategory.Authentication;
        if (text.Contains("permission denied")) return BackupRestoreFailureCategory.PermissionDenied;
        if (text.Contains("does not exist")) return BackupRestoreFailureCategory.DatabaseNotFound;
        if (text.Contains("database") && text.Contains("already exists"))
            return BackupRestoreFailureCategory.DatabaseAlreadyExists;
        if (text.Contains("already exists")) return BackupRestoreFailureCategory.ObjectAlreadyExists;
        if (text.Contains("unsupported version") || text.Contains("version") && text.Contains("not supported"))
            return BackupRestoreFailureCategory.UnsupportedArchiveVersion;
        if (text.Contains("could not open input file") || text.Contains("no such file"))
            return BackupRestoreFailureCategory.FileNotFound;
        if (text.Contains("input file does not appear") || text.Contains("invalid archive")
            || text.Contains("unexpected end of file"))
            return BackupRestoreFailureCategory.CorruptBackup;
        if (text.Contains("dependency")) return BackupRestoreFailureCategory.DependencyFailure;
        if (text.Contains("connection") || text.Contains("server")) return BackupRestoreFailureCategory.ConnectionFailed;
        return BackupRestoreFailureCategory.ProcessTerminated;
    }
}

public static class BackupSecretRedactor
{
    public static string Redact(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value ?? "";
        var redacted = System.Text.RegularExpressions.Regex.Replace(value,
            @"(?i)(password|pwd|passfile|pgpassword)\s*=\s*([^\s;]+)", "$1=[REDACTED]");
        redacted = System.Text.RegularExpressions.Regex.Replace(redacted,
            @"(?i)(postgres(?:ql)?://[^:\s/]+:)([^@\s]+)(@)", "$1[REDACTED]$3");
        return redacted.Replace('\0', '\uFFFD');
    }
}

public static class BackupAtomicOutput
{
    public static string TemporaryPath(string destination, Guid operationId, BackupFormat format)
    {
        var full = Path.GetFullPath(destination);
        var parent = format == BackupFormat.Directory ? Path.GetDirectoryName(full) : Path.GetDirectoryName(full);
        if (string.IsNullOrWhiteSpace(parent)) throw new ArgumentException("The destination parent directory is invalid.");
        return Path.Combine(parent, $".pms-{operationId:N}.partial");
    }

    public static bool Commit(string temporary, string destination, BackupFormat format, bool overwrite)
    {
        if (format == BackupFormat.Directory)
        {
            if (Directory.Exists(destination))
                throw new IOException("Atomic directory replacement is unavailable when the destination already exists.");
            Directory.Move(temporary, destination);
            return true;
        }
        if (!File.Exists(destination))
        {
            File.Move(temporary, destination);
            return true;
        }
        if (!overwrite) throw new IOException("Backup destination already exists.");
        try
        {
            File.Replace(temporary, destination, null, ignoreMetadataErrors: true);
            return true;
        }
        catch (PlatformNotSupportedException)
        {
            File.Move(temporary, destination, true);
            return false;
        }
    }

    public static bool Cleanup(string path, BackupFormat format)
    {
        try
        {
            if (format == BackupFormat.Directory)
            {
                if (Directory.Exists(path)) Directory.Delete(path, true);
            }
            else if (File.Exists(path)) File.Delete(path);
            return true;
        }
        catch { return false; }
    }
}

public sealed class BackupOperationLockManager(int maximumProcesses = 2)
{
    private readonly ConcurrentDictionary<string, Guid> _resources = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _processes = new(maximumProcesses, maximumProcesses);

    public async Task<IAsyncDisposable> AcquireAsync(
        Guid operationId,
        string resource,
        CancellationToken cancellationToken = default)
    {
        if (maximumProcesses is < 1 or > 8) throw new ArgumentOutOfRangeException(nameof(maximumProcesses));
        var key = Path.IsPathFullyQualified(resource) ? Path.GetFullPath(resource) : resource.Trim();
        if (!_resources.TryAdd(key, operationId))
            throw new BackupRestoreException(BackupRestoreFailureCategory.RestoreConflict,
                "Another backup or restore operation already owns this destination or target.");
        try
        {
            await _processes.WaitAsync(cancellationToken).ConfigureAwait(false);
            return new Lease(this, key, operationId);
        }
        catch
        {
            _resources.TryRemove(new KeyValuePair<string, Guid>(key, operationId));
            throw;
        }
    }

    private sealed class Lease(BackupOperationLockManager owner, string key, Guid operationId) : IAsyncDisposable
    {
        private int _disposed;
        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                owner._resources.TryRemove(new KeyValuePair<string, Guid>(key, operationId));
                owner._processes.Release();
            }
            return ValueTask.CompletedTask;
        }
    }
}

public sealed class BackupRestoreOperationController : IAsyncDisposable
{
    private readonly object _gate = new();
    private CancellationTokenSource? _active;
    private Guid? _operationId;
    private BackupRestoreOperationState _state;
    private bool _disposed;

    public BackupRestoreOperationState State { get { lock (_gate) return _state; } }
    public Guid? OperationId { get { lock (_gate) return _operationId; } }
    public bool CanStart => State is BackupRestoreOperationState.Idle
        or BackupRestoreOperationState.Completed or BackupRestoreOperationState.CompletedWithWarnings
        or BackupRestoreOperationState.Failed or BackupRestoreOperationState.Cancelled;
    public bool CanCancel => State is BackupRestoreOperationState.Validating or BackupRestoreOperationState.Preparing
        or BackupRestoreOperationState.WaitingForConfirmation or BackupRestoreOperationState.Starting
        or BackupRestoreOperationState.Running;

    public async Task<BackupRestoreExecutionResult?> RunAsync(
        Guid operationId,
        Func<Action<BackupRestoreOperationState>, CancellationToken, Task<BackupRestoreExecutionResult>> operation,
        CancellationToken cancellationToken = default)
    {
        CancellationToken token;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!CanStart) return null;
            _active?.Dispose();
            _active = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _operationId = operationId;
            _state = BackupRestoreOperationState.Validating;
            token = _active.Token;
        }
        var result = await operation(SetState, token).ConfigureAwait(false);
        lock (_gate)
        {
            if (_disposed || _operationId != operationId) return null;
            _state = result.State;
        }
        return result;

        void SetState(BackupRestoreOperationState state)
        {
            lock (_gate)
            {
                if (_disposed || _operationId != operationId) return;
                if (_state == state) return;
                if (!CanTransition(_state, state))
                    throw new InvalidOperationException($"Invalid backup/restore state transition: {_state} -> {state}.");
                _state = state;
            }
        }
    }

    public void Cancel()
    {
        lock (_gate)
        {
            if (!CanCancel || _disposed) return;
            _state = BackupRestoreOperationState.Cancelling;
            _active?.Cancel();
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposed) return ValueTask.CompletedTask;
            _disposed = true;
            _state = BackupRestoreOperationState.Disposed;
            _operationId = Guid.NewGuid();
            _active?.Cancel();
            _active?.Dispose();
            _active = null;
        }
        return ValueTask.CompletedTask;
    }

    private static bool CanTransition(
        BackupRestoreOperationState from,
        BackupRestoreOperationState to) => from switch
    {
        BackupRestoreOperationState.Validating => to is BackupRestoreOperationState.Preparing
            or BackupRestoreOperationState.WaitingForConfirmation
            or BackupRestoreOperationState.Starting
            or BackupRestoreOperationState.Failed
            or BackupRestoreOperationState.Cancelled,
        BackupRestoreOperationState.Preparing => to is BackupRestoreOperationState.Starting
            or BackupRestoreOperationState.Completed
            or BackupRestoreOperationState.CompletedWithWarnings
            or BackupRestoreOperationState.Failed
            or BackupRestoreOperationState.Cancelled,
        BackupRestoreOperationState.WaitingForConfirmation => to is BackupRestoreOperationState.Validating
            or BackupRestoreOperationState.Starting
            or BackupRestoreOperationState.Failed
            or BackupRestoreOperationState.Cancelled,
        BackupRestoreOperationState.Starting => to is BackupRestoreOperationState.Running
            or BackupRestoreOperationState.Failed
            or BackupRestoreOperationState.Cancelled,
        BackupRestoreOperationState.Running => to is BackupRestoreOperationState.Preparing
            or BackupRestoreOperationState.Completed
            or BackupRestoreOperationState.CompletedWithWarnings
            or BackupRestoreOperationState.Failed
            or BackupRestoreOperationState.Cancelled,
        BackupRestoreOperationState.Cancelling => to is BackupRestoreOperationState.Cancelled
            or BackupRestoreOperationState.Failed,
        _ => false,
    };
}

public sealed record BackupRestoreDiagnostic(
    Guid OperationId,
    BackupRestoreOperationType OperationType,
    string ConnectionProfileId,
    int? ServerMajorVersion,
    string ToolName,
    int ToolMajorVersion,
    BackupFormat Format,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    BackupRestoreOperationState FinalState,
    int ExitCode,
    long OutputSize,
    int WarningCount,
    bool CancellationRequested,
    bool TerminationEscalated,
    bool ValidationSucceeded,
    bool VerificationSucceeded,
    BackupRestoreFailureCategory? FailureCategory);

public interface IBackupRestoreDiagnostics { void Record(BackupRestoreDiagnostic diagnostic); }
public sealed class DiagnosticBackupRestoreDiagnostics : IBackupRestoreDiagnostics
{
    public void Record(BackupRestoreDiagnostic value) => Trace.WriteLine(
        $"backup_restore operation_id={value.OperationId} type={value.OperationType} " +
        $"profile_id={Safe(value.ConnectionProfileId)} server_major={value.ServerMajorVersion?.ToString() ?? "unknown"} " +
        $"tool={value.ToolName} tool_major={value.ToolMajorVersion} format={value.Format} " +
        $"started={value.StartedAt:O} completed={value.CompletedAt:O} state={value.FinalState} " +
        $"exit_code={value.ExitCode} size={value.OutputSize} warnings={value.WarningCount} " +
        $"cancel={value.CancellationRequested} escalated={value.TerminationEscalated} " +
        $"validated={value.ValidationSucceeded} verified={value.VerificationSucceeded} " +
        $"failure={value.FailureCategory?.ToString() ?? "none"}");
    private static string Safe(string value) => value.Replace('\r', '_').Replace('\n', '_').Replace(' ', '_');
}

public sealed class BackupRestoreOperationService(
    IExternalProcessRunner runner,
    IBackupRestoreConnectionValidator connections,
    BackupInspectionService inspection,
    BackupOperationLockManager locks,
    IBackupRestoreDiagnostics? diagnostics = null)
{
    private readonly IBackupRestoreDiagnostics _diagnostics =
        diagnostics ?? new DiagnosticBackupRestoreDiagnostics();
    private readonly ConcurrentDictionary<Guid, byte> _consumedConfirmations = new();

    public Task<BackupRestoreExecutionResult?> ExecuteBackupAsync(
        BackupOperationPlan plan,
        BackupRestoreOperationController controller,
        IProgress<ProcessOutputEntry>? progress = null,
        CancellationToken cancellationToken = default) =>
        controller.RunAsync(plan.OperationId,
            (state, token) => ExecuteBackupCoreAsync(plan, state, progress, token), cancellationToken);

    public Task<BackupRestoreExecutionResult?> ExecuteRestoreAsync(
        RestoreOperationPlan plan,
        RestoreConfirmationToken? confirmation,
        BackupRestoreOperationController controller,
        IProgress<ProcessOutputEntry>? progress = null,
        CancellationToken cancellationToken = default) =>
        controller.RunAsync(plan.OperationId,
            (state, token) => ExecuteRestoreCoreAsync(plan, confirmation, state, progress, token), cancellationToken);

    private async Task<BackupRestoreExecutionResult> ExecuteBackupCoreAsync(
        BackupOperationPlan plan,
        Action<BackupRestoreOperationState> state,
        IProgress<ProcessOutputEntry>? progress,
        CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        var validation = false;
        var verification = false;
        var atomic = false;
        var cleaned = true;
        ProcessExecutionResult? process = null;
        var warnings = plan.CompatibilityWarnings.ToList();
        BackupRestoreFailureCategory? failure = null;
        try
        {
            state(BackupRestoreOperationState.Validating);
            BackupSafetyValidator.ValidateDestination(plan.Destination, plan.Format, plan.AllowOverwrite);
            var connection = await connections.ValidateAsync(plan.Connection, true, cancellationToken).ConfigureAwait(false);
            if (!connection.Succeeded)
                throw new BackupRestoreException(BackupRestoreFailureCategory.ConnectionFailed, connection.Message);
            validation = true;
            await using var lease = await locks.AcquireAsync(plan.OperationId, plan.Destination, cancellationToken).ConfigureAwait(false);
            BackupAtomicOutput.Cleanup(plan.TemporaryDestination, plan.Format);
            state(BackupRestoreOperationState.Starting);
            var request = BackupCommandBuilder.Build(plan.Options, new(plan.Tool.Path, "", ""));
            state(BackupRestoreOperationState.Running);
            process = await runner.RunAsync(request, progress, cancellationToken).ConfigureAwait(false);
            if (process.Cancelled)
            {
                cleaned = BackupAtomicOutput.Cleanup(plan.TemporaryDestination, plan.Format);
                return Finish(BackupRestoreOperationState.Cancelled, BackupRestoreFailureCategory.Cancelled,
                    "Backup cancelled; incomplete output was removed.", false);
            }
            if (process.ExitCode != 0)
            {
                failure = BackupRestoreErrorClassifier.ClassifyProcess(process.ExitCode, process.Output);
                throw new BackupRestoreException(failure.Value, "The backup tool reported a failure.");
            }
            state(BackupRestoreOperationState.Preparing);
            BackupSafetyValidator.VerifyOutput(plan.TemporaryDestination, plan.Format);
            if (plan.Format is BackupFormat.Custom or BackupFormat.Tar or BackupFormat.Directory)
            {
                var inspected = await inspection.InspectAsync(plan.TemporaryDestination, plan.Format,
                    new("", plan.InspectionTool.Path, ""),
                    cancellationToken).ConfigureAwait(false);
                if (!inspected.IsValid)
                    throw new BackupRestoreException(BackupRestoreFailureCategory.CorruptBackup,
                        inspected.Warning ?? "Archive verification failed.");
            }
            verification = true;
            atomic = BackupAtomicOutput.Commit(plan.TemporaryDestination, plan.Destination, plan.Format, plan.AllowOverwrite);
            if (!atomic) warnings.Add("The destination filesystem did not support atomic replacement.");
            warnings.AddRange(BackupWarningClassifier.Warnings(process.Output));
            return Finish(warnings.Count == 0 ? BackupRestoreOperationState.Completed
                : BackupRestoreOperationState.CompletedWithWarnings, null,
                warnings.Count == 0 ? "Backup completed and basic structural verification passed."
                    : "Backup completed with warnings.", true);
        }
        catch (OperationCanceledException)
        {
            cleaned = BackupAtomicOutput.Cleanup(plan.TemporaryDestination, plan.Format);
            return Finish(BackupRestoreOperationState.Cancelled, BackupRestoreFailureCategory.Cancelled,
                "Backup cancelled; incomplete output cleanup was attempted.", false);
        }
        catch (Exception ex)
        {
            failure ??= BackupRestoreErrorClassifier.Classify(ex);
            cleaned = BackupAtomicOutput.Cleanup(plan.TemporaryDestination, plan.Format);
            return Finish(BackupRestoreOperationState.Failed, failure,
                BackupSecretRedactor.Redact(ex.Message), false);
        }
        finally
        {
            if (!atomic) cleaned = BackupAtomicOutput.Cleanup(plan.TemporaryDestination, plan.Format);
        }

        BackupRestoreExecutionResult Finish(
            BackupRestoreOperationState finalState,
            BackupRestoreFailureCategory? category,
            string message,
            bool committed)
        {
            var completed = DateTimeOffset.UtcNow;
            var destinationExists = plan.Format == BackupFormat.Directory
                ? Directory.Exists(plan.Destination) : File.Exists(plan.Destination);
            var size = destinationExists && plan.Format != BackupFormat.Directory
                ? new FileInfo(plan.Destination).Length : 0;
            var result = new BackupRestoreExecutionResult(plan.OperationId, BackupRestoreOperationType.Backup,
                finalState, category, message, process?.ExitCode ?? -1, warnings, process?.Output ?? [],
                size, committed && atomic, !cleaned, false,
                validation, verification, cancellationToken.IsCancellationRequested,
                process?.TerminationEscalated ?? false, started, completed);
            _diagnostics.Record(ToDiagnostic(result, plan.ConnectionProfileId, plan.ServerMajorVersion,
                plan.Tool, plan.Format));
            return result;
        }
    }

    private async Task<BackupRestoreExecutionResult> ExecuteRestoreCoreAsync(
        RestoreOperationPlan plan,
        RestoreConfirmationToken? confirmation,
        Action<BackupRestoreOperationState> state,
        IProgress<ProcessOutputEntry>? progress,
        CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        var validation = false;
        var verification = false;
        ProcessExecutionResult? process = null;
        var warnings = plan.CompatibilityWarnings.ToList();
        BackupRestoreFailureCategory? failure = null;
        try
        {
            state(BackupRestoreOperationState.Validating);
            if (plan.IsDestructive)
            {
                state(BackupRestoreOperationState.WaitingForConfirmation);
                if (confirmation is null || !RestoreConfirmation.Matches(plan, confirmation)
                    || !_consumedConfirmations.TryAdd(confirmation.OperationId, 0))
                    throw new BackupRestoreException(BackupRestoreFailureCategory.Validation,
                        "A fresh target-specific confirmation is required.");
            }
            var inspected = await inspection.InspectAsync(plan.Source, plan.DetectedFormat,
                plan.DetectedFormat == BackupFormat.PlainSql
                    ? new("", "", plan.Tool.Path) : new("", plan.Tool.Path, ""),
                cancellationToken).ConfigureAwait(false);
            if (!inspected.IsValid)
                throw new BackupRestoreException(BackupRestoreFailureCategory.InvalidBackup,
                    inspected.Warning ?? "Backup inspection failed.");
            var connection = await connections.ValidateAsync(plan.Connection,
                databaseMustExist: !plan.Options.CreateDatabase, cancellationToken).ConfigureAwait(false);
            if (!connection.Succeeded)
                throw new BackupRestoreException(BackupRestoreFailureCategory.ConnectionFailed, connection.Message);
            validation = true;
            var target = $"{plan.ServerIdentity}|{plan.Connection.Host}:{plan.Connection.Port}/{plan.Connection.Database}";
            await using var lease = await locks.AcquireAsync(plan.OperationId, target, cancellationToken).ConfigureAwait(false);
            state(BackupRestoreOperationState.Starting);
            var request = RestoreCommandBuilder.Build(plan.Options,
                plan.DetectedFormat == BackupFormat.PlainSql
                    ? new("", "", plan.Tool.Path) : new("", plan.Tool.Path, ""));
            state(BackupRestoreOperationState.Running);
            process = await runner.RunAsync(request, progress, cancellationToken).ConfigureAwait(false);
            if (process.Cancelled)
                return Finish(BackupRestoreOperationState.Cancelled, BackupRestoreFailureCategory.Cancelled,
                    "Restore cancelled. The target database may contain partial changes.");
            if (process.ExitCode != 0)
            {
                failure = BackupRestoreErrorClassifier.ClassifyProcess(process.ExitCode, process.Output);
                throw new BackupRestoreException(failure.Value,
                    plan.TransactionSemantics == RestoreTransactionSemantics.SingleTransaction
                        ? "Transactional restore failed before successful commit."
                        : "Restore failed. The target database may contain partial changes.");
            }
            warnings.AddRange(BackupWarningClassifier.Warnings(process.Output));
            state(BackupRestoreOperationState.Preparing);
            var post = await connections.ValidateAsync(plan.Connection, true, cancellationToken).ConfigureAwait(false);
            verification = post.Succeeded;
            if (!post.Succeeded) warnings.Add("Restore exited successfully, but the fresh post-restore connection check failed.");
            return Finish(warnings.Count == 0 ? BackupRestoreOperationState.Completed
                : BackupRestoreOperationState.CompletedWithWarnings, null,
                warnings.Count == 0 ? "Restore completed and the target connection was revalidated."
                    : "Restore completed with warnings.");
        }
        catch (OperationCanceledException)
        {
            return Finish(BackupRestoreOperationState.Cancelled, BackupRestoreFailureCategory.Cancelled,
                "Restore cancelled. The target database may contain partial changes.");
        }
        catch (Exception ex)
        {
            failure ??= BackupRestoreErrorClassifier.Classify(ex);
            return Finish(BackupRestoreOperationState.Failed, failure,
                BackupSecretRedactor.Redact(ex.Message));
        }

        BackupRestoreExecutionResult Finish(
            BackupRestoreOperationState finalState,
            BackupRestoreFailureCategory? category,
            string message)
        {
            var completed = DateTimeOffset.UtcNow;
            var partial = (finalState is BackupRestoreOperationState.Cancelled or BackupRestoreOperationState.Failed)
                && plan.TransactionSemantics != RestoreTransactionSemantics.SingleTransaction;
            var result = new BackupRestoreExecutionResult(plan.OperationId, BackupRestoreOperationType.Restore,
                finalState, category, message, process?.ExitCode ?? -1, warnings, process?.Output ?? [],
                0, false, false, partial, validation, verification,
                cancellationToken.IsCancellationRequested, process?.TerminationEscalated ?? false, started, completed);
            _diagnostics.Record(ToDiagnostic(result, plan.ConnectionProfileId, plan.ServerMajorVersion,
                plan.Tool, plan.DetectedFormat));
            return result;
        }
    }

    private static BackupRestoreDiagnostic ToDiagnostic(
        BackupRestoreExecutionResult result,
        string profileId,
        int? serverMajor,
        PostgreSqlToolVersion tool,
        BackupFormat format) =>
        new(result.OperationId, result.OperationType, profileId, serverMajor, tool.Name, tool.Major,
            format, result.StartedAt, result.CompletedAt, result.State, result.ExitCode,
            result.OutputSize, result.Warnings.Count, result.CancellationRequested,
            result.TerminationEscalated, result.ValidationSucceeded, result.VerificationSucceeded,
            result.FailureCategory);
}

public static class BackupWarningClassifier
{
    public static IReadOnlyList<string> Warnings(IEnumerable<ProcessOutputEntry> output) =>
        output.Select(x => x.Line)
            .Where(x => x.Contains("warning:", StringComparison.OrdinalIgnoreCase))
            .Select(BackupSecretRedactor.Redact)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(100)
            .ToArray();
}
