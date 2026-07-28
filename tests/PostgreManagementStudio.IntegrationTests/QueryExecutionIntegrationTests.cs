using PostgreManagementStudio.Core;
using PostgreManagementStudio.Postgres;
using PostgreManagementStudio.Application;

namespace PostgreManagementStudio.IntegrationTests;

public sealed class QueryExecutionIntegrationTests
{
    private static string ConnectionString
    {
        get
        {
            var value = Environment.GetEnvironmentVariable("PMS_CONNECTION_STRING");
            return value ?? throw new InvalidOperationException("PostgreSQL test was executed without PMS_CONNECTION_STRING.");
        }
    }

    private static async Task<List<QueryExecutionEvent>> RunAsync(string sql, QueryExecutionOptions? options = null, CancellationToken token = default)
    {
        var events = new List<QueryExecutionEvent>();
        await foreach (var item in new NpgsqlQueryExecutor().ExecuteAsync(new QueryRequest(sql, ConnectionString, options), token)) events.Add(item);
        return events;
    }

    [PostgreSqlFact]
    public async Task ScalarQueryStreamsMetadataRowsAndCompletion()
    {
        var events = await RunAsync("SELECT 1 AS value;");
        Assert.Contains(events, e => e is ResultSetStarted s && s.Schema.Columns.Single().Name == "value");
        Assert.Contains(events, e => e is RowBatchReceived b && (int)b.Batch.Rows.Single().Cells.Single().Value! == 1);
        Assert.Contains(events, e => e is ExecutionCompleted c && c.ResultSetCount == 1);
    }

    [PostgreSqlFact]
    public async Task CentralConnectionPathAndVersionDetectionWork()
    {
        var version = await new NpgsqlPostgresVersionQuery().ExecuteAsync(ConnectionString);
        Assert.Contains("PostgreSQL", version, StringComparison.OrdinalIgnoreCase);

        var events = await RunAsync("SELECT current_setting('application_name');");
        Assert.Contains(
            events.OfType<RowBatchReceived>().SelectMany(x => x.Batch.Rows),
            row => string.Equals(
                row.Cells[0].Value as string,
                "PostgreManagementStudio - Query",
                StringComparison.Ordinal));
    }

    [PostgreSqlFact]
    public async Task BatchingPreservesAllRows()
    {
        var events = await RunAsync("SELECT generate_series(1, 1200) AS value;", new QueryExecutionOptions(128));
        var rows = events.OfType<RowBatchReceived>().SelectMany(b => b.Batch.Rows).Select(r => (int)r.Cells[0].Value!).ToArray();
        Assert.Equal(1200, rows.Length); Assert.Equal(Enumerable.Range(1, 1200), rows); Assert.True(events.OfType<RowBatchReceived>().Count() > 1);
    }

    [PostgreSqlFact]
    public async Task NoticeAndDatabaseErrorAreStructured()
    {
        var notice = await RunAsync("DO $$ BEGIN RAISE NOTICE 'Sprint 001 notice'; END $$;");
        Assert.Contains(notice.OfType<DatabaseNoticeReceived>(), n => n.Notice.Message.Contains("Sprint 001 notice", StringComparison.Ordinal));
        var error = await RunAsync("SELECT * FROM sprint001_missing_table;");
        Assert.Contains(error, e => e is ExecutionFailed f && f.Error.SqlState == "42P01");
    }

    [PostgreSqlFact]
    public async Task MultipleResultSetsAndCommandsAreReported()
    {
        var results = await RunAsync("SELECT 1 AS first_value; SELECT 2 AS second_value;");
        var values = results.OfType<RowBatchReceived>().SelectMany(x => x.Batch.Rows).Select(x => x.Cells[0].Value).ToArray();
        Assert.Equal(new object?[] { 1, 2 }, values);
        Assert.Equal(2, results.OfType<ResultSetCompleted>().Count());
        var command = await RunAsync("CREATE TEMP TABLE sprint001_test(id integer); INSERT INTO sprint001_test VALUES (1), (2), (3);");
        Assert.Contains(command, x => x is CommandCompleted);
    }

    [PostgreSqlFact]
    public async Task CancellationAllowsRecovery()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var events = await RunAsync("SELECT pg_sleep(10);", token: cts.Token);
        Assert.Contains(events, e => e is ExecutionCancelled);
        var recovery = await RunAsync("SELECT 42;");
        Assert.Contains(recovery, e => e is RowBatchReceived b && (int)b.Batch.Rows[0].Cells[0].Value! == 42);
    }

    [PostgreSqlFact]
    public async Task PostgreSqlErrorsPreserveClassificationAndDiagnosticFields()
    {
        var missingTable = Assert.Single((await RunAsync("SELECT * FROM table_that_does_not_exist;")).OfType<ExecutionFailed>());
        Assert.Equal("42P01", missingTable.Error.SqlState);
        Assert.NotNull(missingTable.Error.Position);

        var division = Assert.Single((await RunAsync("SELECT 1 / 0;")).OfType<ExecutionFailed>());
        Assert.Equal("22012", division.Error.SqlState);

        var missingColumn = Assert.Single((await RunAsync("SELECT invalid_column FROM pg_catalog.pg_class;")).OfType<ExecutionFailed>());
        Assert.Equal("42703", missingColumn.Error.SqlState);

        var duplicate = Assert.Single((await RunAsync("CREATE TEMP TABLE duplicate_test(id integer); CREATE TEMP TABLE duplicate_test(id integer);")).OfType<ExecutionFailed>());
        Assert.Equal("42P07", duplicate.Error.SqlState);
    }

    [PostgreSqlFact]
    public async Task ComplexMultiStatementScriptPreservesOrderNoticesAndStopsAfterFailure()
    {
        var events = await RunAsync("""
            SELECT 'value;still inside string' AS first_value;
            /* outer comment /* nested comment */ remains valid */
            DO $body$
            BEGIN
                RAISE NOTICE 'Testing; notice';
            END;
            $body$;
            SELECT 42 AS second_value;
            """);
        var rows = events.OfType<RowBatchReceived>().SelectMany(batch => batch.Batch.Rows).ToArray();
        Assert.Equal("value;still inside string", rows[0].Cells[0].Value);
        Assert.Equal(42, rows[1].Cells[0].Value);
        Assert.Contains(events.OfType<DatabaseNoticeReceived>(), x => x.Notice.Message == "Testing; notice");
        Assert.Equal(2, events.OfType<ResultSetCompleted>().Count());

        var failed = await RunAsync("SELECT 1; SELECT 1 / 0; SELECT 3;");
        Assert.Single(failed.OfType<RowBatchReceived>());
        Assert.Contains(failed, x => x is ExecutionFailed { Error.SqlState: "22012" });
        Assert.DoesNotContain(failed, x => x is ExecutionCompleted);
    }

    [PostgreSqlFact]
    public async Task CommandTimeoutAndAbortedTransactionReturnControlledFailuresAndRecover()
    {
        var timedOut = await RunAsync("SELECT pg_sleep(5);", new QueryExecutionOptions(commandTimeout: TimeSpan.FromSeconds(1)));
        Assert.Contains(timedOut, x => x is ExecutionFailed { Error.Kind: DatabaseErrorKind.Timeout });

        var aborted = await RunAsync("BEGIN; SELECT 1 / 0;");
        Assert.Contains(aborted, x => x is ExecutionFailed { Error.SqlState: "22012" });
        var recovery = await RunAsync("SELECT 42;");
        Assert.Contains(recovery, x => x is ExecutionCompleted);
    }

    [SeededPostgreSqlFact]
    public async Task UnusualPostgreSqlTypesStreamWithoutDestabilisingExecution()
    {
        var events = await RunAsync("""
            SELECT NULL::text AS null_value,
                   ''::text AS empty_value,
                   XMLPARSE(DOCUMENT '<root attr="value"/>') AS xml_value,
                   'NaN'::float8 AS nan_value,
                   'Infinity'::float8 AS positive_infinity,
                   '-Infinity'::float8 AS negative_infinity,
                   'infinity'::timestamptz AS timestamp_infinity,
                   '-infinity'::timestamp AS timestamp_negative_infinity,
                   '192.168.1.5/24'::inet AS network_value,
                   '192.168.0.0/16'::cidr AS cidr_value,
                   point(1.5, -2.5) AS point_value,
                   ROW(1, 'composite')::text AS composite_value,
                   'new'::"PMS Regression"."Status Type" AS enum_value;
            """);
        Assert.Contains(events, x => x is ExecutionCompleted);
        var row = Assert.Single(events.OfType<RowBatchReceived>().SelectMany(x => x.Batch.Rows));
        Assert.True(row.Cells[0].IsNull);
        Assert.Equal(string.Empty, row.Cells[1].Value);
        Assert.Equal(double.NaN, row.Cells[3].Value);
    }

    [PostgreSqlFact]
    public async Task LargeResultCanBeConsumedIntoBoundedClientStore()
    {
        await using var session = await new ResultExecutionService(
            new NpgsqlQueryExecutor(),
            new ResultStorageOptions(32 * 1024 * 1024, 16 * 1024 * 1024, 10_000))
            .ExecuteAndBuildAsync(
                new QueryRequest("SELECT generate_series(1, 25000) AS value", ConnectionString),
                CancellationToken.None);
        Assert.True(session.WasTruncated);
        Assert.Equal(10_000, session.RetainedRowCount);
        Assert.Equal(25_000, session.ReceivedRowCount);
        Assert.Equal(25_000, session.ResultSets[0].FinalRowCount);
    }

    [PostgreSqlFact]
    public async Task RowsAffectedAreTrackedSeparatelyFromDisplayedRows()
    {
        await using var session = await new ResultExecutionService(new NpgsqlQueryExecutor())
            .ExecuteAndBuildAsync(
                new QueryRequest("CREATE TEMP TABLE affected_rows(id integer); INSERT INTO affected_rows VALUES (1), (2), (3);", ConnectionString),
                CancellationToken.None);
        Assert.Equal(0, session.RetainedRowCount);
        Assert.Equal(3, session.RowsAffected);
    }

    [PostgreSqlFact]
    public async Task UserManagedTransactionDetectsAbortRequiresRollbackAndDisposesScope()
    {
        var executor = new NpgsqlQueryExecutor();
        var document = new QueryDocument(new ResultExecutionService(executor), "Transaction test")
        {
            ConnectionString = ConnectionString,
            ConnectionProfileId = "transaction-test",
            Database = Environment.GetEnvironmentVariable("PMS_TEST_DATABASE")!,
            TransactionMode = QueryTransactionMode.UserManaged,
        };
        try
        {
            document.SqlText = "BEGIN";
            Assert.Equal(ResultSessionStatus.Completed, (await document.ExecuteAsync())!.Status);

            document.SqlText = "SELECT 1 / 0";
            Assert.Equal("22012", (await document.ExecuteAsync())!.Error!.SqlState);

            document.SqlText = "SELECT 1";
            var aborted = await document.ExecuteAsync();
            Assert.Equal("25P02", aborted!.Error!.SqlState);
            Assert.Contains("aborted", aborted.Error.Message, StringComparison.OrdinalIgnoreCase);

            document.Database = "postgres";
            document.SqlText = "SELECT 1";
            var wrongContext = await document.ExecuteAsync();
            Assert.Equal(DatabaseErrorKind.Provider, wrongContext!.Error!.Kind);
            Assert.Contains("cannot change", wrongContext.Error.Message, StringComparison.OrdinalIgnoreCase);

            document.Database = Environment.GetEnvironmentVariable("PMS_TEST_DATABASE")!;
            document.SqlText = "ROLLBACK";
            Assert.Equal(ResultSessionStatus.Completed, (await document.ExecuteAsync())!.Status);

            document.SqlText = "SELECT 42";
            var recovered = await document.ExecuteAsync();
            var row = await recovered!.ResultSets[0].GetRowAsync(0, CancellationToken.None);
            Assert.Equal(42, row.Cells[0].Value);
        }
        finally
        {
            await document.DisposeAsync();
        }

        await using var observer = new Npgsql.NpgsqlConnection(ConnectionString);
        await observer.OpenAsync();
        await using var command = new Npgsql.NpgsqlCommand(
            "SELECT count(*) FROM pg_stat_activity WHERE application_name='PostgreManagementStudio - Transaction' AND xact_start IS NOT NULL",
            observer);
        Assert.Equal(0L, await command.ExecuteScalarAsync());
    }

    [PostgreSqlFact]
    public async Task CancellationDuringLargeResultStreamingIsTerminalAndRecoverable()
    {
        using var cancellation = new CancellationTokenSource();
        var events = new List<QueryExecutionEvent>();
        await foreach (var item in new NpgsqlQueryExecutor().ExecuteAsync(
            new QueryRequest("SELECT generate_series(1, 1000000)", ConnectionString, new QueryExecutionOptions(128)),
            cancellation.Token))
        {
            events.Add(item);
            if (item is RowBatchReceived) cancellation.Cancel();
        }
        Assert.Contains(events, x => x is ExecutionCancelled);
        Assert.DoesNotContain(events, x => x is ExecutionCompleted);
        Assert.True(events.OfType<RowBatchReceived>().Sum(x => x.Batch.Rows.Count) < 10_000);
        Assert.Contains(await RunAsync("SELECT 42"), x => x is ExecutionCompleted);
    }

    [PostgreSqlFact]
    public async Task BackendTerminationIsConnectionLossAndNeverAutoRetries()
    {
        var terminated = await RunAsync("SELECT pg_terminate_backend(pg_backend_pid());");
        Assert.Single(terminated.OfType<ExecutionStarted>());
        Assert.Contains(terminated, x => x is ExecutionFailed { Error.Kind: DatabaseErrorKind.ConnectionLost });
        Assert.DoesNotContain(terminated, x => x is ExecutionCompleted);
        Assert.Contains(await RunAsync("SELECT 42"), x => x is ExecutionCompleted);
    }

    [PostgreSqlFact]
    public async Task MissingDatabaseProducesActionableConnectionLossWithoutFallback()
    {
        var missingDatabase = "pms_missing_" + Guid.NewGuid().ToString("N");
        var profile = new Npgsql.NpgsqlConnectionStringBuilder(ConnectionString) { Database = missingDatabase, Timeout = 2 };
        var events = new List<QueryExecutionEvent>();
        await foreach (var item in new NpgsqlQueryExecutor().ExecuteAsync(new QueryRequest("SELECT 1", profile.ConnectionString)))
            events.Add(item);
        var failure = Assert.Single(events.OfType<ExecutionFailed>());
        Assert.Equal(DatabaseErrorKind.ConnectionLost, failure.Error.Kind);
        Assert.DoesNotContain(events, x => x is ExecutionCompleted);
    }

    [PostgreSqlFact]
    public async Task DocumentDatabaseSelectionOverridesProfileDatabaseBeforeExecution()
    {
        var intendedDatabase = Environment.GetEnvironmentVariable("PMS_TEST_DATABASE")!;
        var profile = new Npgsql.NpgsqlConnectionStringBuilder(ConnectionString) { Database = "postgres" };
        await using var document = new QueryDocument(new ResultExecutionService(new NpgsqlQueryExecutor()), "Context test")
        {
            ConnectionString = profile.ConnectionString,
            ConnectionProfileId = "context-test",
            Database = intendedDatabase,
            SqlText = "SELECT current_database()",
        };
        var session = await document.ExecuteAsync();
        var row = await session!.ResultSets[0].GetRowAsync(0, CancellationToken.None);
        Assert.Equal(intendedDatabase, row.Cells[0].Value);
        Assert.Equal(intendedDatabase, document.LastExecutionContext!.Database);
    }

    [PostgreSqlFact]
    public async Task TenConcurrentEditorExecutionsRemainIndependent()
    {
        var executions = Enumerable.Range(1, 10)
            .Select(index => RunAsync($"SELECT pg_sleep(0.05), {index} AS tab_value"))
            .ToArray();
        var results = await Task.WhenAll(executions);
        for (var index = 1; index <= results.Length; index++)
        {
            var row = Assert.Single(results[index - 1].OfType<RowBatchReceived>().SelectMany(x => x.Batch.Rows));
            Assert.Equal(index, row.Cells[1].Value);
            Assert.Contains(results[index - 1], x => x is ExecutionCompleted);
        }
    }

}
