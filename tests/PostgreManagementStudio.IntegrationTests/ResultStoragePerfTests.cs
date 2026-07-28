using System.Diagnostics;
using System.Text;
using PostgreManagementStudio.Application;
using PostgreManagementStudio.Core;
using PostgreManagementStudio.Postgres;
using PostgreManagementStudio.Results;

namespace PostgreManagementStudio.IntegrationTests;

/// <summary>
/// Performance and memory measurements for the Sprint 002 result-store.
/// Gated by the <c>PMS_RUN_PERF</c> environment variable so that default
/// <c>dotnet test</c> invocations stay fast.
/// </summary>
public sealed class ResultStoragePerfTests
{
    private static bool PerfEnabled() =>
        string.Equals(Environment.GetEnvironmentVariable("PMS_RUN_PERF"), "1", StringComparison.Ordinal);

    private static string? ConnectionString =>
        Environment.GetEnvironmentVariable("PMS_CONNECTION_STRING");

    private static async Task<IResultSession> RunAsync(string sql)
    {
        var service = new ResultExecutionService(new NpgsqlQueryExecutor(), ResultStorageOptions.Default);
        return await service.ExecuteAndBuildAsync(new QueryRequest(sql, ConnectionString!), CancellationToken.None);
    }

    private static string FormatRow(IReadOnlyList<ResultCell> cells) =>
        string.Join(" | ", cells.Select(c => c.IsNull ? "NULL" : (c.Value?.ToString() ?? "")));

    [Fact]
    public async Task FirstBatchReadableBeforeCompletion_100k()
    {
        if (!PerfEnabled() || string.IsNullOrWhiteSpace(ConnectionString)) return;
        // The builder consumes the executor's channel and only returns the session after
        // ExecutionCompleted. To assert first-batch-readability we run the executor directly and
        // observe the RowBatchReceived events while the builder is awaiting the full stream.
        var request = new QueryRequest("SELECT generate_series(1, 100000) AS value;", ConnectionString!,
            new QueryExecutionOptions(rowBatchSize: 256));
        var executor = new NpgsqlQueryExecutor();
        long firstBatchAtMs = -1;
        long completionAtMs = -1;
        var wallclock = Stopwatch.StartNew();

        var observeTask = Task.Run(async () =>
        {
            await foreach (var ev in executor.ExecuteAsync(request, CancellationToken.None))
            {
                if (firstBatchAtMs < 0 && ev is RowBatchReceived)
                    firstBatchAtMs = wallclock.ElapsedMilliseconds;
                if (ev is ExecutionCompleted)
                    completionAtMs = wallclock.ElapsedMilliseconds;
            }
        });

        await observeTask;
        Assert.True(firstBatchAtMs >= 0, "First batch was never observed.");
        Assert.True(completionAtMs > 0, "Execution never completed.");
        // The first batch must arrive before the execution completes — proves streamed arrival.
        Assert.True(firstBatchAtMs < completionAtMs, $"first={firstBatchAtMs}ms, complete={completionAtMs}ms");
    }

    [Fact]
    public async Task MemoryBounded_100kScalar()
    {
        if (!PerfEnabled() || string.IsNullOrWhiteSpace(ConnectionString)) return;
        var session = await RunAsync("SELECT generate_series(1, 100000) AS value;");
        Assert.Equal(100_000, session.ResultSets[0].LoadedRowCount);
        // Estimate each cell as BoxedIntBytes + row overhead. Should be comfortably below 100 MiB.
        Assert.True(session.EstimatedMemoryBytes < 100L * 1024 * 1024,
            $"Estimated memory {session.EstimatedMemoryBytes} exceeds 100 MiB bound.");
    }

    [Fact]
    public async Task LookupLatency_Sublinear_100k()
    {
        if (!PerfEnabled() || string.IsNullOrWhiteSpace(ConnectionString)) return;
        var session = await RunAsync("SELECT g AS id, md5(g::text) AS hash_value, repeat('x', 100) AS text_value FROM generate_series(1, 100000) AS g;");
        var store = session.ResultSets[0];
        Assert.Equal(100_000, store.LoadedRowCount);

        var rng = new Random(42);
        var samples = Enumerable.Range(0, 1_000)
            .Select(_ => rng.NextInt64(0, 100_000))
            .ToArray();
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < samples.Length; i++)
        {
            var row = await store.GetRowAsync(samples[i], CancellationToken.None);
            Assert.NotNull(row);
        }
        sw.Stop();
        var avgUs = sw.Elapsed.TotalMilliseconds * 1000.0 / samples.Length;
        // Average lookup under 200 microseconds — well under the spec's "effectively constant" bar.
        Assert.True(avgUs < 200, $"Average lookup {avgUs:F1} µs exceeds 200 µs target.");
    }

    [Fact]
    public async Task RangeRetrieval_100Rows_100k()
    {
        if (!PerfEnabled() || string.IsNullOrWhiteSpace(ConnectionString)) return;
        var session = await RunAsync("SELECT generate_series(1, 100000) AS value;");
        var store = session.ResultSets[0];
        var sw = Stopwatch.StartNew();
        var range = await store.GetRowsAsync(50_000, 100, CancellationToken.None);
        sw.Stop();
        Assert.Equal(100, range.Count);
        Assert.Equal(50_001L, range[0].Cells[0].Value);
        Assert.True(sw.ElapsedMilliseconds < 100, $"Range retrieval took {sw.ElapsedMilliseconds} ms (target < 100 ms).");
    }

    [Fact]
    public async Task DisposalFast_100k()
    {
        if (!PerfEnabled() || string.IsNullOrWhiteSpace(ConnectionString)) return;
        var session = await RunAsync("SELECT generate_series(1, 100000) AS value;");
        var sw = Stopwatch.StartNew();
        await session.DisposeAsync();
        sw.Stop();
        Assert.Equal(0, session.EstimatedMemoryBytes);
        Assert.True(sw.ElapsedMilliseconds < 1_000, $"Disposal took {sw.ElapsedMilliseconds} ms (target < 1 s).");
    }

    [Fact]
    public async Task WritesReportSummary()
    {
        if (!PerfEnabled() || string.IsNullOrWhiteSpace(ConnectionString)) return;
        var report = new StringBuilder();
        report.AppendLine("Sprint 002 perf report");
        report.AppendLine($"Generated: {DateTimeOffset.UtcNow:O}");
        report.AppendLine();

        foreach (var (label, sql) in new (string, string)[]
        {
            ("100k scalar", "SELECT generate_series(1, 100000) AS value;"),
            ("100k mixed", "SELECT g AS id, md5(g::text) AS hash_value, repeat('x', 100) AS text_value FROM generate_series(1, 100000) AS g;")
        })
        {
            var sw = Stopwatch.StartNew();
            var session = await RunAsync(sql);
            sw.Stop();
            var store = session.ResultSets[0];
            var firstRow = await store.GetRowAsync(0, CancellationToken.None);
            var middleRow = await store.GetRowAsync(store.LoadedRowCount / 2, CancellationToken.None);
            var lastRow = await store.GetRowAsync(store.LoadedRowCount - 1, CancellationToken.None);
            var rng = new Random(7);
            var latencies = new long[500];
            for (int i = 0; i < latencies.Length; i++)
            {
                var sw2 = Stopwatch.StartNew();
                await store.GetRowAsync(rng.NextInt64(0, store.LoadedRowCount), CancellationToken.None);
                sw2.Stop();
                latencies[i] = sw2.Elapsed.Ticks;
            }
            Array.Sort(latencies);
            var medianUs = latencies[latencies.Length / 2] * 1_000_000.0 / Stopwatch.Frequency;
            var rangeSw = Stopwatch.StartNew();
            var range = await store.GetRowsAsync(store.LoadedRowCount / 2, 100, CancellationToken.None);
            rangeSw.Stop();

            report.AppendLine($"--- {label} ---");
            report.AppendLine($"Total execution+build: {sw.ElapsedMilliseconds} ms");
            report.AppendLine($"Loaded rows: {store.LoadedRowCount}");
            report.AppendLine($"Estimated memory: {session.EstimatedMemoryBytes:N0} bytes");
            report.AppendLine($"First row sample: {FormatRow(firstRow.Cells)}");
            report.AppendLine($"Middle row sample: {FormatRow(middleRow.Cells)}");
            report.AppendLine($"Last row sample: {FormatRow(lastRow.Cells)}");
            report.AppendLine($"Median lookup latency: {medianUs:F1} µs");
            report.AppendLine($"Range 100 (mid): {rangeSw.ElapsedMilliseconds} ms, returned {range.Count}");
            await session.DisposeAsync();
            report.AppendLine();
        }

        var mirrorDir = Environment.GetEnvironmentVariable("PMS_PERF_REPORT_DIR");
        if (string.IsNullOrEmpty(mirrorDir)) mirrorDir = Path.Combine(Directory.GetCurrentDirectory());
        var mirror = Path.Combine(mirrorDir, "perf-report.txt");
        Directory.CreateDirectory(mirrorDir);
        await File.WriteAllTextAsync(mirror, report.ToString());
        Assert.True(File.Exists(mirror), $"Performance report was not written to {mirror}.");
    }
}