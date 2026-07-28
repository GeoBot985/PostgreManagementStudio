using PostgreManagementStudio.Core;
using PostgreManagementStudio.Results;

namespace PostgreManagementStudio.Results.Tests;

public sealed class ResultSetStoreConcurrencyTests
{
    [Fact]
    public async Task ReaderAndWriterConcurrency_NoCorruption()
    {
        var session = new TestSession(new ResultStorageOptions(long.MaxValue, long.MaxValue, 100_000));
        var store = (IResultSetStore)session.CreateStore(0, QueryEventFactory.Schema(new[] { "v" }));
        var writer = session.Writer;

        using var start = new ManualResetEventSlim(false);
        var writerTask = Task.Run(async () =>
        {
            start.Wait();
            for (int i = 0; i < 200; i++)
            {
                await writer.AppendBatchAsync(
                    QueryEventFactory.Batch(i * 50, Enumerable.Range(0, 50).Select(j => QueryEventFactory.Row(i * 50 + j + 1)).ToArray()),
                    CancellationToken.None);
            }
            await writer.CompleteAsync(200 * 50, CancellationToken.None);
        });

        var readTasks = Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
        {
            start.Wait();
            for (int i = 0; i < 100; i++)
            {
                var range = await store.GetRowsAsync(0, 100, CancellationToken.None);
                // Verify row order when rows are present.
                if (range.Count > 1)
                {
                    for (int j = 1; j < range.Count; j++)
                    {
                        var prev = (int)range[j - 1].Cells[0].Value!;
                        var curr = (int)range[j].Cells[0].Value!;
                        Assert.True(curr > prev, $"Rows out of order at j={j}: {prev} vs {curr}");
                    }
                }
            }
        })).ToArray();

        start.Set();
        await Task.WhenAll(readTasks.Append(writerTask));
        Assert.Equal(10_000, store.LoadedRowCount);
        var last = await store.GetRowAsync(9_999, CancellationToken.None);
        Assert.Equal(10_000, last.Cells[0].Value);
    }

    [Fact]
    public async Task DisposeDuringRead_FailsCleanly()
    {
        var session = new TestSession();
        var store = (IResultSetStore)session.CreateStore(0, QueryEventFactory.Schema(new[] { "v" }));
        var writer = session.Writer;
        await writer.AppendBatchAsync(QueryEventFactory.Batch(0, QueryEventFactory.Row(1)), CancellationToken.None);

        using var cts = new CancellationTokenSource();
        var readTask = store.GetRowsAsync(0, 1, cts.Token).AsTask();
        await store.DisposeAsync();
        // After disposal a fresh read fails; the in-flight read may still complete if it captured the snapshot.
        await Assert.ThrowsAsync<ObjectDisposedResultStoreException>(async () =>
        {
            await store.GetRowsAsync(0, 1, CancellationToken.None);
        });
        cts.Cancel();
    }

    [Fact]
    public async Task CancellationDuringRead_Honored()
    {
        var session = new TestSession();
        var store = (IResultSetStore)session.CreateStore(0, QueryEventFactory.Schema(new[] { "v" }));
        var writer = session.Writer;
        await writer.AppendBatchAsync(QueryEventFactory.Batch(0, QueryEventFactory.Row(1)), CancellationToken.None);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await store.GetRowsAsync(0, 1, cts.Token);
        });
    }
}