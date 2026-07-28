using PostgreManagementStudio.Application;
using PostgreManagementStudio.Core;
using PostgreManagementStudio.Postgres;

namespace PostgreManagementStudio.IntegrationTests;

public sealed class ResultStorageIntegrationTests
{
    private static string? ConnectionString =>
        Environment.GetEnvironmentVariable("PMS_CONNECTION_STRING");

    private static bool Skip() => string.IsNullOrWhiteSpace(ConnectionString);

    private static Task<IResultSession> ExecuteAsync(string sql, ResultStorageOptions? options = null, CancellationToken ct = default)
    {
        var service = new ResultExecutionService(new NpgsqlQueryExecutor(), options);
        return service.ExecuteAndBuildAsync(new QueryRequest(sql, ConnectionString!), ct);
    }

    [Fact]
    public async Task IncrementalArrival_10kRows()
    {
        if (Skip()) return;
        var session = await ExecuteAsync("SELECT generate_series(1, 10000) AS value;", new ResultStorageOptions(long.MaxValue, long.MaxValue, 100_000));
        Assert.Equal(ResultSessionStatus.Completed, session.Status);
        Assert.Single(session.ResultSets);
        var store = session.ResultSets[0];
        Assert.Equal(10_000, store.LoadedRowCount);
        Assert.Equal(10_000, store.ReceivedRowCount);
        Assert.Equal(10_000, store.FinalRowCount);
        var middle = await store.GetRowAsync(5_000, CancellationToken.None);
        Assert.Equal(5_001L, middle.Cells[0].Value);
    }

    [Fact]
    public async Task MultipleResultSets_RoutingAndSchemas()
    {
        if (Skip()) return;
        var session = await ExecuteAsync("SELECT generate_series(1, 10) AS first_value; SELECT generate_series(101, 120) AS second_value;");
        Assert.Equal(2, session.ResultSets.Count);
        Assert.Equal(10, session.ResultSets[0].LoadedRowCount);
        Assert.Equal(20, session.ResultSets[1].LoadedRowCount);
        Assert.Single(session.ResultSets[0].Schema.Columns);
        Assert.Equal("first_value", session.ResultSets[0].Schema.Columns[0].Name);
        Assert.Equal("second_value", session.ResultSets[1].Schema.Columns[0].Name);
        var secondFirst = await session.ResultSets[1].GetRowAsync(0, CancellationToken.None);
        Assert.Equal(101L, secondFirst.Cells[0].Value);
    }

    [Fact]
    public async Task MixedCommandAndResultSets_DoNotCreateFalseStore()
    {
        if (Skip()) return;
        var session = await ExecuteAsync(
            "CREATE TEMP TABLE sprint002_test(id integer); INSERT INTO sprint002_test VALUES (1), (2), (3); SELECT * FROM sprint002_test ORDER BY id;");
        Assert.Single(session.ResultSets);
        Assert.Equal(3, session.ResultSets[0].LoadedRowCount);
        Assert.Equal(1L, (await session.ResultSets[0].GetRowAsync(0, CancellationToken.None)).Cells[0].Value);
        Assert.Equal(3L, (await session.ResultSets[0].GetRowAsync(2, CancellationToken.None)).Cells[0].Value);
    }

    [Fact]
    public async Task CancellationWithPartialRows_RowsRemainReadable_AndFreshSessionWorks()
    {
        if (Skip()) return;
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var session = await ExecuteAsync(
            "SELECT value FROM generate_series(1, 1000) AS value CROSS JOIN LATERAL pg_sleep(0.01);",
            options: null,
            ct: cts.Token);
        Assert.Equal(ResultSessionStatus.Cancelled, session.Status);
        Assert.Single(session.ResultSets);
        var store = session.ResultSets[0];
        Assert.True(store.LoadedRowCount > 0, $"Expected at least one retained row; loaded={store.LoadedRowCount}");
        var row = await store.GetRowAsync(0, CancellationToken.None);
        Assert.NotNull(row);
        var recovery = await ExecuteAsync("SELECT 42;");
        Assert.Equal(ResultSessionStatus.Completed, recovery.Status);
        Assert.Equal(42L, (await recovery.ResultSets[0].GetRowAsync(0, CancellationToken.None)).Cells[0].Value);
    }

    [Fact]
    public async Task FailureAfterEarlierResult_FirstSetRemainsReadable()
    {
        if (Skip()) return;
        var session = await ExecuteAsync("SELECT 1 AS successful_value; SELECT * FROM sprint002_missing_table;");
        Assert.Equal(ResultSessionStatus.Failed, session.Status);
        Assert.Equal(2, session.ResultSets.Count);
        Assert.Equal(ResultSetStatus.Completed, session.ResultSets[0].Status);
        Assert.Equal(ResultSetStatus.Failed, session.ResultSets[1].Status);
        Assert.NotNull(session.Error);
        Assert.Equal("42P01", session.Error!.SqlState);
        var firstRow = await session.ResultSets[0].GetRowAsync(0, CancellationToken.None);
        Assert.Equal(1L, firstRow.Cells[0].Value);
    }

    [Fact]
    public async Task RowLimitTruncation_StopsAtLimit_AndSessionCompleted()
    {
        if (Skip()) return;
        var options = new ResultStorageOptions(maximumSessionMemoryBytes: 1024L * 1024 * 1024, maximumResultSetMemoryBytes: 1024L * 1024 * 1024, maximumRowsPerResultSet: 100);
        var session = await ExecuteAsync("SELECT generate_series(1, 10000) AS value;", options);
        Assert.Equal(ResultSessionStatus.Completed, session.Status);
        Assert.True(session.WasTruncated);
        Assert.Equal(ResultTruncationReason.MaximumRowsReached, session.TruncationReason);
        Assert.Equal(100, session.ResultSets[0].LoadedRowCount);
        Assert.Equal(10_000, session.ResultSets[0].ReceivedRowCount);
        Assert.Equal(10_000, session.ResultSets[0].FinalRowCount);
        Assert.True(session.EstimatedMemoryBytes < 1024L * 1024 * 1024);
    }

    [Fact]
    public async Task LargeValues_NoDisplayFormattingInStorage()
    {
        if (Skip()) return;
        var session = await ExecuteAsync(
            "SELECT g AS id, repeat('x', 10000) AS large_text FROM generate_series(1, 10) AS g;");
        Assert.Equal(ResultSessionStatus.Completed, session.Status);
        Assert.Equal(10, session.ResultSets[0].LoadedRowCount);
        var row = await session.ResultSets[0].GetRowAsync(0, CancellationToken.None);
        var text = Assert.IsType<string>(row.Cells[1].Value);
        Assert.Equal(10_000, text.Length);
        await session.DisposeAsync();
        Assert.Equal(0, session.EstimatedMemoryBytes);
    }
}