using Npgsql;
using PostgreManagementStudio.Application;
using PostgreManagementStudio.Core;
using PostgreManagementStudio.Postgres;

namespace PostgreManagementStudio.IntegrationTests;

public sealed class HardeningRegressionIntegrationTests
{
    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("PMS_CONNECTION_STRING") ??
        throw new InvalidOperationException("PMS_CONNECTION_STRING is required.");

    [SeededPostgreSqlFact]
    [Trait("Category", "Smoke")]
    [Trait("Priority", "P0")]
    public async Task ProductionConnection_ContextSeedAndObjectExplorer_AreUsable()
    {
        var database = Environment.GetEnvironmentVariable("PMS_TEST_DATABASE")!;
        await using (var connection = NpgsqlConnectionFactory.Shared.Create(ConnectionString, "PostgreManagementStudio - Regression Smoke"))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("""
                SELECT current_database(), unicode_text, octet_length(binary_value),
                       numeric_value::text, uuid_value::text, integer_array::text,
                       generated_value
                FROM "PMS Regression"."Type Matrix"
                """, connection);
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(database, reader.GetString(0));
            Assert.Equal("Résumé 東京 🚀", reader.GetString(1));
            Assert.Equal(3, reader.GetInt32(2));
            Assert.Equal("123456789012345678.123456789012", reader.GetString(3));
            Assert.Equal("12345678-1234-5678-9abc-123456789abc", reader.GetString(4));
            Assert.Equal("{1,2,3}", reader.GetString(5));
            Assert.Equal(2, reader.GetInt32(6));
        }

        var explorer = new ObjectExplorerService(new NpgsqlMetadataProvider());
        var root = await explorer.LoadDatabaseAsync(ConnectionString, database);
        var schema = Assert.Single(root.Children, x => x.Name == "PMS Regression");
        Assert.Contains(schema.Children.SelectMany(x => x.Children), x => x.Name == "Type Matrix");
        Assert.Contains(schema.Children.SelectMany(x => x.Children), x => x.Name == "Order");
        Assert.Contains(schema.Children.SelectMany(x => x.Children), x => x.Name == "Materialized Résumé");
        Assert.Contains(schema.Children.SelectMany(x => x.Children), x => x.Name.StartsWith("Function With Space(", StringComparison.Ordinal));
        Assert.Contains(schema.Children.SelectMany(x => x.Children), x => x.Name.StartsWith("Procedure With Space(", StringComparison.Ordinal));
    }

    [SeededPostgreSqlFact]
    [Trait("Category", "Integration")]
    [Trait("Priority", "P0")]
    public async Task Transaction_RollbackIsolationAndRecovery_AreDeterministic()
    {
        var code = "tx-" + Guid.NewGuid().ToString("N");
        await using var writer = NpgsqlConnectionFactory.Shared.Create(ConnectionString, "PostgreManagementStudio - Transaction Writer");
        await using var observer = NpgsqlConnectionFactory.Shared.Create(ConnectionString, "PostgreManagementStudio - Transaction Observer");
        await writer.OpenAsync();
        await observer.OpenAsync();
        await using var transaction = await writer.BeginTransactionAsync();
        await using (var insert = new NpgsqlCommand("""INSERT INTO "PMS Regression"."Parent Table"(code) VALUES (@code)""", writer, transaction))
        {
            insert.Parameters.AddWithValue("code", code);
            Assert.Equal(1, await insert.ExecuteNonQueryAsync());
        }
        await using (var unseen = new NpgsqlCommand("""SELECT count(*) FROM "PMS Regression"."Parent Table" WHERE code=@code""", observer))
        {
            unseen.Parameters.AddWithValue("code", code);
            Assert.Equal(0L, await unseen.ExecuteScalarAsync());
        }
        await transaction.RollbackAsync();
        await using var recovered = new NpgsqlCommand("SELECT 42", writer);
        Assert.Equal(42, await recovered.ExecuteScalarAsync());
    }

    [SeededPostgreSqlFact]
    [Trait("Category", "Contract")]
    [Trait("Priority", "P1")]
    public async Task PermissionRoles_ReadOnlyAndRestricted_AreEnforced()
    {
        var readOnlyConnectionString = Environment.GetEnvironmentVariable("PMS_TEST_READONLY_CONNECTION_STRING")!;
        var restrictedConnectionString = Environment.GetEnvironmentVariable("PMS_TEST_RESTRICTED_CONNECTION_STRING")!;
        await using var readOnly = NpgsqlConnectionFactory.Shared.Create(readOnlyConnectionString, "PostgreManagementStudio - Read Only Test");
        await readOnly.OpenAsync();
        await using (var select = new NpgsqlCommand("""SELECT count(*) FROM "PMS Regression"."Type Matrix" """, readOnly))
            Assert.Equal(1L, await select.ExecuteScalarAsync());
        await using (var deniedWrite = new NpgsqlCommand("""DELETE FROM "PMS Regression"."Type Matrix" """, readOnly))
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() => deniedWrite.ExecuteNonQueryAsync());
            Assert.Equal("42501", error.SqlState);
        }

        await using var restricted = NpgsqlConnectionFactory.Shared.Create(restrictedConnectionString, "PostgreManagementStudio - Restricted Test");
        await restricted.OpenAsync();
        await using var deniedRead = new NpgsqlCommand("""SELECT count(*) FROM "PMS Regression"."Type Matrix" """, restricted);
        var restrictedError = await Assert.ThrowsAsync<PostgresException>(() => deniedRead.ExecuteScalarAsync());
        Assert.Equal("42501", restrictedError.SqlState);
    }

    [SeededPostgreSqlFact]
    [Trait("Category", "Integration")]
    [Trait("Priority", "P1")]
    public async Task MonitoringSearchAndPlan_UseProductionPostgreSqlAdapters()
    {
        var activity = await new NpgsqlActivityService().LoadSnapshotAsync(ConnectionString, 1);
        Assert.NotNull(activity.Summary);

        var search = await new NpgsqlObjectSearchService().SearchAsync(ConnectionString, new ObjectSearchOptions("Type Matrix"));
        Assert.Contains(search.Results, x => x.Schema == "PMS Regression" && x.ObjectName == "Type Matrix");

        var plan = await new NpgsqlExecutionPlanService().ExplainAsync(
            ConnectionString,
            new ExplainRequest("""SELECT * FROM "PMS Regression"."Type Matrix" WHERE id = 1""", new(PlanType.Estimated)));
        Assert.NotNull(plan.Root);
        Assert.NotEmpty(plan.RawJson);
    }

    [PostgreSqlFact]
    [Trait("Category", "Integration")]
    [Trait("Priority", "P0")]
    public async Task ConnectionFailureAndTimeout_DoNotLeakCredentialsAndRecoveryWorks()
    {
        var builder = new NpgsqlConnectionStringBuilder(ConnectionString)
        {
            Password = "regression-password-that-must-not-appear",
            Timeout = 2,
            Pooling = false,
        };
        await using (var bad = NpgsqlConnectionFactory.Shared.Create(builder.ConnectionString, "PostgreManagementStudio - Failure Test"))
        {
            var error = await Assert.ThrowsAnyAsync<NpgsqlException>(() => bad.OpenAsync());
            Assert.DoesNotContain("regression-password-that-must-not-appear", error.ToString(), StringComparison.Ordinal);
        }

        var events = new List<QueryExecutionEvent>();
        await foreach (var item in new NpgsqlQueryExecutor().ExecuteAsync(
            new QueryRequest("SELECT pg_sleep(5)", ConnectionString, new QueryExecutionOptions(commandTimeout: TimeSpan.FromSeconds(1)))))
            events.Add(item);
        Assert.Contains(events, x => x is ExecutionFailed);

        events.Clear();
        await foreach (var item in new NpgsqlQueryExecutor().ExecuteAsync(new QueryRequest("SELECT 42", ConnectionString)))
            events.Add(item);
        Assert.Contains(events, x => x is ExecutionCompleted);
    }

    [ExternalToolsFact]
    [Trait("Category", "Integration")]
    [Trait("Priority", "P1")]
    public async Task BackupTool_ProducesNonEmptyArchiveWithoutPasswordArgument()
    {
        var tools = new PostgreSqlToolLocator().Locate(Environment.GetEnvironmentVariable("PMS_TEST_PG_BIN"));
        Assert.NotNull(tools);
        var destination = Path.Combine(Path.GetTempPath(), $"pms-regression-{Guid.NewGuid():N}.backup");
        try
        {
            var databaseConnection = DatabaseConnection.FromConnectionString(ConnectionString);
            var request = BackupCommandBuilder.Build(
                new BackupOptions(databaseConnection, destination, BackupFormat.Custom),
                tools!);
            Assert.DoesNotContain(databaseConnection.Password!, request.Arguments);
            Assert.DoesNotContain(request.Arguments, x => x.Contains("regression-password", StringComparison.OrdinalIgnoreCase));
            var result = await new ExternalProcessRunner().RunAsync(request);
            Assert.Equal(0, result.ExitCode);
            Assert.True(new FileInfo(destination).Length > 0);
        }
        finally
        {
            if (File.Exists(destination)) File.Delete(destination);
        }
    }

    [SeededPostgreSqlFact]
    [Trait("Category", "EndToEnd")]
    [Trait("Priority", "P1")]
    public async Task CsvImport_QuotedValuesNullsAndTransactionReachPostgreSqlExactly()
    {
        var table = "Sprint 44 Import " + Guid.NewGuid().ToString("N")[..10];
        var qualifiedTable = PostgreSqlIdentifierQuoter.Qualified("PMS Regression", table);
        var source = Path.Combine(
            Path.GetTempPath(),
            $"pms-s44-import-{Guid.NewGuid():N}.csv");
        try
        {
            await using (var setup = NpgsqlConnectionFactory.Shared.Create(
                             ConnectionString,
                             "PostgreManagementStudio - Sprint 44 Import Setup"))
            {
                await setup.OpenAsync();
                await using var create = new NpgsqlCommand(
                    $"CREATE TABLE {qualifiedTable} (id integer PRIMARY KEY, name text NOT NULL, note text NULL)",
                    setup);
                await create.ExecuteNonQueryAsync();
            }

            await File.WriteAllTextAsync(
                source,
                "id,name,note\r\n1,\"A, B\",\"line 1\r\nline 2\"\r\n2,00123,\\N\r\n");
            var request = new ImportRequest(
                source,
                "PMS Regression",
                table,
                [
                    new(0, "id"),
                    new(1, "name"),
                    new(2, "note"),
                ],
                new(),
                new(
                    Strategy: ImportStrategy.BatchInsert,
                    Transaction: TransactionMode.AllRows),
                [
                    new("id", "integer", false),
                    new("name", "text", false),
                    new("note", "text", true),
                ]);

            var result = await new NpgsqlDataTransferService().ImportAsync(
                ConnectionString,
                request);
            Assert.Equal("Completed", result.Status);
            Assert.Equal(2, result.RowsRead);
            Assert.Equal(2, result.RowsWritten);
            Assert.Equal(0, result.RowsRejected);

            await using var verify = NpgsqlConnectionFactory.Shared.Create(
                ConnectionString,
                "PostgreManagementStudio - Sprint 44 Import Verification");
            await verify.OpenAsync();
            await using var command = new NpgsqlCommand(
                $"SELECT id, name, note FROM {qualifiedTable} ORDER BY id",
                verify);
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(1, reader.GetInt32(0));
            Assert.Equal("A, B", reader.GetString(1));
            Assert.Equal("line 1\r\nline 2", reader.GetString(2));
            Assert.True(await reader.ReadAsync());
            Assert.Equal(2, reader.GetInt32(0));
            Assert.Equal("00123", reader.GetString(1));
            Assert.True(reader.IsDBNull(2));
            Assert.False(await reader.ReadAsync());
        }
        finally
        {
            File.Delete(source);
            await using var cleanup = NpgsqlConnectionFactory.Shared.Create(
                ConnectionString,
                "PostgreManagementStudio - Sprint 44 Import Cleanup");
            await cleanup.OpenAsync();
            await using var drop = new NpgsqlCommand(
                $"DROP TABLE IF EXISTS {qualifiedTable}",
                cleanup);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
