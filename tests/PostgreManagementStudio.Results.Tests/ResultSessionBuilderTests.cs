using PostgreManagementStudio.Core;
using PostgreManagementStudio.Results;

namespace PostgreManagementStudio.Results.Tests;

public sealed class ResultSessionBuilderTests
{
    [Fact]
    public async Task MultipleResultSetsRemainIndependent()
    {
        var executor = new FakeQueryExecutor(new QueryExecutionEvent[]
        {
            new ExecutionStarted(DateTimeOffset.UtcNow),
            new ResultSetStarted(0, QueryEventFactory.Schema(new[] { "a" })),
            new RowBatchReceived(0, QueryEventFactory.Batch(0, QueryEventFactory.Row("a1"), QueryEventFactory.Row("a2"))),
            new ResultSetCompleted(0, 2),
            new ResultSetStarted(1, QueryEventFactory.Schema(new[] { "b" })),
            new RowBatchReceived(1, QueryEventFactory.Batch(0, QueryEventFactory.Row("b1"), QueryEventFactory.Row("b2"), QueryEventFactory.Row("b3"))),
            new ResultSetCompleted(1, 3),
            new ExecutionCompleted(TimeSpan.FromMilliseconds(1), 2)
        });
        var session = await new ResultSessionBuilder(executor).ExecuteAndBuildAsync(
            new QueryRequest("SELECT 1; SELECT 2;", "Host=stub"), CancellationToken.None);
        Assert.Equal(2, session.ResultSets.Count);
        Assert.Equal(2, session.ResultSets[0].LoadedRowCount);
        Assert.Equal(3, session.ResultSets[1].LoadedRowCount);
        Assert.Equal("a1", (await session.ResultSets[0].GetRowAsync(0, CancellationToken.None)).Cells[0].Value);
        Assert.Equal("b1", (await session.ResultSets[1].GetRowAsync(0, CancellationToken.None)).Cells[0].Value);
    }

    [Fact]
    public async Task ElapsedTimeIsCapturedOnCompletion()
    {
        var executor = new FakeQueryExecutor(new QueryExecutionEvent[]
        {
            new ExecutionStarted(DateTimeOffset.UtcNow),
            new ExecutionCompleted(TimeSpan.FromMilliseconds(123), 0)
        });
        var session = await new ResultSessionBuilder(executor).ExecuteAndBuildAsync(
            new QueryRequest("SELECT 1", "Host=stub"), CancellationToken.None);
        Assert.NotNull(session.Elapsed);
        Assert.Equal(123, session.Elapsed!.Value.TotalMilliseconds);
    }

    [Fact]
    public async Task CancellationAppliesToActiveStoresOnly()
    {
        var executor = new FakeQueryExecutor(new QueryExecutionEvent[]
        {
            new ExecutionStarted(DateTimeOffset.UtcNow),
            new ResultSetStarted(0, QueryEventFactory.Schema(new[] { "v" })),
            new RowBatchReceived(0, QueryEventFactory.Batch(0, QueryEventFactory.Row(1))),
            new ResultSetCompleted(0, 1),
            new ResultSetStarted(1, QueryEventFactory.Schema(new[] { "v" })),
            new RowBatchReceived(1, QueryEventFactory.Batch(0, QueryEventFactory.Row(2))),
            new ExecutionCancelled(TimeSpan.FromMilliseconds(1))
        });
        var session = await new ResultSessionBuilder(executor).ExecuteAndBuildAsync(
            new QueryRequest("SELECT 1; SELECT pg_sleep(10);", "Host=stub"), CancellationToken.None);
        Assert.Equal(ResultSetStatus.Completed, session.ResultSets[0].Status);
        Assert.Equal(ResultSetStatus.Cancelled, session.ResultSets[1].Status);
    }

    [Fact]
    public async Task SessionStatusBecomesCompletedAtEnd()
    {
        var executor = new FakeQueryExecutor(new QueryExecutionEvent[]
        {
            new ExecutionStarted(DateTimeOffset.UtcNow),
            new ResultSetStarted(0, QueryEventFactory.Schema(new[] { "v" })),
            new RowBatchReceived(0, QueryEventFactory.Batch(0, QueryEventFactory.Row(1))),
            new ResultSetCompleted(0, 1),
            new ExecutionCompleted(TimeSpan.FromMilliseconds(2), 1)
        });
        var session = await new ResultSessionBuilder(executor).ExecuteAndBuildAsync(
            new QueryRequest("SELECT 1", "Host=stub"), CancellationToken.None);
        Assert.Equal(ResultSessionStatus.Completed, session.Status);
    }
}