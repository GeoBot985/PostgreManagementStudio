using System.Diagnostics;
using System.Text;

namespace PostgreManagementStudio.Application;

public enum BackupFormat { Custom, PlainSql, Directory }
public sealed record DatabaseConnection(string Host, int Port, string Database, string Username, string? Password = null)
{
    public static DatabaseConnection FromConnectionString(string value)
    {
        var b = new System.Data.Common.DbConnectionStringBuilder { ConnectionString = value };
        string Get(string name, string fallback = "") => b.TryGetValue(name, out var v) ? Convert.ToString(v) ?? fallback : fallback;
        return new(Get("Host", "localhost"), int.TryParse(Get("Port", "5432"), out var p) ? p : 5432, Get("Database", "postgres"), Get("Username", Get("User ID")), Get("Password"));
    }
}
public sealed record BackupOptions(DatabaseConnection Connection, string Destination, BackupFormat Format = BackupFormat.Custom, bool IncludeLargeObjects = true, bool IncludeOwner = true, bool IncludePrivileges = true, bool Verbose = false, bool CreateDatabase = false, bool DataOnly = false, bool SchemaOnly = false, int? CompressionLevel = null);
public sealed record RestoreOptions(DatabaseConnection Connection, string Source, BackupFormat Format, bool Clean = false, bool CreateDatabase = false, bool DataOnly = false, bool SchemaOnly = false, bool NoOwner = false, bool NoPrivileges = false, bool ExitOnError = true, bool SingleTransaction = false, bool Verbose = false, int? Jobs = null);
public sealed record ProcessOutputEntry(bool IsError, string Line);
public sealed record ProcessExecutionRequest(string FileName, IReadOnlyList<string> Arguments, string? WorkingDirectory = null, IReadOnlyDictionary<string, string?>? Environment = null);
public sealed record ProcessExecutionResult(int ExitCode, IReadOnlyList<ProcessOutputEntry> Output, bool Cancelled);
public interface IExternalProcessRunner { Task<ProcessExecutionResult> RunAsync(ProcessExecutionRequest request, IProgress<ProcessOutputEntry>? progress = null, CancellationToken cancellationToken = default); }
public sealed class ExternalProcessRunner : IExternalProcessRunner
{
    public async Task<ProcessExecutionResult> RunAsync(ProcessExecutionRequest request, IProgress<ProcessOutputEntry>? progress = null, CancellationToken cancellationToken = default)
    {
        var output = new List<ProcessOutputEntry>(); using var process = new Process { StartInfo = new ProcessStartInfo(request.FileName) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, WorkingDirectory = request.WorkingDirectory ?? Environment.CurrentDirectory } };
        foreach (var arg in request.Arguments) process.StartInfo.ArgumentList.Add(arg); if (request.Environment is not null) foreach (var item in request.Environment) process.StartInfo.Environment[item.Key] = item.Value;
        process.Start(); var stdout = ReadAsync(process.StandardOutput, false, output, progress); var stderr = ReadAsync(process.StandardError, true, output, progress);
        try { await process.WaitForExitAsync(cancellationToken); await Task.WhenAll(stdout, stderr); return new(process.ExitCode, output, false); }
        catch (OperationCanceledException) { try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { } await Task.WhenAll(stdout, stderr); return new(-1, output, true); }
    }
    private static async Task ReadAsync(StreamReader reader, bool error, List<ProcessOutputEntry> output, IProgress<ProcessOutputEntry>? progress) { while (await reader.ReadLineAsync() is { } line) { var entry = new ProcessOutputEntry(error, line); lock (output) output.Add(entry); progress?.Report(entry); } }
}
public sealed record PostgreSqlTools(string PgDump, string PgRestore, string Psql);
public sealed class PostgreSqlToolLocator
{
    public PostgreSqlTools? Locate(string? configuredDirectory = null)
    { var paths = new List<string>(); if (!string.IsNullOrWhiteSpace(configuredDirectory)) paths.Add(configuredDirectory); paths.Add(AppContext.BaseDirectory); var path = Environment.GetEnvironmentVariable("PATH") ?? ""; paths.AddRange(path.Split(Path.PathSeparator)); for (var major = 18; major >= 12; major--) paths.Add($@"C:\Program Files\PostgreSQL\{major}\bin"); string? Find(string name) => paths.Select(p => Path.Combine(p, name + ".exe")).FirstOrDefault(File.Exists) ?? paths.Select(p => Path.Combine(p, name)).FirstOrDefault(File.Exists); var dump = Find("pg_dump"); var restore = Find("pg_restore"); var psql = Find("psql"); return dump is not null && restore is not null && psql is not null ? new(dump, restore, psql) : null; }
}
public static class BackupCommandBuilder
{
    public static ProcessExecutionRequest Build(BackupOptions o, PostgreSqlTools tools)
    { Validate(o); var args = new List<string> { "--host", o.Connection.Host, "--port", o.Connection.Port.ToString(), "--username", o.Connection.Username, "--format", o.Format switch { BackupFormat.Custom => "custom", BackupFormat.PlainSql => "plain", _ => "directory" }, "--file", o.Destination }; if (!o.IncludeLargeObjects) args.Add("--no-blobs"); if (!o.IncludeOwner) args.Add("--no-owner"); if (!o.IncludePrivileges) args.Add("--no-privileges"); if (o.Verbose) args.Add("--verbose"); if (o.CreateDatabase) args.Add("--create"); if (o.DataOnly) args.Add("--data-only"); if (o.SchemaOnly) args.Add("--schema-only"); if (o.CompressionLevel is { } c) { args.Add("--compress"); args.Add(c.ToString()); } args.Add(o.Connection.Database); return new(tools.PgDump, args, Environment.CurrentDirectory, PasswordEnvironment(o.Connection)); }
    public static string Preview(ProcessExecutionRequest r) => $"{r.FileName} " + string.Join(" ", r.Arguments.Select(Quote));
    private static string Quote(string s) => s.Contains(' ') || s.Contains('"') ? '"' + s.Replace("\"", "\\\"") + '"' : s;
    private static IReadOnlyDictionary<string, string?> PasswordEnvironment(DatabaseConnection c) => string.IsNullOrEmpty(c.Password) ? new Dictionary<string, string?>() : new Dictionary<string, string?> { ["PGPASSWORD"] = c.Password };
    private static void Validate(BackupOptions o) { if (string.IsNullOrWhiteSpace(o.Destination)) throw new ArgumentException("A backup destination is required."); if (o.DataOnly && o.SchemaOnly) throw new ArgumentException("Data-only and schema-only cannot both be selected."); if (o.Format == BackupFormat.PlainSql && o.CompressionLevel is not null) throw new ArgumentException("Compression is unavailable for plain SQL backups."); if (o.Format == BackupFormat.Directory && File.Exists(o.Destination)) throw new ArgumentException("Directory backup destination is an existing file."); }
}
public static class RestoreCommandBuilder
{
    public static ProcessExecutionRequest Build(RestoreOptions o, PostgreSqlTools tools)
    { if (!File.Exists(o.Source) && o.Format != BackupFormat.Directory) throw new FileNotFoundException("Backup source was not found.", o.Source); if (o.DataOnly && o.SchemaOnly) throw new ArgumentException("Data-only and schema-only cannot both be selected."); var executable = o.Format == BackupFormat.PlainSql ? tools.Psql : tools.PgRestore; var args = new List<string>(); if (o.Format == BackupFormat.PlainSql) { args.AddRange(new[] { "--host", o.Connection.Host, "--port", o.Connection.Port.ToString(), "--username", o.Connection.Username, "--dbname", o.Connection.Database }); if (o.ExitOnError) args.Add("--set ON_ERROR_STOP=on"); if (o.SingleTransaction) args.Add("--single-transaction"); args.Add("--file"); args.Add(o.Source); } else { args.AddRange(new[] { "--host", o.Connection.Host, "--port", o.Connection.Port.ToString(), "--username", o.Connection.Username, "--dbname", o.Connection.Database }); if (o.Clean) args.Add("--clean"); if (o.CreateDatabase) args.Add("--create"); if (o.DataOnly) args.Add("--data-only"); if (o.SchemaOnly) args.Add("--schema-only"); if (o.NoOwner) args.Add("--no-owner"); if (o.NoPrivileges) args.Add("--no-privileges"); if (o.ExitOnError) args.Add("--exit-on-error"); if (o.SingleTransaction) args.Add("--single-transaction"); if (o.Verbose) args.Add("--verbose"); if (o.Jobs is { } jobs) { if (jobs < 1) throw new ArgumentOutOfRangeException(nameof(o.Jobs)); args.Add("--jobs"); args.Add(jobs.ToString()); } args.Add(o.Source); } return new(executable, args, Environment.CurrentDirectory, string.IsNullOrEmpty(o.Connection.Password) ? new Dictionary<string, string?>() : new Dictionary<string, string?> { ["PGPASSWORD"] = o.Connection.Password }); }
}
