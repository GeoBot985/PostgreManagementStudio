using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Npgsql;
using PostgreManagementStudio.Application;

namespace PostgreManagementStudio.Postgres;

public sealed class NpgsqlRelationExportService(
    INpgsqlConnectionFactory? connectionFactory = null) : IRelationExportService
{
    private readonly INpgsqlConnectionFactory _connections =
        connectionFactory ?? NpgsqlConnectionFactory.Shared;

    public async Task<TransferExportResult> ExportAsync(
        string connectionString,
        string database,
        RelationExportRequest request,
        IProgress<TransferExportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Validate(request);
        var started = Stopwatch.StartNew();
        var full = Path.GetFullPath(request.DestinationPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var temp = full + ".pms-export-" + Guid.NewGuid().ToString("N") + ".tmp";
        var rows = 0L;
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString) { Database = database };
            await using var connection = _connections.Create(
                builder.ConnectionString, "PostgreManagementStudio - Relation Export");
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new NpgsqlCommand(BuildSql(request), connection);
            await using var reader = await command.ExecuteReaderAsync(
                System.Data.CommandBehavior.SequentialAccess, cancellationToken).ConfigureAwait(false);
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write,
                             FileShare.None, 64 * 1024,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var writer = new StreamWriter(stream,
                             request.Options.Encoding ?? new UTF8Encoding(false, true),
                             64 * 1024, leaveOpen: true))
            {
                rows = request.Format switch
                {
                    RelationExportFormat.Json => await WriteJsonAsync(
                        reader, writer, request, progress, started, cancellationToken)
                        .ConfigureAwait(false),
                    RelationExportFormat.JsonLines => await WriteJsonLinesAsync(
                        reader, writer, request, progress, started, cancellationToken)
                        .ConfigureAwait(false),
                    RelationExportFormat.SqlInsert => await WriteSqlAsync(
                        reader, writer, request, progress, started, cancellationToken)
                        .ConfigureAwait(false),
                    _ => await WriteDelimitedAsync(
                        reader, writer, request, progress, started, cancellationToken)
                        .ConfigureAwait(false),
                };
                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            File.Move(temp, full, true);
            return new("Completed",
                PostgreSqlIdentifierQuoter.Qualified(request.Schema, request.Relation),
                full, request.Format, rows, new FileInfo(full).Length, started.Elapsed,
                true, false, true, []);
        }
        catch (OperationCanceledException)
        {
            return new("Cancelled",
                PostgreSqlIdentifierQuoter.Qualified(request.Schema, request.Relation),
                full, request.Format, rows, 0, started.Elapsed,
                false, true, false,
                ["The database command and local stream were cancelled; the final destination was not replaced."]);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    private static string BuildSql(RelationExportRequest request)
    {
        var columns = string.Join(", ", request.Options.Columns.Select(
            PostgreSqlIdentifierQuoter.Quote));
        var sql = new StringBuilder("SELECT ").Append(columns).Append(" FROM ")
            .Append(PostgreSqlIdentifierQuoter.Qualified(request.Schema, request.Relation));
        if (!string.IsNullOrWhiteSpace(request.Options.WherePredicate))
            sql.Append(" WHERE ").Append(request.Options.WherePredicate);
        if (!string.IsNullOrWhiteSpace(request.Options.OrderBy))
            sql.Append(" ORDER BY ").Append(request.Options.OrderBy);
        if (request.Options.RowLimit is { } limit)
            sql.Append(" LIMIT ").Append(limit.ToString(CultureInfo.InvariantCulture));
        return sql.ToString();
    }

    private static async Task<long> WriteDelimitedAsync(
        NpgsqlDataReader reader,
        StreamWriter writer,
        RelationExportRequest request,
        IProgress<TransferExportProgress>? progress,
        Stopwatch started,
        CancellationToken cancellationToken)
    {
        var options = request.Options;
        var delimiter = request.Format == RelationExportFormat.Tsv ? '\t' : options.Delimiter;
        var headers = OutputHeaders(request);
        if (options.IncludeHeaders)
            await writer.WriteAsync(string.Join(delimiter,
                headers.Select(value => QuoteDelimited(value, delimiter, options.Quote)))
                + options.LineEnding).ConfigureAwait(false);
        var rows = 0L;
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var values = new string[reader.FieldCount];
            for (var index = 0; index < reader.FieldCount; index++)
                values[index] = await reader.IsDBNullAsync(index, cancellationToken).ConfigureAwait(false)
                    ? options.NullText
                    : QuoteDelimited(FormatValue(reader.GetValue(index)), delimiter, options.Quote);
            await writer.WriteAsync(string.Join(delimiter, values) + options.LineEnding)
                .ConfigureAwait(false);
            rows++;
            if (rows % 100 == 0) Report(progress, rows, writer.BaseStream, "Streaming rows", started);
        }
        Report(progress, rows, writer.BaseStream, "Finalising output", started);
        return rows;
    }

    private static async Task<long> WriteJsonAsync(
        NpgsqlDataReader reader,
        StreamWriter writer,
        RelationExportRequest request,
        IProgress<TransferExportProgress>? progress,
        Stopwatch started,
        CancellationToken cancellationToken)
    {
        var headers = Unique(OutputHeaders(request));
        await writer.WriteAsync("[").ConfigureAwait(false);
        var rows = 0L;
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (rows > 0) await writer.WriteAsync(",").ConfigureAwait(false);
            if (request.Options.PrettyJson)
                await writer.WriteAsync(request.Options.LineEnding + "  ").ConfigureAwait(false);
            await writer.WriteAsync(await JsonObjectAsync(reader, headers, cancellationToken)
                .ConfigureAwait(false)).ConfigureAwait(false);
            rows++;
            if (rows % 100 == 0) Report(progress, rows, writer.BaseStream, "Streaming JSON array", started);
        }
        if (request.Options.PrettyJson && rows > 0)
            await writer.WriteAsync(request.Options.LineEnding).ConfigureAwait(false);
        await writer.WriteAsync("]").ConfigureAwait(false);
        Report(progress, rows, writer.BaseStream, "Finalising output", started);
        return rows;
    }

    private static async Task<long> WriteJsonLinesAsync(
        NpgsqlDataReader reader,
        StreamWriter writer,
        RelationExportRequest request,
        IProgress<TransferExportProgress>? progress,
        Stopwatch started,
        CancellationToken cancellationToken)
    {
        var headers = Unique(OutputHeaders(request));
        var rows = 0L;
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            await writer.WriteAsync(await JsonObjectAsync(reader, headers, cancellationToken)
                .ConfigureAwait(false) + request.Options.LineEnding).ConfigureAwait(false);
            rows++;
            if (rows % 100 == 0) Report(progress, rows, writer.BaseStream, "Streaming JSON Lines", started);
        }
        Report(progress, rows, writer.BaseStream, "Finalising output", started);
        return rows;
    }

    private static async Task<long> WriteSqlAsync(
        NpgsqlDataReader reader,
        StreamWriter writer,
        RelationExportRequest request,
        IProgress<TransferExportProgress>? progress,
        Stopwatch started,
        CancellationToken cancellationToken)
    {
        if (request.Options.IncludeTransaction)
            await writer.WriteAsync("BEGIN;" + request.Options.LineEnding).ConfigureAwait(false);
        var target = PostgreSqlIdentifierQuoter.Qualified(request.Schema, request.Relation);
        var columns = string.Join(", ", request.Options.Columns.Select(PostgreSqlIdentifierQuoter.Quote));
        var batch = new List<string>();
        var rows = 0L;
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var values = new string[reader.FieldCount];
            for (var index = 0; index < reader.FieldCount; index++)
                values[index] = await reader.IsDBNullAsync(index, cancellationToken).ConfigureAwait(false)
                    ? "NULL" : SqlLiteral(reader.GetValue(index));
            batch.Add("(" + string.Join(", ", values) + ")");
            rows++;
            if (batch.Count >= request.Options.SqlBatchSize)
                await FlushSqlBatchAsync(writer, target, columns, batch, request.Options.LineEnding)
                    .ConfigureAwait(false);
            if (rows % 100 == 0) Report(progress, rows, writer.BaseStream,
                "Writing PostgreSQL INSERT statements", started);
        }
        await FlushSqlBatchAsync(writer, target, columns, batch, request.Options.LineEnding)
            .ConfigureAwait(false);
        if (request.Options.IncludeTransaction)
            await writer.WriteAsync("COMMIT;" + request.Options.LineEnding).ConfigureAwait(false);
        Report(progress, rows, writer.BaseStream, "Finalising output", started);
        return rows;
    }

    private static async Task<string> JsonObjectAsync(
        NpgsqlDataReader reader,
        IReadOnlyList<string> headers,
        CancellationToken cancellationToken)
    {
        var values = new string[reader.FieldCount];
        for (var index = 0; index < reader.FieldCount; index++)
        {
            if (await reader.IsDBNullAsync(index, cancellationToken).ConfigureAwait(false))
                values[index] = "null";
            else
            {
                var value = reader.GetValue(index);
                try { values[index] = JsonSerializer.Serialize(value); }
                catch (NotSupportedException)
                {
                    values[index] = JsonSerializer.Serialize(FormatValue(value));
                }
            }
        }
        return "{" + string.Join(",", headers.Zip(values)
            .Select(pair => JsonSerializer.Serialize(pair.First) + ":" + pair.Second)) + "}";
    }

    private static async Task FlushSqlBatchAsync(
        StreamWriter writer,
        string target,
        string columns,
        List<string> rows,
        string lineEnding)
    {
        if (rows.Count == 0) return;
        await writer.WriteAsync($"INSERT INTO {target} ({columns}) VALUES{lineEnding}"
            + string.Join("," + lineEnding, rows) + ";" + lineEnding).ConfigureAwait(false);
        rows.Clear();
    }

    private static IReadOnlyList<string> OutputHeaders(RelationExportRequest request) =>
        request.Options.OutputHeaders is { Count: > 0 } headers
        && headers.Count == request.Options.Columns.Count
            ? headers : request.Options.Columns;

    private static string[] Unique(IEnumerable<string> headers)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return headers.Select(header =>
        {
            var candidate = header;
            var suffix = 2;
            while (!used.Add(candidate)) candidate = header + "_" + suffix++;
            return candidate;
        }).ToArray();
    }

    private static string QuoteDelimited(string value, char delimiter, char quote) =>
        value.Contains(delimiter) || value.Contains(quote) || value.Contains('\r')
        || value.Contains('\n') || value.Trim() != value
            ? quote + value.Replace(quote.ToString(), new string(quote, 2),
                StringComparison.Ordinal) + quote
            : value;

    private static string FormatValue(object value) => value switch
    {
        byte[] bytes => "\\x" + Convert.ToHexString(bytes),
        DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        TimeOnly time => time.ToString("HH:mm:ss.fffffff", CultureInfo.InvariantCulture),
        DateTime timestamp => timestamp.ToString("yyyy-MM-dd HH:mm:ss.fffffff",
            CultureInfo.InvariantCulture),
        DateTimeOffset timestamp => timestamp.ToString("yyyy-MM-dd HH:mm:ss.fffffffzzz",
            CultureInfo.InvariantCulture),
        Array array => "{" + string.Join(",", array.Cast<object?>().Select(item =>
            Convert.ToString(item, CultureInfo.InvariantCulture))) + "}",
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
    };

    private static string SqlLiteral(object value) => value switch
    {
        bool boolean => boolean ? "TRUE" : "FALSE",
        byte[] bytes => "'\\x" + Convert.ToHexString(bytes) + "'::bytea",
        sbyte or byte or short or ushort or int or uint or long or ulong or decimal or float or double =>
            Convert.ToString(value, CultureInfo.InvariantCulture)!,
        _ => "'" + FormatValue(value).Replace("'", "''", StringComparison.Ordinal) + "'",
    };

    private static void Report(
        IProgress<TransferExportProgress>? progress,
        long rows,
        Stream stream,
        string phase,
        Stopwatch started) =>
        progress?.Report(new(rows, rows, stream.Position, phase, started.Elapsed));

    private static void Validate(RelationExportRequest request)
    {
        if (request.Options.Columns.Count == 0)
            throw new ArgumentException("Select at least one export column.");
        if (request.Options.RowLimit is <= 0)
            throw new ArgumentOutOfRangeException(nameof(request.Options.RowLimit));
        if (request.Options.SqlBatchSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(request.Options.SqlBatchSize));
        if (request.Options.Delimiter == request.Options.Quote)
            throw new ArgumentException("Delimiter and quote characters must differ.");
        foreach (var fragment in new[] { request.Options.WherePredicate, request.Options.OrderBy })
            if (fragment?.Contains(';') == true || fragment?.Contains("--", StringComparison.Ordinal) == true
                || fragment?.Contains("/*", StringComparison.Ordinal) == true)
                throw new ArgumentException("Filter and ordering fragments cannot contain statement separators or comments.");
    }
}
