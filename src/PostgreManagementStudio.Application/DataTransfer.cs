using System.Globalization;
using System.Text;

namespace PostgreManagementStudio.Application;

public enum ImportStrategy { Copy, BatchInsert }
public enum ExistingDataMode { Append, Truncate, Delete }
public enum TransactionMode { AllRows, PerBatch, ContinueWithErrors }
public sealed record DelimitedFileSettings(char Delimiter = ',', char Quote = '"', Encoding? Encoding = null, bool HasHeader = true, string NullToken = "\\N", bool TrimWhitespace = false);
public sealed record SourceColumn(int Ordinal, string Name, string Sample);
public sealed record DestinationColumn(
    string Name,
    string PostgreSqlType,
    bool Nullable,
    bool Generated = false,
    bool HasDefault = false,
    bool IdentityAlways = false,
    bool IsPrimaryKey = false,
    string? DefaultExpression = null,
    string? Comment = null,
    bool Included = true)
{
    public bool Writable => !Generated && !IdentityAlways;
}
public sealed record ColumnMapping(int SourceOrdinal, string? DestinationName, bool Included = true);
public sealed record ImportOptions(ImportStrategy Strategy = ImportStrategy.Copy, ExistingDataMode ExistingData = ExistingDataMode.Append, TransactionMode Transaction = TransactionMode.AllRows, int BatchSize = 500, bool ContinueOnError = false, int ErrorLimit = 100, string? RejectedRowsPath = null, bool TrimWhitespace = false);
public sealed record ImportRequest(string SourcePath, string Schema, string Table, IReadOnlyList<ColumnMapping> Mappings, DelimitedFileSettings FileSettings, ImportOptions Options, IReadOnlyList<DestinationColumn> DestinationColumns, bool CreateNewTable = false, string? CreateTableSql = null, IReadOnlyDictionary<int, ImportColumnRule>? ColumnRules = null);
public sealed record ImportProgress(long RowsRead, long RowsWritten, long RowsRejected, string Phase, long BytesProcessed = 0, TimeSpan? Elapsed = null, int CurrentBatch = 0, bool CancellationRequested = false);
public sealed record ImportResult(string Status, long RowsRead, long RowsWritten, long RowsRejected, TimeSpan Elapsed, IReadOnlyList<string> Errors, long RowsSkipped = 0, bool PartialCommit = false, bool NewTableCreated = false, string? RejectedRowsPath = null, IReadOnlyList<string>? Warnings = null);
public static class DelimitedFileDetector { public static DelimitedFileSettings Detect(string path) { using var reader = new StreamReader(path, Encoding.UTF8, true); var line = reader.ReadLine() ?? ""; var choices = new[] { ',', '\t', ';', '|' }; return new(choices.OrderByDescending(c => line.Count(x => x == c)).First(), Encoding: reader.CurrentEncoding); } }
public sealed class DelimitedFileReader
{
    public IEnumerable<string[]> Read(string path, DelimitedFileSettings settings, CancellationToken cancellationToken = default)
    { using var reader = new StreamReader(path, settings.Encoding ?? Encoding.UTF8, true); var fields = new List<string>(); var value = new StringBuilder(); var quoted = false; var first = true; while (true) { cancellationToken.ThrowIfCancellationRequested(); var ch = reader.Read(); if (ch < 0) { if (value.Length > 0 || fields.Count > 0) { fields.Add(Normalize(value.ToString(), settings)); if (!settings.HasHeader || !first) yield return fields.ToArray(); } yield break; } var c = (char)ch; if (c == settings.Quote) { if (quoted && reader.Peek() == settings.Quote) { reader.Read(); value.Append(settings.Quote); } else quoted = !quoted; } else if (c == settings.Delimiter && !quoted) { fields.Add(Normalize(value.ToString(), settings)); value.Clear(); } else if ((c == '\r' || c == '\n') && !quoted) { if (c == '\r' && reader.Peek() == '\n') reader.Read(); fields.Add(Normalize(value.ToString(), settings)); value.Clear(); if (!settings.HasHeader || !first) yield return fields.ToArray(); first = false; fields = new(); } else value.Append(c); } }
    private static string Normalize(string value, DelimitedFileSettings settings) => settings.TrimWhitespace ? value.Trim() : value;
}
public static class ImportMappingService { public static IReadOnlyList<ColumnMapping> Map(IReadOnlyList<SourceColumn> source, IReadOnlyList<DestinationColumn> destination) => source.Select(s => { var target = destination.FirstOrDefault(d => d.Writable && string.Equals(d.Name, s.Name, StringComparison.OrdinalIgnoreCase)); return new ColumnMapping(s.Ordinal, target?.Name, target is not null); }).ToArray(); public static void Validate(IReadOnlyList<ColumnMapping> mappings, IReadOnlyList<DestinationColumn> columns) { var names = mappings.Where(x => x.Included && x.DestinationName is not null).Select(x => x.DestinationName!).ToArray(); if (names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Length) throw new ArgumentException("A destination column cannot receive multiple source columns."); if (columns.Any(x => !x.Writable && names.Contains(x.Name, StringComparer.OrdinalIgnoreCase))) throw new ArgumentException("Generated and identity-always columns cannot be written."); if (columns.Any(x => !x.Nullable && !x.HasDefault && x.Writable && !names.Contains(x.Name, StringComparer.OrdinalIgnoreCase))) throw new ArgumentException("A required destination column is unmapped."); } }
public static class DataValueConverter { public static object? Convert(string value, string type, DelimitedFileSettings settings) { if (value == settings.NullToken) return null; if (value.Length == 0) return ""; return type.ToLowerInvariant() switch { "boolean" or "bool" => value.ToLowerInvariant() switch { "true" or "t" or "1" or "yes" => true, "false" or "f" or "0" or "no" => false, _ => throw new FormatException($"Invalid boolean: {value}") }, "smallint" or "integer" or "bigint" => long.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture), "numeric" or "decimal" or "real" or "double precision" => decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture), "date" => DateOnly.Parse(value, CultureInfo.InvariantCulture), _ when type.StartsWith("timestamp", StringComparison.OrdinalIgnoreCase) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture), "uuid" => Guid.Parse(value), "json" or "jsonb" => value, "bytea" => value.StartsWith("\\x", StringComparison.OrdinalIgnoreCase) ? System.Convert.FromHexString(value[2..]) : value, _ => value }; } }
public sealed class DelimitedFileWriter { public async Task<long> WriteAsync<T>(IAsyncEnumerable<IReadOnlyList<T>> rows, string path, Func<T, string?> format, char delimiter = ',', bool header = false, string? headerLine = null, CancellationToken cancellationToken = default) { await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 65536, true); await using var writer = new StreamWriter(stream, Encoding.UTF8); if (header && headerLine is not null) await writer.WriteLineAsync(headerLine); long count = 0; await foreach (var row in rows.WithCancellation(cancellationToken)) { await writer.WriteLineAsync(string.Join(delimiter, row.Select(x => Quote(format(x) ?? "")))); count++; } return count; } private static string Quote(string value) => '"' + value.Replace("\"", "\"\"").Replace("\r", " ").Replace("\n", " ") + '"'; }
