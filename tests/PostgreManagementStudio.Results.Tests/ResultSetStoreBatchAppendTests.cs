using System.Threading.Channels;
using PostgreManagementStudio.Core;
using PostgreManagementStudio.Results;

namespace PostgreManagementStudio.Results.Tests;

/// <summary>Direct tests on the internal <see cref="ResultSetStore"/> writer using a controlled schema.</summary>
public sealed class ResultSetStoreBatchAppendTests
{
    private static IResultSetWriter CreateWriter(out IResultSetStore store, ResultStorageOptions? options = null)
    {
        // Use a small internal helper to reach the internal writer/reader pair.
        var session = new TestSession(options);
        store = session.CreateStore(0, QueryEventFactory.Schema(new[] { "v" }));
        return session.Writer;
    }

    [Fact]
    public async Task FirstBatchStartsAtZero()
    {
        IResultSetStore store; var writer = CreateWriter(out store);
        await writer.AppendBatchAsync(QueryEventFactory.Batch(0, QueryEventFactory.Row(1), QueryEventFactory.Row(2)), CancellationToken.None);
        Assert.Equal(2, store.LoadedRowCount);
        Assert.Equal(2, store.ReceivedRowCount);
    }

    [Fact]
    public async Task ConsecutiveBatchesAppendCorrectly()
    {
        IResultSetStore store; var writer = CreateWriter(out store);
        await writer.AppendBatchAsync(QueryEventFactory.Batch(0, QueryEventFactory.Row(1), QueryEventFactory.Row(2)), CancellationToken.None);
        await writer.AppendBatchAsync(QueryEventFactory.Batch(2, QueryEventFactory.Row(3)), CancellationToken.None);
        await writer.AppendBatchAsync(QueryEventFactory.Batch(3, QueryEventFactory.Row(4), QueryEventFactory.Row(5)), CancellationToken.None);
        Assert.Equal(5, store.LoadedRowCount);
        Assert.Equal(5, store.ReceivedRowCount);
        var row4 = await store.GetRowAsync(3, CancellationToken.None);
        Assert.Equal(4, row4.Cells[0].Value);
    }

    [Fact]
    public async Task FinalPartialBatchAppendsCorrectly()
    {
        IResultSetStore store; var writer = CreateWriter(out store);
        await writer.AppendBatchAsync(QueryEventFactory.Batch(0, QueryEventFactory.Row(1), QueryEventFactory.Row(2)), CancellationToken.None);
        await writer.AppendBatchAsync(QueryEventFactory.Batch(2, QueryEventFactory.Row(3)), CancellationToken.None);
        Assert.Equal(3, store.LoadedRowCount);
        var row = await store.GetRowAsync(2, CancellationToken.None);
        Assert.Equal(3, row.Cells[0].Value);
    }

    [Fact]
    public async Task OverlappingBatchesAreRejected()
    {
        IResultSetStore store; var writer = CreateWriter(out store);
        await writer.AppendBatchAsync(QueryEventFactory.Batch(0, QueryEventFactory.Row(1)), CancellationToken.None);
        await Assert.ThrowsAsync<InvalidBatchException>(() => writer.AppendBatchAsync(
            QueryEventFactory.Batch(0, QueryEventFactory.Row(2)), CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task GappedBatchesAreRejected()
    {
        IResultSetStore store; var writer = CreateWriter(out store);
        await Assert.ThrowsAsync<InvalidBatchException>(() => writer.AppendBatchAsync(
            QueryEventFactory.Batch(5, QueryEventFactory.Row(1)), CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task AppendAfterCompletionIsRejected()
    {
        IResultSetStore store; var writer = CreateWriter(out store);
        await writer.AppendBatchAsync(QueryEventFactory.Batch(0, QueryEventFactory.Row(1)), CancellationToken.None);
        await writer.CompleteAsync(1, CancellationToken.None);
        await Assert.ThrowsAsync<ResultSetTerminalException>(() => writer.AppendBatchAsync(
            QueryEventFactory.Batch(1, QueryEventFactory.Row(2)), CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task AppendAfterCancellationIsRejected()
    {
        IResultSetStore store; var writer = CreateWriter(out store);
        await writer.AppendBatchAsync(QueryEventFactory.Batch(0, QueryEventFactory.Row(1)), CancellationToken.None);
        await writer.CancelAsync(CancellationToken.None);
        await Assert.ThrowsAsync<ResultSetTerminalException>(() => writer.AppendBatchAsync(
            QueryEventFactory.Batch(1, QueryEventFactory.Row(2)), CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task AppendAfterFailureIsRejected()
    {
        IResultSetStore store; var writer = CreateWriter(out store);
        await writer.AppendBatchAsync(QueryEventFactory.Batch(0, QueryEventFactory.Row(1)), CancellationToken.None);
        await writer.FailAsync(new DatabaseError("oops", "ERROR", "42000", null, null, null, null, null, null, null, null), CancellationToken.None);
        await Assert.ThrowsAsync<ResultSetTerminalException>(() => writer.AppendBatchAsync(
            QueryEventFactory.Batch(1, QueryEventFactory.Row(2)), CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task AppendAfterDisposalIsRejected()
    {
        IResultSetStore store; var writer = CreateWriter(out store);
        await writer.AppendBatchAsync(QueryEventFactory.Batch(0, QueryEventFactory.Row(1)), CancellationToken.None);
        await store.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedResultStoreException>(() => store.GetRowAsync(0, CancellationToken.None).AsTask());
        await Assert.ThrowsAsync<ObjectDisposedResultStoreException>(() => writer.AppendBatchAsync(
            QueryEventFactory.Batch(1, QueryEventFactory.Row(2)), CancellationToken.None).AsTask());
    }
}

/// <summary>Internal test-only session factory: bypasses the public builder to give direct store/writer access.</summary>
internal sealed class TestSession
{
    private readonly ResultSession _session;
    public TestSession(ResultStorageOptions? options = null) { _session = new ResultSession(options ?? ResultStorageOptions.Default, logger: null); }
    public ResultSession Session => _session;
    public IResultSetStore CreateStore(int idx, ResultSetSchema schema) => _session.CreateStore(idx, schema);
    public IResultSetWriter Writer => _session.GetWriter(0);
}