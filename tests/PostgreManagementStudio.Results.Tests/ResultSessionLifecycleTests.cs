using PostgreManagementStudio.Core;
using PostgreManagementStudio.Results;

namespace PostgreManagementStudio.Results.Tests;

public sealed class ResultSessionLifecycleTests
{
    [Fact]
    public async Task CancelledSessionPreservesRetainedRows()
    {
        var executor = new FakeQueryExecutor(new QueryExecutionEvent[]
        {
            new ExecutionStarted(DateTimeOffset.UtcNow),
            new ResultSetStarted(0, QueryEventFactory.Schema(new[] { "v" })),
            new RowBatchReceived(0, QueryEventFactory.Batch(0, QueryEventFactory.Row(1), QueryEventFactory.Row(2))),
            new RowBatchReceived(0, QueryEventFactory.Batch(2, QueryEventFactory.Row(3))),
            new ExecutionCancelled(TimeSpan.FromMilliseconds(2))
        });
        var session = await new ResultSessionBuilder(executor).ExecuteAndBuildAsync(
            new QueryRequest("SELECT 1", "Host=stub"), CancellationToken.None);
        Assert.Equal(ResultSessionStatus.Cancelled, session.Status);
        Assert.Single(session.ResultSets);
        var store = session.ResultSets[0];
        Assert.Equal(3, store.LoadedRowCount);
        var row = await store.GetRowAsync(1, CancellationToken.None);
        Assert.Equal(2, row.Cells[0].Value);
    }

    [Fact]
    public async Task FailureAfterEarlierResultKeepsEarlierSetReadable()
    {
        var executor = new FakeQueryExecutor(new QueryExecutionEvent[]
        {
            new ExecutionStarted(DateTimeOffset.UtcNow),
            new ResultSetStarted(0, QueryEventFactory.Schema(new[] { "v" })),
            new RowBatchReceived(0, QueryEventFactory.Batch(0, QueryEventFactory.Row(1))),
            new ResultSetCompleted(0, 1),
            new ResultSetStarted(1, QueryEventFactory.Schema(new[] { "v" })),
            new ExecutionFailed(new DatabaseError("missing table", "ERROR", "42P01", null, null, null, null, null, null, null, null))
        });
        var session = await new ResultSessionBuilder(executor).ExecuteAndBuildAsync(
            new QueryRequest("SELECT 1; SELECT * FROM missing;", "Host=stub"), CancellationToken.None);
        Assert.Equal(ResultSessionStatus.Failed, session.Status);
        Assert.Equal(2, session.ResultSets.Count);
        Assert.Equal(ResultSetStatus.Completed, session.ResultSets[0].Status);
        Assert.Equal(ResultSetStatus.Failed, session.ResultSets[1].Status);
        Assert.NotNull(session.Error);
        Assert.Equal("42P01", session.Error!.SqlState);
        // Earlier set rows remain readable.
        var firstRow = await session.ResultSets[0].GetRowAsync(0, CancellationToken.None);
        Assert.Equal(1, firstRow.Cells[0].Value);
    }

    [Fact]
    public async Task DisposalIsIdempotent()
    {
        var executor = new FakeQueryExecutor(new QueryExecutionEvent[]
        {
            new ExecutionStarted(DateTimeOffset.UtcNow),
            new ExecutionCompleted(TimeSpan.Zero, 0)
        });
        var session = await new ResultSessionBuilder(executor).ExecuteAndBuildAsync(
            new QueryRequest("SELECT 1", "Host=stub"), CancellationToken.None);
        await session.DisposeAsync();
        await session.DisposeAsync(); // no throw
        Assert.Equal(ResultSessionStatus.Disposed, session.Status);
    }

    [Fact]
    public async Task PartialResultsSurviveCancellation_NewSessionWorks()
    {
        var executor1 = new FakeQueryExecutor(new QueryExecutionEvent[]
        {
            new ExecutionStarted(DateTimeOffset.UtcNow),
            new ResultSetStarted(0, QueryEventFactory.Schema(new[] { "v" })),
            new RowBatchReceived(0, QueryEventFactory.Batch(0, QueryEventFactory.Row(1))),
            new ExecutionCancelled(TimeSpan.FromMilliseconds(1))
        });
        var s1 = await new ResultSessionBuilder(executor1).ExecuteAndBuildAsync(
            new QueryRequest("SELECT 1", "Host=stub"), CancellationToken.None);
        var executor2 = new FakeQueryExecutor(new QueryExecutionEvent[]
        {
            new ExecutionStarted(DateTimeOffset.UtcNow),
            new ResultSetStarted(0, QueryEventFactory.Schema(new[] { "v" })),
            new RowBatchReceived(0, QueryEventFactory.Batch(0, QueryEventFactory.Row(42))),
            new ResultSetCompleted(0, 1),
            new ExecutionCompleted(TimeSpan.FromMilliseconds(1), 1)
        });
        var s2 = await new ResultSessionBuilder(executor2).ExecuteAndBuildAsync(
            new QueryRequest("SELECT 42", "Host=stub"), CancellationToken.None);
        Assert.Equal(ResultSessionStatus.Cancelled, s1.Status);
        Assert.Equal(ResultSessionStatus.Completed, s2.Status);
        Assert.Equal(42, (await s2.ResultSets[0].GetRowAsync(0, CancellationToken.None)).Cells[0].Value);
    }

    [Fact]
    public void InvalidStateTransitionsThrow()
    {
        Assert.False(LifecycleGuards.IsValid(ResultSetStatus.Completed, ResultSetStatus.Receiving));
        Assert.False(LifecycleGuards.IsValid(ResultSessionStatus.Completed, ResultSessionStatus.Failed));
        Assert.True(LifecycleGuards.IsValid(ResultSetStatus.Created, ResultSetStatus.Disposed));
        Assert.True(LifecycleGuards.IsValid(ResultSessionStatus.Running, ResultSessionStatus.Cancelled));
    }

    [Fact]
    public async Task NoticesAccumulate()
    {
        var executor = new FakeQueryExecutor(new QueryExecutionEvent[]
        {
            new ExecutionStarted(DateTimeOffset.UtcNow),
            new DatabaseNoticeReceived(new DatabaseNotice("INFO", "00000", "hello", null, null, DateTimeOffset.UtcNow)),
            new ExecutionCompleted(TimeSpan.FromMilliseconds(1), 0)
        });
        var session = await new ResultSessionBuilder(executor).ExecuteAndBuildAsync(
            new QueryRequest("DO $$ BEGIN RAISE NOTICE 'hello'; END $$;", "Host=stub"), CancellationToken.None);
        Assert.Single(session.Notices);
        Assert.Equal("hello", session.Notices[0].Message);
    }

    [Fact]
    public async Task DuplicateResultSetIndexRejected()
    {
        var executor = new FakeQueryExecutor(new QueryExecutionEvent[]
        {
            new ExecutionStarted(DateTimeOffset.UtcNow),
            new ResultSetStarted(0, QueryEventFactory.Schema(new[] { "v" })),
            new ResultSetStarted(0, QueryEventFactory.Schema(new[] { "v" })),
            new ExecutionCompleted(TimeSpan.FromMilliseconds(1), 1)
        });
        await Assert.ThrowsAsync<DuplicateResultSetIndexException>(async () =>
            await new ResultSessionBuilder(executor).ExecuteAndBuildAsync(
                new QueryRequest("SELECT 1", "Host=stub"), CancellationToken.None));
    }

    [Fact]
    public async Task EarlierCompletedStoresRemainCompletedAfterLaterFailure()
    {
        var executor = new FakeQueryExecutor(new QueryExecutionEvent[]
        {
            new ExecutionStarted(DateTimeOffset.UtcNow),
            new ResultSetStarted(0, QueryEventFactory.Schema(new[] { "v" })),
            new RowBatchReceived(0, QueryEventFactory.Batch(0, QueryEventFactory.Row(1))),
            new ResultSetCompleted(0, 1),
            new ResultSetStarted(1, QueryEventFactory.Schema(new[] { "v" })),
            new ExecutionFailed(new DatabaseError("oops", "ERROR", "42000", null, null, null, null, null, null, null, null))
        });
        var session = await new ResultSessionBuilder(executor).ExecuteAndBuildAsync(
            new QueryRequest("SELECT 1; SELECT * FROM missing;", "Host=stub"), CancellationToken.None);
        Assert.Equal(ResultSetStatus.Completed, session.ResultSets[0].Status);
        Assert.Equal(ResultSetStatus.Failed, session.ResultSets[1].Status);
    }
}