using System.Globalization;
using System.Text;
using System.Text.Json;

namespace PostgreManagementStudio.Application;

public enum TransferFormat { Csv, Tsv, Json, JsonLines }
public enum ImportMode { Append, Replace, Upsert, IgnoreConflicts }
public enum ImportExecutionMethod { Copy, BatchedParameterisedInsert }
public enum ImportErrorStrategy { StopOnFirstError, ContinueAndCollectRejected }
public sealed record FormatDetection(TransferFormat Format, DelimitedFileSettings Settings, double Confidence, IReadOnlyList<string> Warnings);
public sealed record InferredColumn(string Name, string PostgreSqlType, double Confidence, IReadOnlyList<string> Conflicts);
public sealed record ImportPlan(string SourcePath, string Schema, string Table, IReadOnlyList<ColumnMapping> Mappings, ImportMode Mode, ImportExecutionMethod ExecutionMethod, TransactionMode Transaction, ImportErrorStrategy ErrorStrategy, bool ConfirmDestructive = false);
public sealed record ImportValidationResult(IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings) { public bool IsValid => Errors.Count == 0; }
public sealed record RejectedRow(long SourceRow, IReadOnlyList<string> Values, string? TargetColumn, string Error, string? PostgreSqlErrorCode = null);
public sealed record TransferProgress(long RowsProcessed, long RowsSucceeded, long RowsRejected, long? BytesProcessed, string Phase, TimeSpan Elapsed);

public static class DataFormatDetector
{
    public static FormatDetection Detect(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("The import file was not found.", path);
        using var stream = File.OpenRead(path); using var reader = new StreamReader(stream, Encoding.UTF8, true);
        var first = reader.ReadLine() ?? string.Empty; var trimmed = first.TrimStart();
        if (trimmed.StartsWith("[") || trimmed.StartsWith("{"))
        {
            try { using var doc = JsonDocument.Parse(first); if (trimmed.StartsWith("{")) { var second = reader.ReadLine()?.TrimStart(); if (second?.StartsWith("{") == true) return new(TransferFormat.JsonLines, new(), 0.95, Array.Empty<string>()); } return new(TransferFormat.Json, new(), 0.95, Array.Empty<string>()); }
            catch { if (trimmed.StartsWith("{")) return new(TransferFormat.JsonLines, new(), 0.85, new[] { "JSON Lines detected from the first record." }); }
        }
        var delimiter = new[] { ',', '\t', ';', '|' }.OrderByDescending(x => first.Count(c => c == x)).First();
        var format = delimiter == '\t' ? TransferFormat.Tsv : TransferFormat.Csv;
        return new(format, new(delimiter, Encoding: reader.CurrentEncoding), first.Count(c => c == delimiter) > 0 ? 0.9 : 0.55, first.Count(c => c == delimiter) > 0 ? Array.Empty<string>() : new[] { "No delimiter was confidently detected; verify the format." });
    }
}

public static class DataTypeInferenceService
{
    public static IReadOnlyList<InferredColumn> Infer(IReadOnlyList<SourceColumn> columns)
        => columns.Select(c => new InferredColumn(c.Name, InferType(c.Sample, out var confidence, out var conflict), confidence, conflict is null ? Array.Empty<string>() : new[] { conflict })).ToArray();
    private static string InferType(string sample, out double confidence, out string? conflict)
    {
        conflict = null; confidence = 0.5; if (string.IsNullOrWhiteSpace(sample)) return "text";
        if (bool.TryParse(sample, out _)) { confidence = 0.95; return "boolean"; }
        if (Guid.TryParse(sample, out _)) { confidence = 0.9; return "uuid"; }
        if (sample.Length > 1 && sample[0] == '0' && sample.All(char.IsDigit)) { conflict = "Leading zeroes may be significant."; return "text"; }
        if (long.TryParse(sample, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer)) { confidence = integer is >= int.MinValue and <= int.MaxValue ? 0.85 : 0.75; return integer is >= int.MinValue and <= int.MaxValue ? "integer" : "bigint"; }
        if (decimal.TryParse(sample, NumberStyles.Number, CultureInfo.InvariantCulture, out _)) { confidence = 0.75; return "numeric"; }
        if (DateOnly.TryParse(sample, CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) { confidence = 0.8; return "date"; }
        return "text";
    }
}

public static class ImportPlanValidator
{
    public static ImportValidationResult Validate(ImportPlan plan, IReadOnlyList<DestinationColumn> columns)
    {
        var errors = new List<string>(); var warnings = new List<string>();
        if (!File.Exists(plan.SourcePath)) errors.Add("The source file does not exist or is not readable.");
        if (string.IsNullOrWhiteSpace(plan.Schema) || string.IsNullOrWhiteSpace(plan.Table)) errors.Add("A target schema and table are required.");
        try { ImportMappingService.Validate(plan.Mappings, columns); } catch (ArgumentException ex) { errors.Add(ex.Message); }
        if (plan.Mode == ImportMode.Replace && !plan.ConfirmDestructive) errors.Add("Replace mode requires explicit destructive-action confirmation.");
        if (plan.Mode is ImportMode.Upsert or ImportMode.IgnoreConflicts && plan.ExecutionMethod == ImportExecutionMethod.Copy) warnings.Add("COPY cannot provide conflict handling; use batched parameterised inserts.");
        if (plan.ErrorStrategy == ImportErrorStrategy.ContinueAndCollectRejected && plan.ExecutionMethod == ImportExecutionMethod.Copy) errors.Add("Continue-and-collect-rejected mode is incompatible with atomic COPY.");
        return new(errors, warnings);
    }
}

public static class JsonImportReader
{
    public static async IAsyncEnumerable<string[]> ReadAsync(string path, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(path);
        await foreach (var row in JsonSerializer.DeserializeAsyncEnumerable<JsonElement>(stream, cancellationToken: cancellationToken))
        { cancellationToken.ThrowIfCancellationRequested(); if (row.ValueKind != JsonValueKind.Object) throw new FormatException("Each JSON record must be an object."); yield return row.EnumerateObject().Select(x => x.Value.ValueKind == JsonValueKind.Null ? "\\N" : x.Value.ToString()).ToArray(); }
    }
}
