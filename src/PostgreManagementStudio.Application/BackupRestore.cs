using System.Diagnostics;
using System.Text;
using System.Text.Json.Serialization;

namespace PostgreManagementStudio.Application;

public enum BackupFormat { Custom, PlainSql, Directory, Tar }

public sealed record DatabaseConnection(
    string Host,
    int Port,
    string Database,
    string Username,
    [property: JsonIgnore, DebuggerBrowsable(DebuggerBrowsableState.Never)] string? Password = null)
{
    public static DatabaseConnection FromConnectionString(string value)
    {
        var builder = new System.Data.Common.DbConnectionStringBuilder { ConnectionString = value };
        string Get(string name, string fallback = "") =>
            builder.TryGetValue(name, out var item) ? Convert.ToString(item) ?? fallback : fallback;
        return new(Get("Host", "localhost"),
            int.TryParse(Get("Port", "5432"), out var port) ? port : 5432,
            Get("Database", "postgres"), Get("Username", Get("User ID")), Get("Password"));
    }

    public override string ToString() => $"{Username}@{Host}:{Port}/{Database}";
}

public sealed record BackupOptions(
    DatabaseConnection Connection,
    string Destination,
    BackupFormat Format = BackupFormat.Custom,
    bool IncludeLargeObjects = true,
    bool IncludeOwner = true,
    bool IncludePrivileges = true,
    bool Verbose = false,
    bool CreateDatabase = false,
    bool DataOnly = false,
    bool SchemaOnly = false,
    int? CompressionLevel = null);

public sealed record RestoreOptions(
    DatabaseConnection Connection,
    string Source,
    BackupFormat Format,
    bool Clean = false,
    bool CreateDatabase = false,
    bool DataOnly = false,
    bool SchemaOnly = false,
    bool NoOwner = false,
    bool NoPrivileges = false,
    bool ExitOnError = true,
    bool SingleTransaction = false,
    bool Verbose = false,
    int? Jobs = null);

public sealed record ProcessOutputEntry(bool IsError, string Line, DateTimeOffset? Timestamp = null);
public sealed record ProcessExecutionRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    string? WorkingDirectory = null,
    IReadOnlyDictionary<string, string?>? Environment = null);
public sealed record ProcessExecutionResult(
    int ExitCode,
    IReadOnlyList<ProcessOutputEntry> Output,
    bool Cancelled,
    DateTimeOffset? StartedAt = null,
    DateTimeOffset? CompletedAt = null,
    bool TerminationEscalated = false,
    bool OutputTruncated = false);

public interface IExternalProcessRunner
{
    Task<ProcessExecutionResult> RunAsync(
        ProcessExecutionRequest request,
        IProgress<ProcessOutputEntry>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed class ExternalProcessRunner(
    int maximumCapturedLines = 2_000,
    TimeSpan? gracefulTerminationTimeout = null) : IExternalProcessRunner
{
    private readonly TimeSpan _gracefulTerminationTimeout =
        gracefulTerminationTimeout ?? TimeSpan.FromSeconds(2);

    public async Task<ProcessExecutionResult> RunAsync(
        ProcessExecutionRequest request,
        IProgress<ProcessOutputEntry>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (maximumCapturedLines is < 10 or > 100_000)
            throw new ArgumentOutOfRangeException(nameof(maximumCapturedLines));
        ValidateExecutable(request.FileName);
        var startedAt = DateTimeOffset.UtcNow;
        // Keep a small header as well as the newest diagnostics. PostgreSQL tools place
        // archive identity/version metadata at the start of output, while failures and
        // summaries are normally at the end.
        var headerCapacity = Math.Max(1, Math.Min(64, maximumCapturedLines / 4));
        var tailCapacity = maximumCapturedLines - headerCapacity;
        var outputHeader = new List<ProcessOutputEntry>(headerCapacity);
        var outputTail = new Queue<ProcessOutputEntry>(Math.Min(tailCapacity, 256));
        var outputGate = new object();
        var truncated = false;
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(request.FileName)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = request.WorkingDirectory ?? Environment.CurrentDirectory,
            },
            EnableRaisingEvents = true,
        };
        foreach (var argument in request.Arguments) process.StartInfo.ArgumentList.Add(argument);
        if (request.Environment is not null)
            foreach (var item in request.Environment) process.StartInfo.Environment[item.Key] = item.Value;

        try
        {
            if (!process.Start()) throw new BackupProcessStartException("The PostgreSQL tool did not start.");
        }
        catch (Exception ex) when (ex is not BackupProcessStartException)
        {
            throw new BackupProcessStartException(
                $"The PostgreSQL tool could not start ({ex.GetType().Name}).", ex);
        }

        var stdout = ReadAsync(process.StandardOutput, false);
        var stderr = ReadAsync(process.StandardError, true);
        var escalated = false;
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
            return new(process.ExitCode, Snapshot(), false, startedAt, DateTimeOffset.UtcNow,
                false, truncated);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.CloseMainWindow();
                    using var grace = new CancellationTokenSource(_gracefulTerminationTimeout);
                    try { await process.WaitForExitAsync(grace.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException)
                    {
                        escalated = true;
                        if (!process.HasExited) process.Kill(entireProcessTree: true);
                    }
                }
            }
            catch
            {
                escalated = true;
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            }
            try { await process.WaitForExitAsync().ConfigureAwait(false); } catch { }
            await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
            return new(-1, Snapshot(), true, startedAt, DateTimeOffset.UtcNow, escalated, truncated);
        }

        async Task ReadAsync(StreamReader reader, bool error)
        {
            while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                var entry = new ProcessOutputEntry(error, BackupSecretRedactor.Redact(line), DateTimeOffset.UtcNow);
                lock (outputGate)
                {
                    if (outputHeader.Count < headerCapacity)
                    {
                        outputHeader.Add(entry);
                    }
                    else
                    {
                        if (outputTail.Count == tailCapacity)
                        {
                            outputTail.Dequeue();
                            truncated = true;
                        }
                        outputTail.Enqueue(entry);
                    }
                }
                progress?.Report(entry);
            }
        }

        IReadOnlyList<ProcessOutputEntry> Snapshot()
        {
            lock (outputGate) return [.. outputHeader, .. outputTail];
        }
    }

    private static void ValidateExecutable(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var full = Path.GetFullPath(path);
        if (!File.Exists(full)) throw new BackupProcessStartException("The PostgreSQL tool executable was not found.");
        if ((File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
            throw new BackupProcessStartException("PostgreSQL tool executable redirections are not allowed.");
        var name = Path.GetFileNameWithoutExtension(full);
        if (name is not ("pg_dump" or "pg_restore" or "psql" or "createdb" or "dropdb"))
            throw new BackupProcessStartException("The selected executable is not an approved PostgreSQL tool.");
    }
}

public sealed class BackupProcessStartException(string message, Exception? innerException = null)
    : InvalidOperationException(message, innerException);

public sealed record PostgreSqlTools(string PgDump, string PgRestore, string Psql);

public sealed class PostgreSqlToolLocator
{
    private readonly object _gate = new();
    private (string? Configured, DateTimeOffset Expires, PostgreSqlTools? Tools)? _cached;

    public PostgreSqlTools? Locate(string? configuredDirectory = null)
    {
        lock (_gate)
        {
            if (_cached is { } cached
                && cached.Expires > DateTimeOffset.UtcNow
                && string.Equals(cached.Configured, configuredDirectory, StringComparison.OrdinalIgnoreCase))
                return cached.Tools;
        }

        var directories = CandidateDirectories(configuredDirectory).Distinct(StringComparer.OrdinalIgnoreCase);
        var dump = FindExpected(directories, "pg_dump");
        var restore = FindExpected(directories, "pg_restore");
        var psql = FindExpected(directories, "psql");
        var tools = dump is not null && restore is not null && psql is not null
            ? new PostgreSqlTools(dump, restore, psql) : null;
        lock (_gate) _cached = (configuredDirectory, DateTimeOffset.UtcNow.AddMinutes(5), tools);
        return tools;
    }

    public Task<PostgreSqlTools?> LocateAsync(
        string? configuredDirectory = null,
        CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Locate(configuredDirectory);
        }, cancellationToken);

    public void Invalidate()
    {
        lock (_gate) _cached = null;
    }

    private static IEnumerable<string> CandidateDirectories(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured)) yield return Path.GetFullPath(configured);
        yield return AppContext.BaseDirectory;
        for (var major = 30; major >= 12; major--)
            yield return $@"C:\Program Files\PostgreSQL\{major}\bin";
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            yield return directory;
    }

    private static string? FindExpected(IEnumerable<string> directories, string executable)
    {
        foreach (var directory in directories)
        {
            foreach (var filename in OperatingSystem.IsWindows()
                ? new[] { executable + ".exe" } : new[] { executable })
            {
                var candidate = Path.GetFullPath(Path.Combine(directory, filename));
                if (!File.Exists(candidate)) continue;
                if (!string.Equals(Path.GetFileNameWithoutExtension(candidate), executable,
                    StringComparison.OrdinalIgnoreCase)) continue;
                return candidate;
            }
        }
        return null;
    }
}

public static class BackupCommandBuilder
{
    public static ProcessExecutionRequest Build(BackupOptions options, PostgreSqlTools tools)
    {
        Validate(options);
        var arguments = new List<string>
        {
            "--host", options.Connection.Host,
            "--port", options.Connection.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--username", options.Connection.Username,
            "--no-password",
            "--format", options.Format switch
            {
                BackupFormat.Custom => "custom",
                BackupFormat.PlainSql => "plain",
                BackupFormat.Tar => "tar",
                _ => "directory",
            },
            "--file", Path.GetFullPath(options.Destination),
        };
        if (!options.IncludeLargeObjects) arguments.Add("--no-blobs");
        if (!options.IncludeOwner) arguments.Add("--no-owner");
        if (!options.IncludePrivileges) arguments.Add("--no-privileges");
        if (options.Verbose) arguments.Add("--verbose");
        if (options.CreateDatabase) arguments.Add("--create");
        if (options.DataOnly) arguments.Add("--data-only");
        if (options.SchemaOnly) arguments.Add("--schema-only");
        if (options.CompressionLevel is { } compression)
        {
            arguments.Add("--compress");
            arguments.Add(compression.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        arguments.Add(options.Connection.Database);
        return new(tools.PgDump, arguments, Environment.CurrentDirectory,
            PasswordEnvironment(options.Connection));
    }

    public static string Preview(ProcessExecutionRequest request) =>
        $"{Path.GetFileName(request.FileName)} " + string.Join(" ", request.Arguments.Select(Quote));

    private static string Quote(string value) =>
        value.Any(char.IsWhiteSpace) || value.Contains('"')
            ? '"' + value.Replace("\"", "\\\"", StringComparison.Ordinal) + '"'
            : value;

    private static IReadOnlyDictionary<string, string?> PasswordEnvironment(DatabaseConnection connection) =>
        string.IsNullOrEmpty(connection.Password)
            ? new Dictionary<string, string?>()
            : new Dictionary<string, string?> { ["PGPASSWORD"] = connection.Password };

    private static void Validate(BackupOptions options)
    {
        BackupPlanValidator.ValidateConnection(options.Connection);
        if (string.IsNullOrWhiteSpace(options.Destination)) throw new ArgumentException("A backup destination is required.");
        if (options.DataOnly && options.SchemaOnly) throw new ArgumentException("Data-only and schema-only cannot both be selected.");
        if (options.Format == BackupFormat.PlainSql && options.CompressionLevel is not null)
            throw new ArgumentException("Compression is unavailable for plain SQL backups.");
        if (options.CompressionLevel is < 0 or > 9) throw new ArgumentOutOfRangeException(nameof(options.CompressionLevel));
        if (options.Format == BackupFormat.Directory && File.Exists(options.Destination))
            throw new ArgumentException("Directory backup destination is an existing file.");
    }
}

public static class RestoreCommandBuilder
{
    public static ProcessExecutionRequest Build(RestoreOptions options, PostgreSqlTools tools)
    {
        BackupPlanValidator.ValidateConnection(options.Connection);
        if (options.Format == BackupFormat.Directory)
        {
            if (!Directory.Exists(options.Source)) throw new DirectoryNotFoundException("Backup source directory was not found.");
        }
        else if (!File.Exists(options.Source)) throw new FileNotFoundException("Backup source was not found.", options.Source);
        if (options.DataOnly && options.SchemaOnly) throw new ArgumentException("Data-only and schema-only cannot both be selected.");
        if (options.Format == BackupFormat.PlainSql && options.Jobs is not null)
            throw new ArgumentException("Parallel jobs are unavailable for plain SQL restore.");
        if (options.SingleTransaction && options.Jobs is > 1)
            throw new ArgumentException("Parallel restore and single-transaction restore are incompatible.");

        var executable = options.Format == BackupFormat.PlainSql ? tools.Psql : tools.PgRestore;
        var arguments = new List<string>
        {
            "--host", options.Connection.Host,
            "--port", options.Connection.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--username", options.Connection.Username,
            "--no-password",
            "--dbname", options.CreateDatabase ? "postgres" : options.Connection.Database,
        };
        if (options.Format == BackupFormat.PlainSql)
        {
            if (options.ExitOnError) { arguments.Add("--set"); arguments.Add("ON_ERROR_STOP=on"); }
            if (options.SingleTransaction) arguments.Add("--single-transaction");
            arguments.Add("--file");
            arguments.Add(Path.GetFullPath(options.Source));
        }
        else
        {
            if (options.Clean) arguments.Add("--clean");
            if (options.CreateDatabase) arguments.Add("--create");
            if (options.DataOnly) arguments.Add("--data-only");
            if (options.SchemaOnly) arguments.Add("--schema-only");
            if (options.NoOwner) arguments.Add("--no-owner");
            if (options.NoPrivileges) arguments.Add("--no-privileges");
            if (options.ExitOnError) arguments.Add("--exit-on-error");
            if (options.SingleTransaction) arguments.Add("--single-transaction");
            if (options.Verbose) arguments.Add("--verbose");
            if (options.Jobs is { } jobs)
            {
                if (jobs < 1) throw new ArgumentOutOfRangeException(nameof(options.Jobs));
                arguments.Add("--jobs");
                arguments.Add(jobs.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            arguments.Add(Path.GetFullPath(options.Source));
        }
        return new(executable, arguments, Environment.CurrentDirectory,
            string.IsNullOrEmpty(options.Connection.Password)
                ? new Dictionary<string, string?>()
                : new Dictionary<string, string?> { ["PGPASSWORD"] = options.Connection.Password });
    }
}
