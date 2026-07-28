using System.Diagnostics;
using System.Text;

namespace PostgreManagementStudio.Application;

public sealed record PostgreSqlToolInfo(string Path, string Version);
public sealed record BackupInspectionResult(BackupFormat Format, string? ServerVersion, string? SourceDatabase, long SizeBytes, int ObjectCount, IReadOnlyList<string> Items, bool IsValid, string? Warning);
public sealed record BackupOperationRecord(string Operation, string Server, string Database, string Path, BackupFormat Format, DateTimeOffset Started, DateTimeOffset Completed, TimeSpan Duration, string Result, string ToolVersion, int ExitCode, int WarningCount, string ErrorSummary);
public sealed class BackupOperationHistoryService(int maximumEntries = 100)
{ private readonly LinkedList<BackupOperationRecord> _entries = new(); public IReadOnlyList<BackupOperationRecord> Entries => _entries.ToArray(); public void Add(BackupOperationRecord record) { _entries.AddFirst(record); while (_entries.Count > Math.Max(1, maximumEntries)) _entries.RemoveLast(); } public void ClearCompleted() { foreach (var item in _entries.Where(x => x.Result is "Completed" or "Cancelled").ToArray()) _entries.Remove(item); } }
public sealed class TemporaryCredentialService
{
    public async Task<(string Path, IReadOnlyDictionary<string, string?> Environment)> CreateAsync(DatabaseConnection connection, CancellationToken cancellationToken = default)
    { var path = Path.Combine(Path.GetTempPath(), $"pms-pgpass-{Guid.NewGuid():N}"); var content = $"{Escape(connection.Host)}:{connection.Port}:{Escape(connection.Database)}:{Escape(connection.Username)}:{Escape(connection.Password ?? "")}\n"; await File.WriteAllTextAsync(path, content, Encoding.UTF8, cancellationToken); try { File.SetAttributes(path, FileAttributes.Hidden); } catch { } return (path, new Dictionary<string, string?> { ["PGPASSFILE"] = path }); }
    public void Delete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }
    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace(":", "\\:");
}
public sealed class BackupInspectionService(IExternalProcessRunner runner)
{
    public async Task<BackupInspectionResult> InspectAsync(string path, BackupFormat format, PostgreSqlTools tools, CancellationToken cancellationToken = default)
    { if (format == BackupFormat.PlainSql) { var info = new FileInfo(path); return new(format, null, null, info.Exists ? info.Length : 0, 0, Array.Empty<string>(), info.Exists && info.Length > 0, "Plain SQL cannot provide archive-level object selection."); } var request = new ProcessExecutionRequest(tools.PgRestore, new[] { "--list", path }); var result = await runner.RunAsync(request, cancellationToken: cancellationToken); var items = result.Output.Where(x => !x.IsError).Select(x => x.Line).Where(x => x.Length > 0).ToArray(); var file = new FileInfo(path); return new(format, null, null, file.Exists ? file.Length : 0, items.Length, items, result.ExitCode == 0 && file.Exists, result.ExitCode == 0 ? null : string.Join(Environment.NewLine, result.Output.Where(x => x.IsError).Select(x => x.Line))); }
}
public static class BackupSafetyValidator
{
    public static void ValidateDestination(string path, BackupFormat format, bool allowOverwrite = false) { if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A destination is required."); var full = Path.GetFullPath(path); if (full.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Backup destinations inside the temporary directory are not allowed."); if (format == BackupFormat.Directory) { if (File.Exists(full)) throw new IOException("Directory backup destination is an existing file."); if (Directory.Exists(full) && Directory.EnumerateFileSystemEntries(full).Any() && !allowOverwrite) throw new IOException("Directory backup destination is not empty."); } else if (File.Exists(full) && !allowOverwrite) throw new IOException("Backup destination already exists."); }
    public static void VerifyOutput(string path, BackupFormat format) { if (format == BackupFormat.Directory ? !Directory.Exists(path) : !File.Exists(path)) throw new IOException("The backup process completed but the expected output was not found."); if (format != BackupFormat.Directory && new FileInfo(path).Length == 0) throw new IOException("The backup output is empty."); }
}
public static class PostgreSqlToolVersionParser { public static int? Major(string text) { var match = System.Text.RegularExpressions.Regex.Match(text, @"(?:PostgreSQL|pg_dump|pg_restore|psql)[^0-9]*(\d+)"); return match.Success && int.TryParse(match.Groups[1].Value, out var major) ? major : null; } }
