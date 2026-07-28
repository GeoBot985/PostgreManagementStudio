using PostgreManagementStudio.Core;
using PostgreManagementStudio.Results;

namespace PostgreManagementStudio.Results.Tests;

public sealed class ResultSetStoreTruncationTests
{
    [Fact]
    public async Task RowLimitTriggersTruncation()
    {
        var session = new TestSession(new ResultStorageOptions(long.MaxValue, long.MaxValue, 50));
        var store = session.CreateStore(0, QueryEventFactory.Schema(new[] { "v" }));
        var writer = session.Writer;
        await writer.AppendBatchAsync(QueryEventFactory.Batch(0, Enumerable.Range(1, 50).Select(i => QueryEventFactory.Row(i)).ToArray()), CancellationToken.None);
        await writer.AppendBatchAsync(QueryEventFactory.Batch(50, QueryEventFactory.Row(51), QueryEventFactory.Row(52)), CancellationToken.None);
        Assert.True(store.WasTruncated);
        Assert.Equal(ResultTruncationReason.MaximumRowsReached, store.TruncationReason);
        Assert.Equal(50, store.LoadedRowCount);
        Assert.Equal(52, store.ReceivedRowCount);
    }

    [Fact]
    public async Task ResultSetMemoryLimitTriggersTruncation()
    {
        // Choose a tiny memory limit so any batch with strings triggers it.
        var session = new TestSession(new ResultStorageOptions(maximumSessionMemoryBytes: long.MaxValue, maximumResultSetMemoryBytes: 256, maximumRowsPerResultSet: long.MaxValue));
        var store = session.CreateStore(0, QueryEventFactory.Schema(new[] { "v" }));
        var writer = session.Writer;
        await writer.AppendBatchAsync(QueryEventFactory.Batch(0, QueryEventFactory.Row(new string('x', 64))), CancellationToken.None);
        await writer.AppendBatchAsync(QueryEventFactory.Batch(1, QueryEventFactory.Row(new string('y', 64))), CancellationToken.None);
        Assert.True(store.WasTruncated);
        Assert.Equal(ResultTruncationReason.ResultSetMemoryLimitReached, store.TruncationReason);
    }

    [Fact]
    public async Task SessionMemoryLimitTriggersTruncation()
    {
        // Allow the per-result-set limit to be high; force the session limit to fire instead.
        var session = new TestSession(new ResultStorageOptions(maximumSessionMemoryBytes: 512, maximumResultSetMemoryBytes: 1024 * 1024, maximumRowsPerResultSet: long.MaxValue));
        var store = session.CreateStore(0, QueryEventFactory.Schema(new[] { "v" }));
        var writer = session.Writer;
        // Build a fake session so the builder wires up session aggregate checks. Use the real builder.
        var builder = new ResultSessionBuilder(new FakeQueryExecutor(new QueryExecutionEvent[] {
            new ExecutionStarted(DateTimeOffset.UtcNow),
            new ResultSetStarted(0, QueryEventFactory.Schema(new[] { "v" })),
            new RowBatchReceived(0, QueryEventFactory.Batch(0, QueryEventFactory.Row(new string('a', 32)))),
            new RowBatchReceived(0, QueryEventFactory.Batch(1, QueryEventFactory.Row(new string('b', 32)))),
            new RowBatchReceived(0, QueryEventFactory.Batch(2, QueryEventFactory.Row(new string('c', 32)))),
            new RowBatchReceived(0, QueryEventFactory.Batch(3, QueryEventFactory.Row(new string('d', 32)))),
            new ResultSetCompleted(0, 4),
            new ExecutionCompleted(TimeSpan.FromMilliseconds(1), 1)
        }), new ResultStorageOptions(maximumSessionMemoryBytes: 256, maximumResultSetMemoryBytes: 1024 * 1024, maximumRowsPerResultSet: long.MaxValue), logger: null);
        var builtSession = await builder.ExecuteAndBuildAsync(new QueryRequest("SELECT 'x';", "Host=stub"), CancellationToken.None);
        Assert.True(builtSession.WasTruncated);
        Assert.Equal(ResultTruncationReason.SessionMemoryLimitReached, builtSession.TruncationReason);
        // Retained rows are bounded by the truncation; received exceeds retained.
        Assert.True(builtSession.ReceivedRowCount >= builtSession.RetainedRowCount);
    }

    [Fact]
    public async Task RetentionStopsAfterTruncation()
    {
        var session = new TestSession(new ResultStorageOptions(long.MaxValue, long.MaxValue, 10));
        var store = session.CreateStore(0, QueryEventFactory.Schema(new[] { "v" }));
        var writer = session.Writer;
        await writer.AppendBatchAsync(QueryEventFactory.Batch(0, Enumerable.Range(1, 10).Select(i => QueryEventFactory.Row(i)).ToArray()), CancellationToken.None);
        Assert.Equal(10, store.LoadedRowCount);
        // Next batch starts at the next index but must NOT be retained.
        await writer.AppendBatchAsync(QueryEventFactory.Batch(10, QueryEventFactory.Row(11), QueryEventFactory.Row(12)), CancellationToken.None);
        Assert.Equal(10, store.LoadedRowCount);
        Assert.Equal(12, store.ReceivedRowCount);
    }

    [Fact]
    public async Task DisposalReleasesAccounting()
    {
        var session = new TestSession(new ResultStorageOptions(256 * 1024 * 1024, 128 * 1024 * 1024, 1_000_000));
        var store = (IResultSetStore)session.CreateStore(0, QueryEventFactory.Schema(new[] { "v" }));
        await store.DisposeAsync();
        Assert.Equal(0, store.EstimatedMemoryBytes);
    }
}