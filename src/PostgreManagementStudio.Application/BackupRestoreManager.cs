using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;

namespace PostgreManagementStudio.Application;

public sealed record PostgreSqlToolInfo(string Path, string Version);
public sealed record BackupInspectionResult(
    BackupFormat Format,
    string? ServerVersion,
    string? SourceDatabase,
    long SizeBytes,
    int ObjectCount,
    IReadOnlyList<string> Items,
    bool IsValid,
    string? Warning,
    BackupRestoreFailureCategory? FailureCategory = null);

public sealed record BackupOperationRecord(
    string Operation,
    string Server,
    string Database,
    string Path,
    BackupFormat Format,
    DateTimeOffset Started,
    DateTimeOffset Completed,
    TimeSpan Duration,
    string Result,
    string ToolVersion,
    int ExitCode,
    int WarningCount,
    string ErrorSummary);

public sealed class BackupOperationHistoryService(int maximumEntries = 100)
{
    private readonly object _gate = new();
    private readonly LinkedList<BackupOperationRecord> _entries = new();

    public IReadOnlyList<BackupOperationRecord> Entries
    {
        get { lock (_gate) return _entries.ToArray(); }
    }

    public void Add(BackupOperationRecord record)
    {
        var safe = record with
        {
            Path = Path.GetFileName(record.Path),
            ErrorSummary = BackupSecretRedactor.Redact(record.ErrorSummary),
        };
        lock (_gate)
        {
            _entries.AddFirst(safe);
            while (_entries.Count > Math.Max(1, maximumEntries)) _entries.RemoveLast();
        }
    }

    public void ClearCompleted()
    {
        lock (_gate)
            foreach (var item in _entries.Where(x => x.Result is "Completed" or "Cancelled").ToArray())
                _entries.Remove(item);
    }
}

public sealed class TemporaryCredentialService
{
    public async Task<(string Path, IReadOnlyDictionary<string, string?> Environment)> CreateAsync(
        DatabaseConnection connection,
        CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(Path.GetTempPath(), $"pms-pgpass-{Guid.NewGuid():N}");
        var content =
            $"{Escape(connection.Host)}:{connection.Port}:{Escape(connection.Database)}:{Escape(connection.Username)}:{Escape(connection.Password ?? "")}\n";
        await File.WriteAllTextAsync(path, content, new UTF8Encoding(false), cancellationToken)
            .ConfigureAwait(false);
        TryRestrictToCurrentUser(path);
        try { File.SetAttributes(path, FileAttributes.Hidden | FileAttributes.Temporary); } catch { }
        return (path, new Dictionary<string, string?> { ["PGPASSFILE"] = path });
    }

    public void Delete(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
        }
        catch { }
    }

    private static void TryRestrictToCurrentUser(string path)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            var identity = WindowsIdentity.GetCurrent().User;
            if (identity is null) return;
            var security = new FileSecurity();
            security.SetOwner(identity);
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.AddAccessRule(new FileSystemAccessRule(identity, FileSystemRights.FullControl,
                AccessControlType.Allow));
            new FileInfo(path).SetAccessControl(security);
        }
        catch { }
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace(":", "\\:");
}

public sealed class BackupInspectionService(IExternalProcessRunner runner)
{
    private const int MaximumInspectionItems = 5_000;

    public async Task<BackupInspectionResult> InspectAsync(
        string path,
        BackupFormat requestedFormat,
        PostgreSqlTools tools,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
            return Invalid(requestedFormat, 0, "A backup source is required.",
                BackupRestoreFailureCategory.FileNotFound);

        var fullPath = Path.GetFullPath(path);
        if ((File.Exists(fullPath) || Directory.Exists(fullPath))
            && (File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
            return Invalid(requestedFormat, Size(fullPath),
                "Backup source filesystem redirections are not supported.",
                BackupRestoreFailureCategory.InvalidBackup);
        var detected = DetectFormat(fullPath);
        if (detected is null)
            return Invalid(requestedFormat, Size(fullPath),
                "The selected source is empty, corrupt, or is not a recognised PostgreSQL backup.",
                BackupRestoreFailureCategory.InvalidBackup);
        if (detected != requestedFormat)
            return Invalid(detected.Value, Size(fullPath),
                $"Detected {detected} input does not match the selected {requestedFormat} format.",
                BackupRestoreFailureCategory.UnsupportedFormat);

        if (detected == BackupFormat.PlainSql)
            return new(detected.Value, null, null, Size(fullPath), 0, [],
                true, "Plain SQL cannot provide archive-level object selection.");

        if (string.IsNullOrWhiteSpace(tools.PgRestore) || !File.Exists(tools.PgRestore))
            return Invalid(detected.Value, Size(fullPath), "pg_restore is unavailable for archive inspection.",
                BackupRestoreFailureCategory.ToolNotFound);

        var result = await runner.RunAsync(
            new(tools.PgRestore, ["--list", fullPath]),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var items = result.Output.Where(x => !x.IsError)
            .Select(x => x.Line).Where(x => !string.IsNullOrWhiteSpace(x))
            .Take(MaximumInspectionItems).ToArray();
        var serverVersion = HeaderValue(items, "Dumped from database version:");
        var sourceDatabase = HeaderValue(items, "dbname:");
        var warning = result.ExitCode == 0
            ? null
            : BackupSecretRedactor.Redact(string.Join(Environment.NewLine,
                result.Output.Where(x => x.IsError).Select(x => x.Line).Take(100)));
        return new(detected.Value, serverVersion, sourceDatabase, Size(fullPath), items.Length, items,
            result.ExitCode == 0, warning,
            result.ExitCode == 0 ? null : BackupRestoreErrorClassifier.ClassifyProcess(result.ExitCode, result.Output));
    }

    public static BackupFormat? DetectFormat(string path)
    {
        if (Directory.Exists(path))
            return File.Exists(Path.Combine(path, "toc.dat")) ? BackupFormat.Directory : null;
        if (!File.Exists(path) || new FileInfo(path).Length == 0) return null;

        Span<byte> header = stackalloc byte[512];
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            header.Length, FileOptions.SequentialScan);
        var count = stream.Read(header);
        if (count >= 5 && header[..5].SequenceEqual("PGDMP"u8)) return BackupFormat.Custom;
        if (count >= 262 && header.Slice(257, 5).SequenceEqual("ustar"u8)) return BackupFormat.Tar;

        var sample = Encoding.UTF8.GetString(header[..count]).TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
        if (sample.StartsWith("--", StringComparison.Ordinal)
            || sample.StartsWith("SET ", StringComparison.OrdinalIgnoreCase)
            || sample.StartsWith("CREATE ", StringComparison.OrdinalIgnoreCase)
            || sample.StartsWith("COPY ", StringComparison.OrdinalIgnoreCase)
            || sample.StartsWith("\\", StringComparison.Ordinal))
            return BackupFormat.PlainSql;
        return null;
    }

    private static long Size(string path) =>
        File.Exists(path) ? new FileInfo(path).Length : 0;

    private static string? HeaderValue(IEnumerable<string> lines, string name)
    {
        var line = lines.FirstOrDefault(x =>
            x.TrimStart().TrimStart(';').TrimStart()
                .StartsWith(name, StringComparison.OrdinalIgnoreCase));
        if (line is null) return null;
        var index = line.IndexOf(name, StringComparison.OrdinalIgnoreCase);
        return line[(index + name.Length)..].Trim();
    }

    private static BackupInspectionResult Invalid(
        BackupFormat format,
        long size,
        string warning,
        BackupRestoreFailureCategory category) =>
        new(format, null, null, size, 0, [], false, warning, category);
}

public static class BackupSafetyValidator
{
    public static string ValidateDestination(
        string path,
        BackupFormat format,
        bool allowOverwrite = false)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A destination is required.");
        if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            throw new ArgumentException("The destination contains invalid path characters.");

        var full = Path.GetFullPath(path);
        var parent = Path.GetDirectoryName(full)
            ?? throw new ArgumentException("The destination parent directory is invalid.");
        if (!Directory.Exists(parent))
            throw new DirectoryNotFoundException("The destination parent directory does not exist.");
        if ((File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Backup destinations below filesystem reparse points are not supported.");
        if ((File.Exists(full) || Directory.Exists(full))
            && (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Backup destination filesystem redirections are not supported.");

        if (format == BackupFormat.Directory)
        {
            if (File.Exists(full)) throw new IOException("Directory backup destination is an existing file.");
            if (Directory.Exists(full))
                throw new IOException("Atomic directory backup destination must not already exist.");
        }
        else if (Directory.Exists(full))
            throw new IOException("File backup destination is an existing directory.");
        else if (File.Exists(full) && !allowOverwrite)
            throw new IOException("Backup destination already exists.");

        ProbeWritable(parent);
        return full;
    }

    public static void VerifyOutput(string path, BackupFormat format)
    {
        if (format == BackupFormat.Directory)
        {
            if (!Directory.Exists(path) || !File.Exists(Path.Combine(path, "toc.dat")))
                throw new IOException("The directory backup is missing its table of contents.");
            if (!Directory.EnumerateFileSystemEntries(path).Any())
                throw new IOException("The backup output directory is empty.");
            return;
        }
        if (!File.Exists(path)) throw new IOException("The expected backup output was not found.");
        if (new FileInfo(path).Length == 0) throw new IOException("The backup output is empty.");
        if (BackupInspectionService.DetectFormat(path) != format)
            throw new IOException($"The backup output does not match the requested {format} format.");
    }

    private static void ProbeWritable(string directory)
    {
        var probe = Path.Combine(directory, $".pms-write-{Guid.NewGuid():N}.tmp");
        try
        {
            using var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 1, FileOptions.DeleteOnClose);
            stream.WriteByte(0);
        }
        catch (UnauthorizedAccessException)
        {
            throw new BackupRestoreException(BackupRestoreFailureCategory.DestinationNotWritable,
                "The destination directory is not writable.");
        }
        finally
        {
            try { File.Delete(probe); } catch { }
        }
    }
}

public static class PostgreSqlToolVersionParser
{
    private static readonly Regex VersionPattern =
        new(@"(?:PostgreSQL|pg_dump|pg_restore|psql)[^0-9]*(\d+)(?:\.\d+)?",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static int? Major(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var match = VersionPattern.Match(text);
        return match.Success && int.TryParse(match.Groups[1].Value, out var major) && major is >= 7 and <= 999
            ? major : null;
    }
}
