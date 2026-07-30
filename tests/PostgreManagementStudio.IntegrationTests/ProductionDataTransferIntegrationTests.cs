using Npgsql;
using PostgreManagementStudio.Application;
using PostgreManagementStudio.Postgres;

namespace PostgreManagementStudio.IntegrationTests;

[Collection(ResourceStabilityCollection.Name)]
public sealed class ProductionDataTransferIntegrationTests
{
    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("PMS_CONNECTION_STRING")
        ?? throw new InvalidOperationException("PMS_CONNECTION_STRING is required.");

    private static string Database =>
        Environment.GetEnvironmentVariable("PMS_TEST_DATABASE") ?? "postgres";

    [SeededPostgreSqlFact]
    public async Task MetadataReportsWritableGeneratedIdentityAndPermissions()
    {
        var metadata = await new NpgsqlTransferMetadataProvider().LoadAsync(
            ConnectionString, Database, "PMS Regression", "Type Matrix");

        Assert.Contains(metadata.Schemas, schema => schema == "PMS Regression");
        Assert.True(metadata.HasInsertPermission);
        Assert.Contains(metadata.Columns, column =>
            column.Name == "id" && column.IdentityAlways && !column.Writable);
        Assert.Contains(metadata.Columns, column =>
            column.Name == "generated_value" && column.Generated && !column.Writable);
        Assert.Contains(metadata.Columns, column =>
            column.Name == "unicode_text" && column.Writable);
    }

    [SeededPostgreSqlFact]
    public async Task CreatesNewTableAndImportsComplexPostgreSqlValues()
    {
        var table = "Sprint 60 Import " + Guid.NewGuid().ToString("N");
        var path = TemporaryPath(".csv");
        try
        {
            await File.WriteAllTextAsync(path,
                "id,external_id,payload,amount,occurred_at,binary\r\n"
                + "1,12345678-1234-5678-9abc-123456789abc,\"{\"\"ok\"\":true}\",12.50,2026-07-30T08:15:00+02:00,\\x00FF\r\n");
            var columns = new[]
            {
                new DestinationColumn("id", "integer", false),
                new DestinationColumn("external_id", "uuid", false),
                new DestinationColumn("payload", "jsonb", false),
                new DestinationColumn("amount", "numeric(12,2)", false),
                new DestinationColumn("occurred_at", "timestamp with time zone", false),
                new DestinationColumn("binary", "bytea", true),
            };
            var request = new ImportRequest(path, "PMS Regression", table,
                columns.Select((column, index) => new ColumnMapping(index, column.Name)).ToArray(),
                new(), new(ImportStrategy.Copy, Transaction: TransactionMode.AllRows),
                columns, true, NewTableSqlBuilder.Build("PMS Regression", table, columns));

            var result = await new NpgsqlDataTransferService().ImportAsync(ConnectionString, request);

            Assert.Equal("Completed", result.Status);
            Assert.True(result.NewTableCreated);
            Assert.Equal(1, result.RowsWritten);
            await using var connection = NpgsqlConnectionFactory.Shared.Create(
                ConnectionString, "PostgreManagementStudio - Sprint 60 Verify");
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                $"SELECT external_id,payload->>'ok',amount::text,octet_length(\"binary\") "
                + $"FROM {Quote("PMS Regression")}.{Quote(table)}", connection);
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(Guid.Parse("12345678-1234-5678-9abc-123456789abc"), reader.GetGuid(0));
            Assert.Equal("true", reader.GetString(1));
            Assert.Equal("12.50", reader.GetString(2));
            Assert.Equal(2, reader.GetInt32(3));
        }
        finally
        {
            File.Delete(path);
            await DropAsync(table);
        }
    }

    [SeededPostgreSqlFact]
    public async Task RequestedCopyFallsBackForJsonEnumDomainArrayAndReportsInvalidJsonContext()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var table = "Sprint 62 Complex " + suffix;
        var enumName = "Sprint 62 State " + suffix;
        var domainName = "Sprint 62 Amount " + suffix;
        var path = TemporaryPath(".csv");
        try
        {
            await ExecuteAsync(
                $"CREATE TYPE {Quote("PMS Regression")}.{Quote(enumName)} AS ENUM ('new','done');"
                + $"CREATE DOMAIN {Quote("PMS Regression")}.{Quote(domainName)} AS numeric(12,2) CHECK (VALUE >= 0);"
                + $"CREATE TABLE {Quote("PMS Regression")}.{Quote(table)} ("
                + "id integer PRIMARY KEY,"
                + "payload_json json NOT NULL,"
                + "payload_jsonb jsonb NOT NULL,"
                + $"state {Quote("PMS Regression")}.{Quote(enumName)} NOT NULL,"
                + $"amount {Quote("PMS Regression")}.{Quote(domainName)} NOT NULL,"
                + "labels text[] NOT NULL,"
                + "external_id uuid NOT NULL,"
                + "binary_value bytea NOT NULL,"
                + "event_date date NOT NULL,"
                + "event_time time NOT NULL,"
                + "local_stamp timestamp without time zone NOT NULL,"
                + "instant timestamp with time zone NOT NULL,"
                + "duration interval NOT NULL,"
                + "address inet NOT NULL,"
                + "span int4range NOT NULL,"
                + "spans int4multirange NOT NULL)");
            await File.WriteAllTextAsync(path,
                "id,payload_json,payload_jsonb,state,amount,labels,external_id,binary_value,event_date,event_time,local_stamp,instant,duration,address,span,spans\r\n"
                + "1,\"{\"\"status\"\":\"\"active\"\",\"\"count\"\":3}\",\"null\",new,12.50,\"{\"\"alpha\"\",\"\"comma,value\"\"}\",12345678-1234-5678-9abc-123456789abc,\\x00FF,2026-07-30,10:15:30,2026-07-30 10:15:30,2026-07-30T08:15:30Z,01:02:03,192.168.1.1/24,\"[1,5)\",\"{[1,5),[10,12)}\"\r\n");
            var types = new[]
            {
                "integer", "json", "jsonb",
                $"{Quote("PMS Regression")}.{Quote(enumName)}",
                $"{Quote("PMS Regression")}.{Quote(domainName)}",
                "text[]", "uuid", "bytea", "date", "time",
                "timestamp without time zone", "timestamp with time zone",
                "interval", "inet", "int4range", "int4multirange",
            };
            var names = new[]
            {
                "id", "payload_json", "payload_jsonb", "state", "amount", "labels",
                "external_id", "binary_value", "event_date", "event_time", "local_stamp",
                "instant", "duration", "address", "span", "spans",
            };
            var columns = names.Select((name, index) =>
                new DestinationColumn(name, types[index], false)).ToArray();
            var mappings = names.Select((name, index) => new ColumnMapping(index, name)).ToArray();
            var service = new NpgsqlDataTransferService();

            var success = await service.ImportAsync(ConnectionString,
                new(path, "PMS Regression", table, mappings, new(),
                    new(ImportStrategy.Copy, Transaction: TransactionMode.AllRows),
                    columns, SourceColumnNames: names));

            Assert.Equal("Completed", success.Status);
            Assert.Equal(1, success.RowsWritten);
            Assert.Equal(1, await ScalarAsync(table));

            await File.WriteAllTextAsync(path,
                "id,payload_json,payload_jsonb,state,amount,labels,external_id,binary_value,event_date,event_time,local_stamp,instant,duration,address,span,spans\r\n"
                + "2,\"{bad json\",\"{}\",new,12.50,\"{alpha}\",12345678-1234-5678-9abc-123456789abc,\\x00FF,2026-07-30,10:15:30,2026-07-30 10:15:30,2026-07-30T08:15:30Z,01:02:03,192.168.1.1,\"[1,5)\",\"{[1,5)}\"\r\n");
            var failure = await service.ImportAsync(ConnectionString,
                new(path, "PMS Regression", table, mappings, new(),
                    new(ImportStrategy.Copy, Transaction: TransactionMode.AllRows),
                    columns, SourceColumnNames: names));

            Assert.StartsWith("Failed", failure.Status);
            Assert.Equal(0, failure.RowsWritten);
            Assert.Equal(1, await ScalarAsync(table));
            var diagnostic = Assert.Single(failure.Diagnostics!);
            Assert.Equal(2, diagnostic.LogicalRow);
            Assert.Equal("payload_json", diagnostic.SourceColumn);
            Assert.Equal("payload_json", diagnostic.DestinationColumn);
            Assert.Equal("json", diagnostic.DestinationPostgreSqlType);
            Assert.Contains("not valid json", diagnostic.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
            await ExecuteAsync($"DROP TABLE IF EXISTS {Quote("PMS Regression")}.{Quote(table)};"
                + $"DROP DOMAIN IF EXISTS {Quote("PMS Regression")}.{Quote(domainName)};"
                + $"DROP TYPE IF EXISTS {Quote("PMS Regression")}.{Quote(enumName)};");
        }
    }

    [SeededPostgreSqlFact]
    public async Task AtomicFailureRollsBackAndBatchedModeDisclosesPartialCommit()
    {
        var table = "Sprint 60 Atomic " + Guid.NewGuid().ToString("N");
        var path = TemporaryPath(".csv");
        try
        {
            await ExecuteAsync($"CREATE TABLE {Quote("PMS Regression")}.{Quote(table)} "
                + "(id integer PRIMARY KEY, value integer NOT NULL)");
            await File.WriteAllTextAsync(path, "id,value\r\n1,10\r\n2,not-an-integer\r\n3,30\r\n");
            var mappings = new[] { new ColumnMapping(0, "id"), new ColumnMapping(1, "value") };
            var columns = new[]
            {
                new DestinationColumn("id", "integer", false),
                new DestinationColumn("value", "integer", false),
            };
            var atomic = await new NpgsqlDataTransferService().ImportAsync(ConnectionString,
                new(path, "PMS Regression", table, mappings, new(),
                    new(ImportStrategy.BatchInsert, Transaction: TransactionMode.AllRows),
                    columns));
            Assert.StartsWith("Failed", atomic.Status);
            Assert.Equal(0, atomic.RowsWritten);
            Assert.Equal(0, await ScalarAsync(table));

            var batched = await new NpgsqlDataTransferService().ImportAsync(ConnectionString,
                new(path, "PMS Regression", table, mappings, new(),
                    new(ImportStrategy.BatchInsert, Transaction: TransactionMode.PerBatch,
                        BatchSize: 1), columns));
            Assert.StartsWith("Failed", batched.Status);
            Assert.True(batched.PartialCommit);
            Assert.Equal(1, batched.RowsWritten);
            Assert.Equal(1, await ScalarAsync(table));
        }
        finally
        {
            File.Delete(path);
            await DropAsync(table);
        }
    }

    [SeededPostgreSqlFact]
    public async Task RelationExportsStreamCsvJsonLinesAndSqlWithoutReplacingOnCancellation()
    {
        var root = Path.Combine(Path.GetTempPath(), "pms-s60-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var columns = new[] { "id", "unicode_text", "binary_value" };
            foreach (var format in new[]
                     {
                         RelationExportFormat.Csv, RelationExportFormat.JsonLines,
                         RelationExportFormat.SqlInsert,
                     })
            {
                var path = Path.Combine(root, format + ".out");
                var result = await new NpgsqlRelationExportService().ExportAsync(
                    ConnectionString, Database,
                    new("PMS Regression", "Type Matrix", format, path,
                        new(columns, RowLimit: 1)));
                Assert.True(result.Completed);
                Assert.Equal(1, result.RowsWritten);
                Assert.True(new FileInfo(path).Length > 0);
            }

            var destination = Path.Combine(root, "cancelled.csv");
            await File.WriteAllTextAsync(destination, "original");
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var cancelled = await new NpgsqlRelationExportService().ExportAsync(
                ConnectionString, Database,
                new("PMS Regression", "Type Matrix", RelationExportFormat.Csv,
                    destination, new(["id"])), cancellationToken: cancellation.Token);
            Assert.True(cancelled.Cancelled);
            Assert.Equal("original", await File.ReadAllTextAsync(destination));
        }
        finally { Directory.Delete(root, true); }
    }

    private static async Task ExecuteAsync(string sql)
    {
        await using var connection = NpgsqlConnectionFactory.Shared.Create(
            ConnectionString, "PostgreManagementStudio - Sprint 60 Setup");
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> ScalarAsync(string table)
    {
        await using var connection = NpgsqlConnectionFactory.Shared.Create(
            ConnectionString, "PostgreManagementStudio - Sprint 60 Count");
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"SELECT count(*) FROM {Quote("PMS Regression")}.{Quote(table)}", connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static Task DropAsync(string table) =>
        ExecuteAsync($"DROP TABLE IF EXISTS {Quote("PMS Regression")}.{Quote(table)}");

    private static string Quote(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";

    private static string TemporaryPath(string extension) =>
        Path.Combine(Path.GetTempPath(), "pms-s60-" + Guid.NewGuid().ToString("N") + extension);
}
