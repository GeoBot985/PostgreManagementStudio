using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

namespace PostgreManagementStudio.Application;

public enum DelimitedEmptyValueMode { PreserveEmptyString, ConvertToNull }
public enum InvalidValueMode { RejectRow, SubstituteNull }
public enum ImportDestinationMode { ExistingTable, CreateNewTable }
public enum ImportMappingMode { ExactName, CaseInsensitiveName, Ordinal }

public sealed record DelimitedFormatOptions(
    char Delimiter = ',',
    char? Quote = '"',
    bool HasHeader = true,
    bool TrimUnquotedWhitespace = false,
    bool AllowMultilineFields = true,
    string NullMarker = "\\N",
    DelimitedEmptyValueMode EmptyValueMode = DelimitedEmptyValueMode.PreserveEmptyString,
    string? CommentPrefix = null,
    int SkipRows = 0,
    int MaximumColumns = 4096,
    int MaximumFieldCharacters = 16 * 1024 * 1024);

public sealed record FileInspection(
    string Path,
    long SizeBytes,
    Encoding Encoding,
    string EncodingLabel,
    bool EncodingWasDetected,
    string LineEnding,
    long? EstimatedRows,
    DelimitedFormatOptions Format,
    double DelimiterConfidence,
    IReadOnlyList<string> Warnings);

public sealed record DelimitedField(
    string Value,
    bool WasQuoted,
    bool IsExplicitNull,
    bool IsEmpty,
    bool IsWhitespaceOnly);

public sealed record DelimitedRecord(
    long SourceRow,
    IReadOnlyList<DelimitedField> Fields,
    string? Error = null,
    long? PhysicalLineStart = null,
    long? PhysicalLineEnd = null)
{
    public bool IsMalformed => Error is not null;
}

public sealed record DelimitedPreview(
    FileInspection Inspection,
    IReadOnlyList<string> Headers,
    IReadOnlyList<DelimitedRecord> Records,
    IReadOnlyList<string> Warnings,
    bool IsBoundedSample);

public sealed record ImportColumnRule(
    bool TrimWhitespace = false,
    bool EmptyStringBecomesNull = false,
    string? NullMarker = null,
    string? DateFormat = null,
    string? TimeFormat = null,
    string? TimestampFormat = null,
    string DecimalSeparator = ".",
    string? ThousandsSeparator = null,
    IReadOnlyList<string>? TrueValues = null,
    IReadOnlyList<string>? FalseValues = null,
    bool StripCurrencySymbol = false,
    bool ParenthesesAreNegative = false,
    bool AllowExponent = true,
    InvalidValueMode InvalidValueMode = InvalidValueMode.RejectRow,
    string? TimeZoneAssumption = null);

public sealed record TypeInferenceProposal(
    string Name,
    string PostgreSqlType,
    double Confidence,
    IReadOnlyList<string> Warnings,
    int ValuesSampled);

public sealed record ImportPreflightRequest(
    string SourcePath,
    string Schema,
    string Table,
    ImportDestinationMode DestinationMode,
    IReadOnlyList<ColumnMapping> Mappings,
    IReadOnlyList<DestinationColumn> DestinationColumns,
    ImportStrategy Strategy,
    TransactionMode Transaction,
    bool CollectErrors,
    bool HasCreatePermission = true,
    bool HasInsertPermission = true);

public sealed record TransferRelationSource(
    string Schema,
    string Name,
    string ObjectType,
    bool CanImport,
    bool CanExport,
    string QualifiedName);

public sealed record TransferDestinationMetadata(
    IReadOnlyList<string> Schemas,
    IReadOnlyList<TransferRelationSource> Relations,
    IReadOnlyList<DestinationColumn> Columns,
    bool HasCreatePermission,
    bool HasInsertPermission);

public interface ITransferMetadataProvider
{
    Task<TransferDestinationMetadata> LoadAsync(
        string connectionString,
        string database,
        string? schema = null,
        string? relation = null,
        CancellationToken cancellationToken = default);
}

public enum RelationExportFormat { Csv, Tsv, Json, JsonLines, SqlInsert }

public sealed record RelationExportOptions(
    IReadOnlyList<string> Columns,
    IReadOnlyList<string>? OutputHeaders = null,
    long? RowLimit = null,
    string? WherePredicate = null,
    string? OrderBy = null,
    bool IncludeHeaders = true,
    char Delimiter = ',',
    char Quote = '"',
    string NullText = "",
    string LineEnding = "\r\n",
    Encoding? Encoding = null,
    bool PrettyJson = false,
    int SqlBatchSize = 100,
    bool IncludeTransaction = true);

public sealed record RelationExportRequest(
    string Schema,
    string Relation,
    RelationExportFormat Format,
    string DestinationPath,
    RelationExportOptions Options);

public sealed record TransferExportProgress(
    long RowsRead,
    long RowsWritten,
    long BytesWritten,
    string Phase,
    TimeSpan Elapsed,
    bool CancellationRequested = false);

public sealed record TransferExportResult(
    string Status,
    string Source,
    string DestinationPath,
    RelationExportFormat Format,
    long RowsWritten,
    long BytesWritten,
    TimeSpan Elapsed,
    bool Completed,
    bool Cancelled,
    bool SourceComplete,
    IReadOnlyList<string> Warnings);

public interface IRelationExportService
{
    Task<TransferExportResult> ExportAsync(
        string connectionString,
        string database,
        RelationExportRequest request,
        IProgress<TransferExportProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public static class TransferEncodingDetector
{
    public static (Encoding Encoding, string Label, bool Detected, IReadOnlyList<string> Warnings)
        Detect(string path)
    {
        using var stream = File.OpenRead(path);
        Span<byte> prefix = stackalloc byte[4];
        var count = stream.Read(prefix);
        if (count >= 3 && prefix[0] == 0xEF && prefix[1] == 0xBB && prefix[2] == 0xBF)
            return (new UTF8Encoding(true, true), "UTF-8 with BOM", true, []);
        if (count >= 2 && prefix[0] == 0xFF && prefix[1] == 0xFE)
            return (new UnicodeEncoding(false, true, true), "UTF-16 little-endian", true, []);
        if (count >= 2 && prefix[0] == 0xFE && prefix[1] == 0xFF)
            return (new UnicodeEncoding(true, true, true), "UTF-16 big-endian", true, []);

        stream.Position = 0;
        var probe = new byte[Math.Min(64 * 1024, checked((int)Math.Min(stream.Length, int.MaxValue)))];
        _ = stream.Read(probe);
        try
        {
            _ = new UTF8Encoding(false, true).GetString(probe);
            return (new UTF8Encoding(false, true), "UTF-8", true, []);
        }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return (Encoding.GetEncoding(1252, EncoderFallback.ExceptionFallback,
                    DecoderFallback.ExceptionFallback),
                "Windows-1252 (best estimate)", false,
                ["The file is not valid UTF-8. Windows-1252 is a best estimate; verify or override the encoding."]);
        }
    }

    public static Encoding FromLabel(string label) => label switch
    {
        "UTF-8 with BOM" => new UTF8Encoding(true, true),
        "UTF-16 little-endian" => new UnicodeEncoding(false, true, true),
        "UTF-16 big-endian" => new UnicodeEncoding(true, true, true),
        "Windows-1252" or "Windows-1252 (best estimate)" => Windows1252(),
        _ => new UTF8Encoding(false, true),
    };

    private static Encoding Windows1252()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1252, EncoderFallback.ExceptionFallback,
            DecoderFallback.ExceptionFallback);
    }
}

public sealed class ProductionDelimitedFileInspector
{
    public async Task<FileInspection> InspectAsync(
        string path,
        Encoding? encodingOverride = null,
        DelimitedFormatOptions? formatOverride = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("The source file was not found.", path);
        var info = new FileInfo(path);
        if (info.Length == 0) throw new InvalidOperationException("The source file is empty.");
        var detected = TransferEncodingDetector.Detect(path);
        var encoding = encodingOverride ?? detected.Encoding;
        var sample = await ReadSampleAsync(path, encoding, cancellationToken).ConfigureAwait(false);
        var lineEnding = sample.Contains("\r\n", StringComparison.Ordinal) ? "CRLF"
            : sample.Contains('\n') ? "LF" : sample.Contains('\r') ? "CR" : "Unknown";
        var format = formatOverride ?? DetectFormat(sample);
        var sampledBytes = Math.Max(1, encoding.GetByteCount(sample));
        var sampledRecords = CountLogicalRecords(sample, format.Quote);
        var isCompleteSample = sampledBytes >= info.Length - encoding.GetPreamble().Length;
        var estimatedRecords = isCompleteSample
            ? sampledRecords
            : Math.Max(1, (long)Math.Round(
                (double)info.Length / sampledBytes * sampledRecords));
        var estimatedRows = Math.Max(0, estimatedRecords - (format.HasHeader ? 1 : 0));
        var confidence = DelimiterConfidence(sample, format.Delimiter);
        var warnings = detected.Warnings.ToList();
        if (lineEnding == "Unknown") warnings.Add("No line ending was detected in the bounded source sample.");
        if (confidence < 0.6) warnings.Add("Delimiter detection confidence is low; verify the format.");
        return new(path, info.Length, encoding, encodingOverride is null ? detected.Label : encoding.WebName,
            encodingOverride is null && detected.Detected, lineEnding, estimatedRows, format, confidence, warnings);
    }

    public async Task<DelimitedPreview> PreviewAsync(
        string path,
        Encoding? encodingOverride = null,
        DelimitedFormatOptions? formatOverride = null,
        int maximumRecords = 200,
        CancellationToken cancellationToken = default)
    {
        if (maximumRecords is < 1 or > 5000)
            throw new ArgumentOutOfRangeException(nameof(maximumRecords));
        var inspection = await InspectAsync(path, encodingOverride, formatOverride, cancellationToken)
            .ConfigureAwait(false);
        var raw = new List<DelimitedRecord>();
        await foreach (var record in ReadAsync(path, inspection.Encoding, inspection.Format,
                           cancellationToken).ConfigureAwait(false))
        {
            raw.Add(record);
            if (raw.Count >= maximumRecords + (inspection.Format.HasHeader ? 1 : 0)) break;
        }
        if (raw.Count == 0) throw new InvalidOperationException("The source contains no readable records.");
        var warnings = inspection.Warnings.ToList();
        var expected = raw.First(record => !record.IsMalformed).Fields.Count;
        foreach (var malformed in raw.Where(record => record.IsMalformed
                || record.Fields.Count != expected).Take(20))
            warnings.Add($"Source row {malformed.SourceRow}: "
                + (malformed.Error ?? $"expected {expected} fields but found {malformed.Fields.Count}."));
        var headerRecord = inspection.Format.HasHeader ? raw[0] : null;
        var headers = headerRecord is null
            ? Enumerable.Range(1, expected).Select(index => $"column_{index}").ToArray()
            : headerRecord.Fields.Select(field => field.Value).ToArray();
        var normalized = HeaderNormalizationService.Normalize(headers, convertSpacesToUnderscores: false);
        if (!headers.SequenceEqual(normalized, StringComparer.Ordinal))
            warnings.Add("Empty or duplicate headers were detected. Proposed unique names are shown.");
        var records = inspection.Format.HasHeader ? raw.Skip(1).ToArray() : raw.ToArray();
        return new(inspection, normalized, records, warnings.Distinct().ToArray(), true);
    }

    public async IAsyncEnumerable<DelimitedRecord> ReadAsync(
        string path,
        Encoding encoding,
        DelimitedFormatOptions format,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(stream, encoding, false, 64 * 1024);
        var fields = new List<DelimitedField>();
        var value = new StringBuilder();
        var buffer = new char[16 * 1024];
        var quoted = false;
        var fieldWasQuoted = false;
        var row = 1L;
        var physicalLine = 1L;
        var recordStartLine = 1L;
        var skipped = 0;
        var pendingCr = false;
        var priorQuotedCr = false;
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            for (var index = 0; index < read; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var character = buffer[index];
                if (pendingCr)
                {
                    pendingCr = false;
                    if (character == '\n') continue;
                }
                if (quoted)
                {
                    if (format.Quote is { } quote && character == quote)
                    {
                        if (index + 1 < read && buffer[index + 1] == quote)
                        {
                            value.Append(quote);
                            index++;
                        }
                        else quoted = false;
                    }
                    else
                    {
                        if (!format.AllowMultilineFields && character is '\r' or '\n')
                            yield return new(row++, Finish(fields, value, fieldWasQuoted, format),
                                "A quoted field crossed a line boundary while multiline fields are disabled.",
                                recordStartLine, physicalLine);
                        else value.Append(character);
                        if (character == '\r')
                        {
                            physicalLine++;
                            priorQuotedCr = true;
                        }
                        else if (character == '\n')
                        {
                            if (!priorQuotedCr) physicalLine++;
                            priorQuotedCr = false;
                        }
                        else priorQuotedCr = false;
                    }
                }
                else if (format.Quote is { } quote && character == quote && value.Length == 0)
                {
                    quoted = true;
                    fieldWasQuoted = true;
                }
                else if (character == format.Delimiter)
                {
                    AddField(fields, value, fieldWasQuoted, format);
                    fieldWasQuoted = false;
                }
                else if (character is '\r' or '\n')
                {
                    if (character == '\r') pendingCr = true;
                    var completed = Finish(fields, value, fieldWasQuoted, format);
                    fieldWasQuoted = false;
                    var recordEndLine = physicalLine;
                    physicalLine++;
                    if (skipped++ < format.SkipRows)
                    {
                        row++;
                        recordStartLine = physicalLine;
                        continue;
                    }
                    if (format.CommentPrefix is { Length: > 0 } prefix
                        && completed.Count > 0 && completed[0].Value.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        row++;
                        recordStartLine = physicalLine;
                        continue;
                    }
                    yield return completed.Count > format.MaximumColumns
                        ? new(row++, completed, $"The row exceeds the {format.MaximumColumns} column limit.",
                            recordStartLine, recordEndLine)
                        : new(row++, completed, null, recordStartLine, recordEndLine);
                    recordStartLine = physicalLine;
                }
                else
                {
                    value.Append(character);
                    if (value.Length > format.MaximumFieldCharacters)
                    {
                        yield return new(row++, Finish(fields, value, fieldWasQuoted, format),
                            $"A field exceeds the {format.MaximumFieldCharacters:N0} character limit.",
                            recordStartLine, physicalLine);
                        fieldWasQuoted = false;
                        quoted = false;
                    }
                }
            }
        }
        if (quoted)
            yield return new(row, Finish(fields, value, fieldWasQuoted, format),
                "The file ended inside a quoted field.", recordStartLine, physicalLine);
        else if (value.Length > 0 || fields.Count > 0)
            yield return new(row, Finish(fields, value, fieldWasQuoted, format),
                null, recordStartLine, physicalLine);
    }

    private static async Task<string> ReadSampleAsync(
        string path, Encoding encoding, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(stream, encoding, false, 64 * 1024);
        var buffer = new char[64 * 1024];
        var count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
        return new string(buffer, 0, count);
    }

    private static DelimitedFormatOptions DetectFormat(string sample)
    {
        var lines = sample.Split(["\r\n", "\n", "\r"], StringSplitOptions.RemoveEmptyEntries)
            .Take(20).ToArray();
        var candidates = new[] { ',', '\t', ';', '|' };
        var delimiter = candidates
            .Select(candidate => new
            {
                Character = candidate,
                Counts = lines.Select(line => CountOutsideQuotes(line, candidate)).ToArray(),
            })
            .OrderByDescending(candidate => candidate.Counts.Length == 0
                || candidate.Counts.Max() == 0 ? 0
                : candidate.Counts.Count(count => count == candidate.Counts[0]) * 100
                  + candidate.Counts.Sum())
            .First().Character;
        var first = lines.FirstOrDefault() ?? string.Empty;
        var firstValues = first.Split(delimiter);
        var secondValues = lines.Skip(1).FirstOrDefault()?.Split(delimiter) ?? [];
        var header = firstValues.Length > 1 && firstValues.Any(value => !LooksLikeData(value))
            && secondValues.Any(LooksLikeData);
        var quote = sample.Contains('"') ? '"' : sample.Contains('\'') ? '\'' : (char?)null;
        return new(delimiter, quote, header);
    }

    private static long CountLogicalRecords(string sample, char? quote)
    {
        if (sample.Length == 0) return 0;
        var records = 0L;
        var quoted = false;
        for (var index = 0; index < sample.Length; index++)
        {
            var character = sample[index];
            if (quote is { } quoteCharacter && character == quoteCharacter)
            {
                if (quoted && index + 1 < sample.Length && sample[index + 1] == quoteCharacter)
                    index++;
                else quoted = !quoted;
                continue;
            }
            if (!quoted && character is '\r' or '\n')
            {
                records++;
                if (character == '\r' && index + 1 < sample.Length && sample[index + 1] == '\n')
                    index++;
            }
        }
        if (sample[^1] is not '\r' and not '\n') records++;
        return records;
    }

    private static double DelimiterConfidence(string sample, char delimiter)
    {
        var counts = sample.Split(["\r\n", "\n", "\r"], StringSplitOptions.RemoveEmptyEntries)
            .Take(20).Select(line => CountOutsideQuotes(line, delimiter)).ToArray();
        if (counts.Length == 0 || counts.Max() == 0) return 0.25;
        var mode = counts.GroupBy(value => value).OrderByDescending(group => group.Count()).First();
        return Math.Min(0.99, 0.5 + 0.49 * mode.Count() / counts.Length);
    }

    private static int CountOutsideQuotes(string line, char delimiter)
    {
        var count = 0;
        var quoted = false;
        for (var index = 0; index < line.Length; index++)
        {
            if (line[index] == '"')
            {
                if (quoted && index + 1 < line.Length && line[index + 1] == '"') index++;
                else quoted = !quoted;
            }
            else if (!quoted && line[index] == delimiter) count++;
        }
        return count;
    }

    private static bool LooksLikeData(string value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
        || decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _)
        || bool.TryParse(value, out _)
        || Guid.TryParse(value, out _)
        || DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    private static IReadOnlyList<DelimitedField> Finish(
        List<DelimitedField> fields, StringBuilder value, bool quoted, DelimitedFormatOptions format)
    {
        AddField(fields, value, quoted, format);
        var completed = fields.ToArray();
        fields.Clear();
        return completed;
    }

    private static void AddField(
        List<DelimitedField> fields, StringBuilder value, bool quoted, DelimitedFormatOptions format)
    {
        var raw = value.ToString();
        value.Clear();
        var normalized = format.TrimUnquotedWhitespace && !quoted ? raw.Trim() : raw;
        fields.Add(new(normalized, quoted,
            normalized.Equals(format.NullMarker, StringComparison.Ordinal),
            normalized.Length == 0,
            normalized.Length > 0 && string.IsNullOrWhiteSpace(normalized)));
    }
}

public static class HeaderNormalizationService
{
    public static IReadOnlyList<string> Normalize(
        IEnumerable<string> headers, bool convertSpacesToUnderscores)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();
        var ordinal = 0;
        foreach (var header in headers)
        {
            ordinal++;
            var name = new string(header.Trim().Where(character => !char.IsControl(character)).ToArray());
            if (convertSpacesToUnderscores)
                name = Regex.Replace(name, @"\s+", "_");
            if (string.IsNullOrWhiteSpace(name)) name = $"column_{ordinal}";
            var candidate = name;
            var suffix = 2;
            while (!used.Add(candidate)) candidate = $"{name}_{suffix++}";
            result.Add(candidate);
        }
        return result;
    }
}

public static class PostgreSqlTransferIdentifier
{
    public static string? Validate(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "An identifier is required.";
        if (value.Any(char.IsControl)) return "PostgreSQL identifiers cannot contain control characters.";
        if (Encoding.UTF8.GetByteCount(value) > 63)
            return "PostgreSQL identifiers are limited to 63 UTF-8 bytes.";
        if (value.IndexOf('\0') >= 0) return "PostgreSQL identifiers cannot contain NUL.";
        return null;
    }
}

public static class PostgreSqlTransferType
{
    private static readonly Regex SafeType = new(
        """^(?:(?:"(?:[^"]|"")*"|[\p{L}_][\p{L}\p{N}_$]*)\.)?(?:"(?:[^"]|"")*"|[\p{L}_][\p{L}\p{N}_$]*)(?:\s+(?:with|without)\s+time\s+zone|\s+precision)?(?:\(\d+(?:\s*,\s*\d+)?\))?(?:\[\])*$""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool IsSafe(string value) => SafeType.IsMatch(value.Trim());
}

public static class ProductionDataTypeInferenceService
{
    private enum Kind
    {
        Empty, Boolean, SmallInteger, Integer, BigInteger, Numeric, Real, Double,
        Date, Time, Timestamp, TimestampTz, Uuid, Text,
    }

    public static IReadOnlyList<TypeInferenceProposal> Infer(
        IReadOnlyList<string> headers,
        IEnumerable<DelimitedRecord> records,
        int maximumSamples = 1000)
    {
        var sample = records.Where(record => !record.IsMalformed).Take(maximumSamples).ToArray();
        return headers.Select((header, ordinal) =>
        {
            var warnings = new List<string>();
            var values = sample.Where(record => ordinal < record.Fields.Count)
                .Select(record => record.Fields[ordinal])
                .Where(field => !field.IsExplicitNull && !field.IsEmpty)
                .Select(field => field.Value).ToArray();
            var kinds = values.Select(value => Classify(value, warnings)).ToArray();
            var type = Promote(kinds, warnings);
            return new TypeInferenceProposal(header, type, Confidence(kinds, type),
                warnings.Distinct().ToArray(), values.Length);
        }).ToArray();
    }

    private static Kind Classify(string value, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(value)) return Kind.Empty;
        if (value.Length > 1 && value[0] == '0' && value.All(char.IsDigit))
        {
            warnings.Add("Leading-zero values were preserved as text.");
            return Kind.Text;
        }
        if (bool.TryParse(value, out _)
            || value.Equals("t", StringComparison.OrdinalIgnoreCase)
            || value.Equals("f", StringComparison.OrdinalIgnoreCase)) return Kind.Boolean;
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
            return integer is >= short.MinValue and <= short.MaxValue ? Kind.SmallInteger
                : integer is >= int.MinValue and <= int.MaxValue ? Kind.Integer : Kind.BigInteger;
        if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
            return Kind.Numeric;
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var floating))
            return float.IsFinite((float)floating) && Math.Abs(floating) <= float.MaxValue
                ? Kind.Real : Kind.Double;
        if (Guid.TryParse(value, out _)) return Kind.Uuid;
        if (Regex.IsMatch(value, @"^\d{4}-\d{2}-\d{2}$")
            && DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out _)) return Kind.Date;
        if (Regex.IsMatch(value, @"^\d{2}:\d{2}(:\d{2}(?:\.\d+)?)?$")
            && TimeOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            return Kind.Time;
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out _)
            && Regex.IsMatch(value, @"(?:Z|[+-]\d{2}:?\d{2})$")) return Kind.TimestampTz;
        if (DateTime.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out _) && value.Contains('T')) return Kind.Timestamp;
        if (Regex.IsMatch(value, @"^\d{1,2}/\d{1,2}/\d{4}$"))
            warnings.Add($"Ambiguous date value '{value}' was preserved as text.");
        return Kind.Text;
    }

    private static string Promote(IReadOnlyList<Kind> kinds, List<string> warnings)
    {
        var meaningful = kinds.Where(kind => kind != Kind.Empty).Distinct().ToArray();
        if (meaningful.Length == 0) return "text";
        if (meaningful.Contains(Kind.Text)) return "text";
        if (meaningful.All(kind => kind is Kind.SmallInteger)) return "smallint";
        if (meaningful.All(kind => kind is Kind.SmallInteger or Kind.Integer)) return "integer";
        if (meaningful.All(kind => kind is Kind.SmallInteger or Kind.Integer or Kind.BigInteger))
            return "bigint";
        if (meaningful.All(kind => kind is Kind.SmallInteger or Kind.Integer or Kind.BigInteger
                or Kind.Numeric)) return "numeric";
        if (meaningful.All(kind => kind is Kind.SmallInteger or Kind.Integer or Kind.BigInteger
                or Kind.Numeric or Kind.Real)) return "real";
        if (meaningful.All(kind => kind is Kind.SmallInteger or Kind.Integer or Kind.BigInteger
                or Kind.Numeric or Kind.Real or Kind.Double)) return "double precision";
        if (meaningful.Length == 1) return meaningful[0] switch
        {
            Kind.Boolean => "boolean",
            Kind.Date => "date",
            Kind.Time => "time",
            Kind.Timestamp => "timestamp without time zone",
            Kind.TimestampTz => "timestamp with time zone",
            Kind.Uuid => "uuid",
            _ => "text",
        };
        warnings.Add("Conflicting sampled values were preserved as text.");
        return "text";
    }

    private static double Confidence(IReadOnlyList<Kind> kinds, string type) =>
        kinds.Count == 0 || type == "text" ? 0.6 : 0.9;
}

public static class ProductionImportMappingService
{
    public static IReadOnlyList<ColumnMapping> Map(
        IReadOnlyList<SourceColumn> source,
        IReadOnlyList<DestinationColumn> destination,
        ImportMappingMode mode)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<ColumnMapping>();
        foreach (var sourceColumn in source.OrderBy(column => column.Ordinal))
        {
            DestinationColumn? target = mode switch
            {
                ImportMappingMode.Ordinal => sourceColumn.Ordinal < destination.Count
                    ? destination[sourceColumn.Ordinal] : null,
                ImportMappingMode.ExactName => destination.FirstOrDefault(column =>
                    column.Name.Equals(sourceColumn.Name, StringComparison.Ordinal)),
                _ => destination.FirstOrDefault(column =>
                    column.Name.Equals(sourceColumn.Name, StringComparison.OrdinalIgnoreCase)),
            };
            if (target is { Writable: true } && used.Add(target.Name))
                result.Add(new(sourceColumn.Ordinal, target.Name));
            else result.Add(new(sourceColumn.Ordinal, null, false));
        }
        return result;
    }
}

public static class ProductionImportPreflight
{
    public static ImportValidationResult Validate(ImportPreflightRequest request)
    {
        var errors = new List<string>();
        var warnings = new List<string>
        {
            "Validation is bounded; unsampled source rows may still contain incompatible values.",
        };
        if (!File.Exists(request.SourcePath)) errors.Add("The source file does not exist.");
        if (PostgreSqlTransferIdentifier.Validate(request.Schema) is { } schemaError)
            errors.Add($"Schema: {schemaError}");
        if (PostgreSqlTransferIdentifier.Validate(request.Table) is { } tableError)
            errors.Add($"Table: {tableError}");
        if (request.DestinationMode == ImportDestinationMode.CreateNewTable
            && !request.HasCreatePermission) errors.Add("CREATE permission is required on the destination schema.");
        if (!request.HasInsertPermission) errors.Add("INSERT permission is required on the destination table.");
        try { ImportMappingService.Validate(request.Mappings, request.DestinationColumns); }
        catch (ArgumentException exception) { errors.Add(exception.Message); }
        var effectiveStrategy = ImportStrategySelector.Select(
            request.Strategy, request.Mappings, request.DestinationColumns);
        if (request.CollectErrors && effectiveStrategy == ImportStrategy.Copy)
            errors.Add("Collect-errors mode requires row-by-row validated import.");
        if (effectiveStrategy != request.Strategy)
            warnings.Add("The selected fast path contains types that are not certified for binary COPY; validated typed batches will be used.");
        if (request.Transaction == TransactionMode.PerBatch)
            warnings.Add("Completed batches remain committed if a later batch fails or is cancelled.");
        return new(errors, warnings);
    }
}

public static class ImportStrategySelector
{
    private static readonly Regex CertifiedBinaryType = new(
        """^(?:bool(?:ean)?|smallint|int2|integer|int4|bigint|int8|numeric(?:\(\d+(?:\s*,\s*\d+)?\))?|decimal(?:\(\d+(?:\s*,\s*\d+)?\))?|real|double\s+precision|date|time(?:\s+without\s+time\s+zone)?|timestamp\s+without\s+time\s+zone|timestamp\s+with\s+time\s+zone|timestamptz|uuid|bytea|text|character\s+varying(?:\(\d+\))?|varchar(?:\(\d+\))?)$""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static ImportStrategy Select(
        ImportStrategy requested,
        IReadOnlyList<ColumnMapping> mappings,
        IReadOnlyList<DestinationColumn> destinationColumns)
    {
        if (requested != ImportStrategy.Copy) return ImportStrategy.BatchInsert;
        var includedTypes = mappings
            .Where(mapping => mapping.Included && mapping.DestinationName is not null)
            .Select(mapping => destinationColumns.Single(column =>
                column.Name.Equals(mapping.DestinationName, StringComparison.Ordinal)).PostgreSqlType)
            .ToArray();
        return includedTypes.All(IsCertifiedForBinaryCopy)
            ? ImportStrategy.Copy
            : ImportStrategy.BatchInsert;
    }

    public static bool IsCertifiedForBinaryCopy(string postgreSqlType) =>
        CertifiedBinaryType.IsMatch(postgreSqlType.Trim());

    public static string DisplayName(ImportStrategy strategy) => strategy switch
    {
        ImportStrategy.Copy => "Fast bulk import using binary COPY",
        _ => "Validated typed import using parameterised batches",
    };
}

public static class NewTableSqlBuilder
{
    public static string Build(
        string schema, string table, IReadOnlyList<DestinationColumn> columns)
    {
        if (columns.Count == 0) throw new ArgumentException("At least one destination column is required.");
        var included = columns.Where(column => column.Included).ToArray();
        if (included.Select(column => column.Name).Distinct(StringComparer.Ordinal).Count()
            != included.Length)
            throw new ArgumentException("New-table column names must be unique.");
        var definitions = included.Select(column =>
        {
            if (PostgreSqlTransferIdentifier.Validate(column.Name) is { } error)
                throw new ArgumentException($"{column.Name}: {error}");
            if (!PostgreSqlTransferType.IsSafe(column.PostgreSqlType))
                throw new ArgumentException(
                    $"{column.Name}: PostgreSQL type syntax is not supported by the import wizard.");
            var definition = $"    {Quote(column.Name)} {column.PostgreSqlType}";
            if (!column.Nullable) definition += " NOT NULL";
            if (column.HasDefault && !string.IsNullOrWhiteSpace(column.DefaultExpression))
                definition += $" DEFAULT {column.DefaultExpression}";
            if (column.IsPrimaryKey) definition += " PRIMARY KEY";
            return definition;
        });
        return $"CREATE TABLE {Quote(schema)}.{Quote(table)}\r\n(\r\n"
            + string.Join(",\r\n", definitions) + "\r\n);";
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
}

public sealed class RejectedRowWriter
{
    public async Task WriteAsync(
        string path,
        IEnumerable<RejectedRow> rows,
        CancellationToken cancellationToken = default)
    {
        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var temp = full + ".pms-rejected-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write,
                             FileShare.None, 64 * 1024, FileOptions.Asynchronous))
            await using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                await writer.WriteLineAsync(
                    "logical_row,physical_line_start,physical_line_end,source_column,original_fields,destination_column,destination_type,transfer_strategy,error_category,error_message,sql_state");
                foreach (var row in rows)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var values = string.Join('\t', row.Values);
                    await writer.WriteLineAsync(string.Join(',',
                        Csv(row.SourceRow.ToString(CultureInfo.InvariantCulture)),
                        Csv(row.PhysicalLineStart?.ToString(CultureInfo.InvariantCulture) ?? string.Empty),
                        Csv(row.PhysicalLineEnd?.ToString(CultureInfo.InvariantCulture) ?? string.Empty),
                        Csv(row.SourceColumn ?? string.Empty),
                        Csv(values),
                        Csv(row.TargetColumn ?? string.Empty),
                        Csv(row.DestinationType ?? string.Empty),
                        Csv(row.TransferStrategy ?? string.Empty),
                        Csv(row.PostgreSqlErrorCode is null ? "Conversion" : "PostgreSQL"),
                        Csv(row.Error),
                        Csv(row.PostgreSqlErrorCode ?? string.Empty)));
                }
                await writer.FlushAsync(cancellationToken);
            }
            File.Move(temp, full, true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    private static string Csv(string value) => "\""
        + value.Replace("\"", "\"\"", StringComparison.Ordinal)
        + "\"";
}
