using PostgreManagementStudio.Core;
using PostgreManagementStudio.Postgres;

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

}
