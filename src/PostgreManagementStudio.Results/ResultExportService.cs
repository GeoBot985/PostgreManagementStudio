using System.Globalization;
using System.Text;
using System.Text.Json;
using PostgreManagementStudio.Core;

namespace PostgreManagementStudio.Results;

public enum ResultExportFormat { Csv, Tsv, Json, SqlInsert }
public enum ResultExportScope { EntireResult, SelectedRows, SelectedCells }
public sealed record ResultExportOptions(bool IncludeHeaders = true, string Delimiter = ",", string LineEnding = "\r\n", string NullText = "", bool ProtectSpreadsheetFormulas = true, bool JsonArrayLayout = false, string TargetSchema = "public", string TargetTable = "exported_results", int RowsPerInsert = 100, bool IncludeTransaction = true, Encoding? Encoding = null);
public sealed record ResultExportRequest(IResultSetStore ResultSet, ResultSelection? Selection, ResultExportFormat Format, ResultExportScope Scope, string DestinationPath, ResultExportOptions Options);
public sealed record ResultExportProgress(long RowsWritten, long TotalRows, string Phase);
public sealed record ResultExportOutcome(long RowsWritten, long ColumnsWritten, long BytesWritten, TimeSpan Duration, string Path, bool Completed, bool Cancelled);
public interface IResultExportService { Task<ResultExportOutcome> ExportAsync(ResultExportRequest request, IProgress<ResultExportProgress>? progress = null, CancellationToken cancellationToken = default); }

public sealed class ResultExportService : IResultExportService
{
    public async Task<ResultExportOutcome> ExportAsync(ResultExportRequest request, IProgress<ResultExportProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request); Validate(request); var started = DateTimeOffset.UtcNow; var full = Path.GetFullPath(request.DestinationPath); Directory.CreateDirectory(Path.GetDirectoryName(full)!); var temp = full + ".pms-export-" + Guid.NewGuid().ToString("N") + ".tmp"; var rows = 0L;
        try
        {
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var writer = new StreamWriter(stream, request.Options.Encoding ?? new UTF8Encoding(false), 64 * 1024, leaveOpen: false))
            {
                rows = request.Format switch { ResultExportFormat.Json => await WriteJson(request, writer, progress, cancellationToken), ResultExportFormat.SqlInsert => await WriteSql(request, writer, progress, cancellationToken), _ => await WriteDelimited(request, writer, progress, cancellationToken) };
                await writer.FlushAsync(cancellationToken);
            }
            File.Move(temp, full, true); return new ResultExportOutcome(rows, request.Selection?.ColumnCount ?? request.ResultSet.Schema.Columns.Count, new FileInfo(full).Length, DateTimeOffset.UtcNow - started, full, true, false);
        }
        catch (OperationCanceledException) { return new ResultExportOutcome(rows, request.ResultSet.Schema.Columns.Count, 0, DateTimeOffset.UtcNow - started, full, false, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    private static async Task<long> WriteDelimited(ResultExportRequest request, StreamWriter writer, IProgress<ResultExportProgress>? progress, CancellationToken token)
    { var (start, end, first, count) = Range(request); if (request.Options.IncludeHeaders) await writer.WriteAsync(string.Join(request.Options.Delimiter, Enumerable.Range(first, count).Select(i => Csv(request.ResultSet.Schema.Columns[i].Name, request.Options))) + request.Options.LineEnding); var rows = 0L; await foreach (var row in Rows(request.ResultSet, start, end, token)) { var values = Enumerable.Range(first, count).Select(i => DelimitedValue(row.Cells[i], request.ResultSet.Schema.Columns[i], request.Options)).ToArray(); await writer.WriteAsync(string.Join(request.Options.Delimiter, values) + request.Options.LineEnding); progress?.Report(new(rows, end - start + 1, "Writing delimited data")); rows++; } return rows; }
    private static async Task<long> WriteJson(ResultExportRequest request, StreamWriter writer, IProgress<ResultExportProgress>? progress, CancellationToken token)
    { var (start, end, first, count) = Range(request); await writer.WriteAsync(request.Options.JsonArrayLayout ? "{\"columns\":[" + string.Join(',', Enumerable.Range(first, count).Select(i => JsonSerializer.Serialize(request.ResultSet.Schema.Columns[i].Name))) + "],\"rows\":[" : "["); var names = UniqueNames(Enumerable.Range(first, count).Select(i => request.ResultSet.Schema.Columns[i].Name)); var rows = 0L; var firstRow = true; await foreach (var row in Rows(request.ResultSet, start, end, token)) { if (!firstRow) await writer.WriteAsync(","); firstRow = false; var values = Enumerable.Range(first, count).Select(i => JsonValue(row.Cells[i].Value, row.Cells[i].IsNull)).ToArray(); await writer.WriteAsync(request.Options.JsonArrayLayout ? "[" + string.Join(',', values) + "]" : "{" + string.Join(',', names.Zip(values).Select(x => JsonSerializer.Serialize(x.First) + ":" + x.Second)) + "}"); progress?.Report(new(rows, end - start + 1, "Writing JSON")); rows++; } await writer.WriteAsync(request.Options.JsonArrayLayout ? "]}" : "]"); return rows; }
    private static async Task<long> WriteSql(ResultExportRequest request, StreamWriter writer, IProgress<ResultExportProgress>? progress, CancellationToken token)
    { var (start, end, first, count) = Range(request); if (request.Options.IncludeTransaction) await writer.WriteAsync("BEGIN;\r\n"); var columns = string.Join(", ", Enumerable.Range(first, count).Select(i => Quote(request.ResultSet.Schema.Columns[i].Name))); var batch = new List<string>(); var rows = 0L; await foreach (var row in Rows(request.ResultSet, start, end, token)) { batch.Add("(" + string.Join(", ", Enumerable.Range(first, count).Select(i => SqlValue(row.Cells[i]))) + ")"); rows++; if (batch.Count == request.Options.RowsPerInsert) { await writer.WriteAsync($"INSERT INTO {Quote(request.Options.TargetSchema)}.{Quote(request.Options.TargetTable)} ({columns}) VALUES\r\n" + string.Join(",\r\n", batch) + ";\r\n"); batch.Clear(); } progress?.Report(new(rows, end - start + 1, "Writing SQL")); } if (batch.Count > 0) await writer.WriteAsync($"INSERT INTO {Quote(request.Options.TargetSchema)}.{Quote(request.Options.TargetTable)} ({columns}) VALUES\r\n" + string.Join(",\r\n", batch) + ";\r\n"); if (request.Options.IncludeTransaction) await writer.WriteAsync("COMMIT;\r\n"); return rows; }
    private static async IAsyncEnumerable<ResultRow> Rows(IResultSetStore store, long start, long end, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token) { for (var pos = start; pos <= end; pos += 256) { var batch = await store.GetRowsAsync(pos, (int)Math.Min(256, end - pos + 1), token); foreach (var row in batch) yield return row; } }
    private static (long start, long end, int first, int count) Range(ResultExportRequest r) { var s = r.Selection ?? new ResultSelection(0, Math.Max(0, r.ResultSet.LoadedRowCount - 1), 0, r.ResultSet.Schema.Columns.Count - 1); if (r.ResultSet.LoadedRowCount == 0) return (0, -1, s.StartColumnIndex, s.ColumnCount); if (s.EndRowIndex >= r.ResultSet.LoadedRowCount) throw new ArgumentOutOfRangeException(nameof(r.Selection)); return (s.StartRowIndex, s.EndRowIndex, s.StartColumnIndex, s.ColumnCount); }
    private static string DelimitedValue(ResultCell cell, ResultColumn column, ResultExportOptions o) { if (cell.IsNull) return o.NullText; var value = new DefaultResultValueFormatter().FormatForSerialization(cell, column, new(o.NullText, o.LineEnding, false)); if (o.ProtectSpreadsheetFormulas && value.Length > 0 && "=+-@".Contains(value[0]) && cell.Value is string) value = "'" + value; return Csv(value, o); }
    private static string Csv(string value, ResultExportOptions o) { var quote = o.OptionsQuote(); return value.Contains(o.Delimiter) || value.Contains(quote) || value.Contains('\r') || value.Contains('\n') || value.Trim() != value ? quote + value.Replace(quote, quote + quote) + quote : value; }
    private static string JsonValue(object? value, bool isNull) => isNull ? "null" : JsonSerializer.Serialize(value, new JsonSerializerOptions { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals });
    private static string[] UniqueNames(IEnumerable<string> input) { var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase); return input.Select(n => { var candidate = n; var i = 2; while (!used.Add(candidate)) candidate = n + "_" + i++; return candidate; }).ToArray(); }
    private static string Quote(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
    private static string SqlValue(ResultCell cell) { if (cell.IsNull) return "NULL"; if (cell.Value is bool b) return b ? "TRUE" : "FALSE"; if (cell.Value is byte[] bytes) return "'\\x" + Convert.ToHexString(bytes) + "'::bytea"; if (cell.Value is sbyte or byte or short or ushort or int or uint or long or ulong or decimal or float or double) return Convert.ToString(cell.Value, CultureInfo.InvariantCulture)!; var text = Convert.ToString(cell.Value, CultureInfo.InvariantCulture) ?? string.Empty; return "'" + text.Replace("'", "''").Replace("\\", "\\\\") + "'"; }
    private static void Validate(ResultExportRequest r) { if (string.IsNullOrWhiteSpace(r.DestinationPath)) throw new ArgumentException("Destination path is required."); if (r.Options.RowsPerInsert <= 0) throw new ArgumentOutOfRangeException(nameof(r.Options.RowsPerInsert)); if (string.IsNullOrWhiteSpace(r.Options.Delimiter) || r.Options.Delimiter.Contains('"')) throw new ArgumentException("Delimiter is invalid."); }
}
file static class ExportOptionsExtensions { public static string OptionsQuote(this ResultExportOptions _) => "\""; }
