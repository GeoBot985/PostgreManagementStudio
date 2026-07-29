using System.Diagnostics;
using Npgsql;
using PostgreManagementStudio.Application;
using PostgreManagementStudio.Core;
using PostgreManagementStudio.Postgres;
using PostgreManagementStudio.Results;

namespace PostgreManagementStudio.IntegrationTests;

public sealed class PerformanceHardeningIntegrationTests
{
    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("PMS_CONNECTION_STRING") ??
        throw new InvalidOperationException("PMS_CONNECTION_STRING is required.");

    [LargeDatasetFact]
    public async Task LargeSchemaLoadsLazilyWithOneRoundTripPerExpandedLevel()
    {
        await using var explorer = new ObjectExplorerService(new NpgsqlMetadataProvider());
        var stopwatch = Stopwatch.StartNew();
        var root = await explorer.LoadRootAsync(ConnectionString,
            Environment.GetEnvironmentVariable("PMS_TEST_DATABASE")!);
        var schema = Assert.Single(root.Children, item => item.Name == "pms_perf_1");

        Assert.Equal(1, explorer.DatabaseRoundTrips);
        Assert.False(schema.IsLoaded);
        await explorer.ExpandAsync(schema);
        stopwatch.Stop();

        var tables = Assert.Single(schema.Children,
            item => item.Kind == ObjectExplorerNodeKind.Tables);
        Assert.True(tables.Children.Count >= 200);
        Assert.Equal(2, explorer.DatabaseRoundTrips);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10),
            $"Large-schema root and one expansion took {stopwatch.Elapsed}.");
    }

    [LargeDatasetFact]
    public async Task MillionRowTransferRetainsOnlyConfiguredDisplayPrefix()
    {
        var service = new ResultExecutionService(
            new NpgsqlQueryExecutor(),
            new ResultStorageOptions(
                maximumSessionMemoryBytes: 32L * 1024 * 1024,
                maximumResultSetMemoryBytes: 24L * 1024 * 1024,
                maximumRowsPerResultSet: 10_000));
        await using var session = await service.ExecuteAndBuildAsync(
            new QueryRequest(
                """
                SELECT row_number() OVER () AS row_id, source.md5_payload
                FROM (
                    SELECT seed, md5(seed::text) AS md5_payload
                    FROM pms_perf_1.million_row_source
                ) AS source
                CROSS JOIN pms_perf_1.million_row_source AS replica
                """,
                ConnectionString,
                new QueryExecutionOptions(rowBatchSize: 512)),
            CancellationToken.None);

        var store = Assert.Single(session.ResultSets);
        Assert.Equal(1_000_000, store.ReceivedRowCount);
        Assert.Equal(10_000, store.LoadedRowCount);
        Assert.True(store.WasTruncated);
        Assert.Equal(ResultTruncationReason.MaximumRowsReached, store.TruncationReason);
        Assert.True(session.EstimatedMemoryBytes <= 32L * 1024 * 1024);

        var page = await new ResultDisplayPageService().LoadAsync(store, 0);
        Assert.Equal(ResultDisplayPageService.DefaultPageSize, page.DisplayRows.Count);
        Assert.True(page.HasNext);
    }

    [LargeDatasetFact]
    public async Task RepeatedConnectionsQueriesAndDisposalStabilise()
    {
        var managedSamples = new List<long>();
        var handleSamples = new List<int>();
        for (var cycle = 0; cycle < 20; cycle++)
        {
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                "SELECT seed FROM pms_perf_1.million_row_source ORDER BY seed LIMIT 1000",
                connection);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) _ = reader.GetInt32(0);

            if (cycle % 5 == 4)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                managedSamples.Add(GC.GetTotalMemory(true));
                handleSamples.Add(Process.GetCurrentProcess().HandleCount);
            }
        }

        Assert.True(managedSamples[^1] <= managedSamples[0] + 16L * 1024 * 1024,
            $"Managed heap trend grew from {managedSamples[0]:N0} to {managedSamples[^1]:N0} bytes.");
        Assert.True(handleSamples[^1] <= handleSamples[0] + 32,
            $"Handle trend grew from {handleSamples[0]} to {handleSamples[^1]}.");
    }
}
