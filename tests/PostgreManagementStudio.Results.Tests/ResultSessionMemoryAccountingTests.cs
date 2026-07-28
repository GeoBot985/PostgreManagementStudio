using PostgreManagementStudio.Core;
using PostgreManagementStudio.Results;

namespace PostgreManagementStudio.Results.Tests;

public sealed class ResultSessionMemoryAccountingTests
{
    [Fact]
    public async Task MemoryIsMonotonicWhileAppending()
    {
        var session = new TestSession();
        var store = (IResultSetStore)session.CreateStore(0, QueryEventFactory.Schema(new[] { "v" }));
        var initial = store.EstimatedMemoryBytes;
        var writer = session.Writer;
        long previous = initial;
        for (int batch = 0; batch < 5; batch++)
        {
            await writer.AppendBatchAsync(
                QueryEventFactory.Batch(batch * 2,
                    QueryEventFactory.Row(new string('x', 32)),
                    QueryEventFactory.Row(new string('y', 32))),
                CancellationToken.None);
            var current = store.EstimatedMemoryBytes;
            Assert.True(current >= previous, $"Memory must be monotonic: prev={previous}, curr={current}");
            previous = current;
        }
    }

    [Fact]
    public async Task DisposedStoreReportsZero()
    {
        var session = new TestSession();
        var store = (IResultSetStore)session.CreateStore(0, QueryEventFactory.Schema(new[] { "v" }));
        var writer = session.Writer;
        await writer.AppendBatchAsync(QueryEventFactory.Batch(0, QueryEventFactory.Row(new string('z', 128))), CancellationToken.None);
        Assert.True(store.EstimatedMemoryBytes > 0);
        await store.DisposeAsync();
        Assert.Equal(0, store.EstimatedMemoryBytes);
    }

    [Fact]
    public void NullCellsHaveLowerCostThanStringCells()
    {
        var nullBytes = ResultSizeEstimatorPublic.EstimateCellBytes(new ResultCell(null, true));
        var strBytes = ResultSizeEstimatorPublic.EstimateCellBytes(new ResultCell("hello", false));
        Assert.True(nullBytes < strBytes, $"nullBytes={nullBytes}, strBytes={strBytes}");
    }
}