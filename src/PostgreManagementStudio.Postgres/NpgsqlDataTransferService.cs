using System.Diagnostics;
using System.Globalization;
using Npgsql;
using PostgreManagementStudio.Application;

namespace PostgreManagementStudio.Postgres;

public sealed class NpgsqlDataTransferService(INpgsqlConnectionFactory? connectionFactory = null)
{
    private readonly INpgsqlConnectionFactory _connections =
        connectionFactory ?? NpgsqlConnectionFactory.Shared;

    public async Task<ImportResult> ImportAsync(
        string connectionString,
        ImportRequest request,
        IProgress<ImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ImportMappingService.Validate(request.Mappings, request.DestinationColumns);
        var started = Stopwatch.StartNew();
        var errors = new List<string>();
        var rejectedRows = new List<RejectedRow>();
        long read = 0;
        long written = 0;
        long rejected = 0;
        long skipped = 0;
        var partialCommit = false;
        var created = false;
        var batch = 0;
        await using var connection = _connections.Create(
            connectionString, "PostgreManagementStudio - Data Import");
        NpgsqlTransaction? transaction = null;
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            if (request.Options.Transaction == TransactionMode.AllRows)
                transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            if (request.CreateNewTable)
            {
                if (string.IsNullOrWhiteSpace(request.CreateTableSql))
                    throw new InvalidOperationException("The reviewed CREATE TABLE statement is required.");
                await Execute(connection, transaction, request.CreateTableSql, cancellationToken)
                    .ConfigureAwait(false);
                created = true;
            }
            await PrepareDestinationAsync(connection, transaction, request, cancellationToken)
                .ConfigureAwait(false);

            var mappings = request.Mappings
                .Where(mapping => mapping.Included && mapping.DestinationName is not null)
                .OrderBy(mapping => mapping.SourceOrdinal).ToArray();
            var format = new DelimitedFormatOptions(
                request.FileSettings.Delimiter,
                request.FileSettings.Quote,
                request.FileSettings.HasHeader,
                request.FileSettings.TrimWhitespace,
                true,
                request.FileSettings.NullToken);
            var reader = new ProductionDelimitedFileInspector();
            var records = reader.ReadAsync(request.SourcePath,
                request.FileSettings.Encoding ?? new System.Text.UTF8Encoding(false, true),
                format, cancellationToken);
            if (request.Options.Strategy == ImportStrategy.Copy)
            {
                await ImportCopyAsync(connection, transaction, request, mappings, records,
                    (record, accepted) =>
                    {
                        read++;
                        if (accepted) written++;
                        Report(progress, read, written, rejected, "Fast bulk import", request.SourcePath,
                            started.Elapsed, batch);
                    }, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var rowsInBatch = 0;
                var halted = false;
                await foreach (var record in records.WithCancellation(cancellationToken)
                                   .ConfigureAwait(false))
                {
                    if (request.FileSettings.HasHeader && record.SourceRow == 1) { skipped++; continue; }
                    read++;
                    if (record.IsMalformed)
                    {
                        Reject(record, null, record.Error!, null);
                        if (!request.Options.ContinueOnError) break;
                        continue;
                    }
                    if (request.Options.Transaction == TransactionMode.PerBatch && transaction is null)
                        transaction = await connection.BeginTransactionAsync(cancellationToken)
                            .ConfigureAwait(false);
                    var savepoint = request.Options.ContinueOnError && transaction is not null;
                    if (savepoint)
                        await transaction!.SaveAsync("pms_import_row", cancellationToken)
                            .ConfigureAwait(false);
                    try
                    {
                        await InsertAsync(connection, transaction, request, mappings, record,
                            cancellationToken).ConfigureAwait(false);
                        if (savepoint)
                            await transaction!.ReleaseAsync("pms_import_row", cancellationToken)
                                .ConfigureAwait(false);
                        written++;
                        rowsInBatch++;
                    }
                    catch (Exception exception) when (exception is FormatException
                        or OverflowException or ArgumentException or NpgsqlException)
                    {
                        if (savepoint)
                            await transaction!.RollbackAsync("pms_import_row", cancellationToken)
                                .ConfigureAwait(false);
                        Reject(record, TryColumn(exception), exception.Message,
                            (exception as PostgresException)?.SqlState);
                        if (!request.Options.ContinueOnError)
                        {
                            halted = true;
                            break;
                        }
                    }
                    if (request.Options.Transaction == TransactionMode.PerBatch
                        && rowsInBatch >= request.Options.BatchSize)
                    {
                        await transaction!.CommitAsync(cancellationToken).ConfigureAwait(false);
                        await transaction.DisposeAsync().ConfigureAwait(false);
                        transaction = null;
                        rowsInBatch = 0;
                        batch++;
                        partialCommit = written > 0;
                    }
                    Report(progress, read, written, rejected, "Row-by-row validated import",
                        request.SourcePath, started.Elapsed, batch);
                    if (rejected >= request.Options.ErrorLimit)
                    {
                        errors.Add($"The configured error limit of {request.Options.ErrorLimit:N0} was reached.");
                        break;
                    }
                }
                if (halted)
                {
                    if (transaction is not null)
                    {
                        await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                        await transaction.DisposeAsync().ConfigureAwait(false);
                        transaction = null;
                    }
                    if (request.Options.Transaction == TransactionMode.AllRows) written = 0;
                    else
                    {
                        written -= rowsInBatch;
                        partialCommit = written > 0;
                    }
                    var haltedRejectedPath = await WriteRejectedAsync(
                        request, rejectedRows, cancellationToken).ConfigureAwait(false);
                    return new("Failed — stopped on first error", read, written, rejected,
                        started.Elapsed, errors, skipped, partialCommit,
                        created && request.Options.Transaction != TransactionMode.AllRows,
                        haltedRejectedPath, ["The active transaction was rolled back."]);
                }
                if (transaction is not null)
                {
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                    await transaction.DisposeAsync().ConfigureAwait(false);
                    transaction = null;
                }
            }

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                await transaction.DisposeAsync().ConfigureAwait(false);
                transaction = null;
            }
            var rejectedPath = await WriteRejectedAsync(request, rejectedRows, cancellationToken)
                .ConfigureAwait(false);
            return new(rejected == 0 ? "Completed" : "Completed with rejected rows",
                read, written, rejected, started.Elapsed, errors, skipped, partialCommit,
                created, rejectedPath, []);

            void Reject(DelimitedRecord record, string? target, string error, string? sqlState)
            {
                rejected++;
                var safe = $"Source row {record.SourceRow}: {error}";
                errors.Add(safe);
                rejectedRows.Add(new(record.SourceRow,
                    record.Fields.Select(field => field.Value).ToArray(), target, error, sqlState));
            }
        }
        catch (OperationCanceledException)
        {
            if (transaction is not null)
            {
                try { await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false); }
                catch (ObjectDisposedException) { }
                await transaction.DisposeAsync().ConfigureAwait(false);
            }
            var rejectedPath = await WriteRejectedAsync(request, rejectedRows, CancellationToken.None)
                .ConfigureAwait(false);
            Report(progress, read, written, rejected, "Cancelled", request.SourcePath,
                started.Elapsed, batch, true);
            return new("Cancelled", read,
                request.Options.Transaction == TransactionMode.AllRows ? 0 : written,
                rejected, started.Elapsed, errors, skipped,
                request.Options.Transaction != TransactionMode.AllRows && written > 0,
                created && request.Options.Transaction != TransactionMode.AllRows,
                rejectedPath, ["Cancellation stopped additional input and rolled back the active transaction."]);
        }
        catch
        {
            if (transaction is not null)
            {
                try { await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false); }
                catch (ObjectDisposedException) { }
                await transaction.DisposeAsync().ConfigureAwait(false);
            }
            throw;
        }
    }

    private static async Task ImportCopyAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        ImportRequest request,
        ColumnMapping[] mappings,
        IAsyncEnumerable<DelimitedRecord> records,
        Action<DelimitedRecord, bool> rowCompleted,
        CancellationToken cancellationToken)
    {
        var columns = string.Join(",", mappings.Select(mapping =>
            PostgreSqlIdentifierQuoter.Quote(mapping.DestinationName!)));
        await using var importer = await connection.BeginBinaryImportAsync(
            $"COPY {PostgreSqlIdentifierQuoter.Qualified(request.Schema, request.Table)} ({columns}) "
            + "FROM STDIN (FORMAT BINARY)", cancellationToken).ConfigureAwait(false);
        await foreach (var record in records.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (request.FileSettings.HasHeader && record.SourceRow == 1) continue;
            if (record.IsMalformed) throw new FormatException(
                $"Source row {record.SourceRow}: {record.Error}");
            var values = ConvertRecord(request, mappings, record);
            await importer.StartRowAsync(cancellationToken).ConfigureAwait(false);
            foreach (var value in values)
                await importer.WriteAsync(value ?? DBNull.Value, cancellationToken).ConfigureAwait(false);
            rowCompleted(record, true);
        }
        await importer.CompleteAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        ImportRequest request,
        ColumnMapping[] mappings,
        DelimitedRecord record,
        CancellationToken cancellationToken)
    {
        var names = string.Join(",", mappings.Select(mapping =>
            PostgreSqlIdentifierQuoter.Quote(mapping.DestinationName!)));
        var parameters = string.Join(",", mappings.Select((mapping, index) =>
        {
            var destination = request.DestinationColumns.Single(column =>
                column.Name.Equals(mapping.DestinationName, StringComparison.Ordinal));
            if (!PostgreSqlTransferType.IsSafe(destination.PostgreSqlType))
                throw new InvalidOperationException(
                    $"Destination type {destination.PostgreSqlType} is not safe to parameterise.");
            return $"CAST(@v{index} AS {destination.PostgreSqlType})";
        }));
        await using var command = new NpgsqlCommand(
            $"INSERT INTO {PostgreSqlIdentifierQuoter.Qualified(request.Schema, request.Table)} "
            + $"({names}) VALUES ({parameters})", connection, transaction);
        var values = ConvertRecord(request, mappings, record);
        for (var index = 0; index < values.Length; index++)
            command.Parameters.AddWithValue("v" + index, values[index] ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static object?[] ConvertRecord(
        ImportRequest request,
        IReadOnlyList<ColumnMapping> mappings,
        DelimitedRecord record)
    {
        var result = new object?[mappings.Count];
        for (var index = 0; index < mappings.Count; index++)
        {
            var mapping = mappings[index];
            if (mapping.SourceOrdinal >= record.Fields.Count)
                throw new FormatException(
                    $"Source row {record.SourceRow} is missing field {mapping.SourceOrdinal + 1}.");
            var destination = request.DestinationColumns.Single(column =>
                column.Name.Equals(mapping.DestinationName, StringComparison.Ordinal));
            var rule = request.ColumnRules?.GetValueOrDefault(mapping.SourceOrdinal) ?? new();
            try
            {
                result[index] = ConvertValue(record.Fields[mapping.SourceOrdinal],
                    destination.PostgreSqlType, request.FileSettings, rule);
            }
            catch (Exception exception) when (exception is FormatException or OverflowException)
            {
                if (rule.InvalidValueMode == InvalidValueMode.SubstituteNull && destination.Nullable)
                    result[index] = null;
                else throw new FormatException(
                    $"Column {destination.Name}: {exception.Message}", exception);
            }
        }
        return result;
    }

    private static object? ConvertValue(
        DelimitedField field,
        string type,
        DelimitedFileSettings settings,
        ImportColumnRule rule)
    {
        var value = rule.TrimWhitespace ? field.Value.Trim() : field.Value;
        if (field.IsExplicitNull
            || rule.NullMarker is { } marker && value.Equals(marker, StringComparison.Ordinal)
            || rule.EmptyStringBecomesNull && value.Length == 0) return null;
        if (value.Length == 0) return string.Empty;
        var normalizedType = type.ToLowerInvariant();
        if (normalizedType is "boolean" or "bool")
        {
            var trueValues = rule.TrueValues ?? ["true", "t", "1", "yes"];
            var falseValues = rule.FalseValues ?? ["false", "f", "0", "no"];
            if (trueValues.Contains(value, StringComparer.OrdinalIgnoreCase)) return true;
            if (falseValues.Contains(value, StringComparer.OrdinalIgnoreCase)) return false;
            throw new FormatException($"'{value}' is not a configured boolean value.");
        }
        if (normalizedType is "smallint") return short.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
        if (normalizedType is "integer" or "int4") return int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
        if (normalizedType is "bigint" or "int8") return long.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
        if (normalizedType.StartsWith("numeric", StringComparison.Ordinal)
            || normalizedType is "decimal" or "real" or "double precision")
        {
            var numeric = value;
            if (rule.StripCurrencySymbol)
                numeric = new string(numeric.Where(character =>
                    char.IsDigit(character) || "+-.,eE()".Contains(character)).ToArray());
            if (rule.ParenthesesAreNegative && numeric.StartsWith('(') && numeric.EndsWith(')'))
                numeric = "-" + numeric[1..^1];
            if (rule.ThousandsSeparator is { Length: > 0 } thousands)
                numeric = numeric.Replace(thousands, string.Empty, StringComparison.Ordinal);
            if (rule.DecimalSeparator != ".")
                numeric = numeric.Replace(rule.DecimalSeparator, ".", StringComparison.Ordinal);
            var styles = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;
            if (rule.AllowExponent) styles |= NumberStyles.AllowExponent;
            if (normalizedType == "real") return float.Parse(numeric, styles, CultureInfo.InvariantCulture);
            if (normalizedType == "double precision") return double.Parse(numeric, styles, CultureInfo.InvariantCulture);
            return decimal.Parse(numeric, styles, CultureInfo.InvariantCulture);
        }
        if (normalizedType == "date")
            return rule.DateFormat is { Length: > 0 }
                ? DateOnly.ParseExact(value, rule.DateFormat, CultureInfo.InvariantCulture)
                : DateOnly.Parse(value, CultureInfo.InvariantCulture);
        if (normalizedType.StartsWith("time ", StringComparison.Ordinal)
            || normalizedType == "time")
            return rule.TimeFormat is { Length: > 0 }
                ? TimeOnly.ParseExact(value, rule.TimeFormat, CultureInfo.InvariantCulture)
                : TimeOnly.Parse(value, CultureInfo.InvariantCulture);
        if (normalizedType.Contains("timestamp with time zone", StringComparison.Ordinal)
            || normalizedType == "timestamptz")
            return (rule.TimestampFormat is { Length: > 0 }
                ? DateTimeOffset.ParseExact(value, rule.TimestampFormat, CultureInfo.InvariantCulture)
                : DateTimeOffset.Parse(value, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind)).ToUniversalTime();
        if (normalizedType.Contains("timestamp without time zone", StringComparison.Ordinal)
            || normalizedType == "timestamp")
            return DateTime.SpecifyKind(rule.TimestampFormat is { Length: > 0 }
                    ? DateTime.ParseExact(value, rule.TimestampFormat, CultureInfo.InvariantCulture,
                        DateTimeStyles.None)
                    : DateTime.Parse(value, CultureInfo.InvariantCulture),
                DateTimeKind.Unspecified);
        if (normalizedType == "uuid") return Guid.Parse(value);
        if (normalizedType is "json" or "jsonb") return value;
        if (normalizedType == "bytea")
            return value.StartsWith("\\x", StringComparison.OrdinalIgnoreCase)
                ? Convert.FromHexString(value[2..]) : System.Text.Encoding.UTF8.GetBytes(value);
        return value;
    }

    private static async Task PrepareDestinationAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        ImportRequest request,
        CancellationToken cancellationToken)
    {
        var sql = request.Options.ExistingData switch
        {
            ExistingDataMode.Truncate =>
                $"TRUNCATE TABLE {PostgreSqlIdentifierQuoter.Qualified(request.Schema, request.Table)}",
            ExistingDataMode.Delete =>
                $"DELETE FROM {PostgreSqlIdentifierQuoter.Qualified(request.Schema, request.Table)}",
            _ => null,
        };
        if (sql is not null)
            await Execute(connection, transaction, sql, cancellationToken).ConfigureAwait(false);
    }

    private static async Task Execute(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string?> WriteRejectedAsync(
        ImportRequest request,
        IReadOnlyList<RejectedRow> rows,
        CancellationToken cancellationToken)
    {
        if (rows.Count == 0 || string.IsNullOrWhiteSpace(request.Options.RejectedRowsPath)) return null;
        await new RejectedRowWriter().WriteAsync(
            request.Options.RejectedRowsPath, rows, cancellationToken).ConfigureAwait(false);
        return Path.GetFullPath(request.Options.RejectedRowsPath);
    }

    private static void Report(
        IProgress<ImportProgress>? progress,
        long read,
        long written,
        long rejected,
        string phase,
        string sourcePath,
        TimeSpan elapsed,
        int batch,
        bool cancellationRequested = false)
    {
        var bytes = File.Exists(sourcePath) ? new FileInfo(sourcePath).Length : 0;
        progress?.Report(new(read, written, rejected, phase, bytes, elapsed, batch,
            cancellationRequested));
    }

    private static string? TryColumn(Exception exception)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            exception.Message, @"Column (?<name>[^:]+):");
        return match.Success ? match.Groups["name"].Value : null;
    }
}
