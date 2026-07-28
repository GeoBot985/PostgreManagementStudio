using PostgreManagementStudio.Core;
using PostgreManagementStudio.Results;

namespace PostgreManagementStudio.Results.Tests;

public sealed class ResultSetStoreRandomAccessTests
{
    private static async Task<IResultSetStore> LoadedStore(int rows)
    {
        var session = new TestSession();
        var store = session.CreateStore(0, QueryEventFactory.Schema(new[] { "v" }));
        var writer = session.Writer;
        var batchSize = 256;
        for (long i = 0; i < rows; i += batchSize)
        {
            var take = (int)Math.Min(batchSize, rows - i);
            var rowArr = new ResultRow[take];
            for (var j = 0; j < take; j++) rowArr[j] = QueryEventFactory.Row(i + j + 1);
            await writer.AppendBatchAsync(QueryEventFactory.Batch(i, rowArr), CancellationToken.None);
        }
        await writer.CompleteAsync(rows, CancellationToken.None);
        return store;
    }

    [Fact]
    public async Task GetRow_FirstRow()
    {
        var store = await LoadedStore(1024);
        var row = await store.GetRowAsync(0, CancellationToken.None);
        Assert.Equal(1L, row.Cells[0].Value);
    }

    [Fact]
    public async Task GetRow_MiddleRow()
    {
        var store = await LoadedStore(1024);
        var row = await store.GetRowAsync(500, CancellationToken.None);
        Assert.Equal(501L, row.Cells[0].Value);
    }

    [Fact]
    public async Task GetRow_LastRow()
    {
        var store = await LoadedStore(1024);
        var row = await store.GetRowAsync(1023, CancellationToken.None);
        Assert.Equal(1024L, row.Cells[0].Value);
    }

    [Fact]
    public async Task GetRows_CrossBatchRange()
    {
        var store = await LoadedStore(1024);
        var range = await store.GetRowsAsync(240, 100, CancellationToken.None);
        Assert.Equal(100, range.Count);
        Assert.Equal(241L, range[0].Cells[0].Value);
        Assert.Equal(340L, range[99].Cells[0].Value);
    }

    [Fact]
    public async Task GetRow_NegativeIndex_Throws()
    {
        var store = await LoadedStore(10);
        await Assert.ThrowsAsync<ResultRowUnavailableException>(() => store.GetRowAsync(-1, CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task GetRow_IndexEqualToLoadedCount_Throws()
    {
        var store = await LoadedStore(10);
        await Assert.ThrowsAsync<ResultRowUnavailableException>(() => store.GetRowAsync(10, CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task GetRows_RangeExtendingPastLoaded_ReturnsPrefix()
    {
        var store = await LoadedStore(100);
        var range = await store.GetRowsAsync(80, 50, CancellationToken.None);
        Assert.Equal(20, range.Count); // only 20 rows remain after 80
    }

    [Fact]
    public async Task AccessAfterDisposal_Throws()
    {
        var store = await LoadedStore(10);
        await store.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedResultStoreException>(() => store.GetRowAsync(0, CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task GetRows_StartIndexBeyondLoaded_ReturnsEmpty()
    {
        var store = await LoadedStore(10);
        var range = await store.GetRowsAsync(50, 10, CancellationToken.None);
        Assert.Empty(range);
    }
}