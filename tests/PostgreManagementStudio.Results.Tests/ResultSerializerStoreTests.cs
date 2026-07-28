using PostgreManagementStudio.Core;
using PostgreManagementStudio.Results;

namespace PostgreManagementStudio.Results.Tests;

public sealed class ResultSerializerStoreTests
{
    [Fact]
    public async Task SerializesSelectionAcrossBatchesAndPreservesNullEmptyAndQuotes()
    {
        var schema = QueryEventFactory.Schema(new[] { "id", "text" });
        var events = QueryEventFactory.Build(new ExecutionStarted(DateTimeOffset.UtcNow), new ResultSetStarted(0, schema),
            new RowBatchReceived(0, QueryEventFactory.Batch(0, QueryEventFactory.Row(1, ""))),
            new RowBatchReceived(0, QueryEventFactory.Batch(1, QueryEventFactory.Row(2, null))),
            new RowBatchReceived(0, QueryEventFactory.Batch(2, QueryEventFactory.Row(3, "a,b"))),
            new ResultSetCompleted(0, 3), new ExecutionCompleted(TimeSpan.FromMilliseconds(1), 1));
        await using var session = await new ResultSessionBuilder(new FakeQueryExecutor(events)).ExecuteAndBuildAsync(new QueryRequest("select", "local"), CancellationToken.None);
        using var writer = new StringWriter(); var serializer = new ResultSerializer(new DefaultResultValueFormatter(), ResultSerializationFormat.Csv);
        var outcome = await serializer.SerializeAsync(session.ResultSets[0], new ResultSelection(0, 2, 0, 1), new(ResultSerializationFormat.Csv), writer);
        Assert.True(outcome.Completed); Assert.Equal("id,text\r\n1,\r\n2,NULL\r\n3,\"a,b\"\r\n", writer.ToString());
    }

    [Fact]
    public async Task OutputLimitAndCancellationAreReported()
    {
        var schema = QueryEventFactory.Schema(new[] { "value" }); var rows = Enumerable.Range(0, 100).Select(i => QueryEventFactory.Row(i.ToString())).ToArray();
        var events = QueryEventFactory.Build(new ExecutionStarted(DateTimeOffset.UtcNow), new ResultSetStarted(0, schema), new RowBatchReceived(0, QueryEventFactory.Batch(0, rows)), new ResultSetCompleted(0, 100), new ExecutionCompleted(TimeSpan.Zero, 1));
        await using var session = await new ResultSessionBuilder(new FakeQueryExecutor(events)).ExecuteAndBuildAsync(new QueryRequest("select", "local"), CancellationToken.None);
        var serializer = new ResultSerializer(new DefaultResultValueFormatter(), ResultSerializationFormat.PlainText); using var writer = new StringWriter();
        var limited = await serializer.SerializeAsync(session.ResultSets[0], new ResultSelection(0, 99, 0, 0), new(ResultSerializationFormat.PlainText, MaximumOutputCharacters: 20), writer);
        Assert.Equal(ResultSerializationStopReason.MaximumOutputExceeded, limited.StopReason);
        using var cts = new CancellationTokenSource(); cts.Cancel(); using var cancelledWriter = new StringWriter();
        var cancelled = await serializer.SerializeAsync(session.ResultSets[0], new ResultSelection(0, 99, 0, 0), new(ResultSerializationFormat.PlainText), cancelledWriter, cts.Token);
        Assert.Equal(ResultSerializationStopReason.Cancelled, cancelled.StopReason);
    }
}
