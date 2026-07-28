using System.Diagnostics;
using PostgreManagementStudio.Core;
using PostgreManagementStudio.Results;

namespace PostgreManagementStudio.Results.Tests;

public sealed class ResultSerializationPerformanceTests
{
    [Fact]
    public async Task HundredThousandRowsStreamWithBoundedSerializerReads()
    {
        var schema = QueryEventFactory.Schema(new[] { "id", "hash", "text" });
        var batches = Enumerable.Range(0, 100_000).Chunk(1_000).Select((chunk, index) => new RowBatchReceived(0, QueryEventFactory.Batch(index * 1_000, chunk.Select(i => QueryEventFactory.Row(i, i.ToString("X"), new string('x', 100))).ToArray()))).Cast<QueryExecutionEvent>().ToList();
        var events = new List<QueryExecutionEvent> { new ExecutionStarted(DateTimeOffset.UtcNow), new ResultSetStarted(0, schema) }; events.AddRange(batches); events.Add(new ResultSetCompleted(0, 100_000)); events.Add(new ExecutionCompleted(TimeSpan.Zero, 1));
        await using var session = await new ResultSessionBuilder(new FakeQueryExecutor(events)).ExecuteAndBuildAsync(new QueryRequest("select", "local"), CancellationToken.None);
        var writer = new MeasuringWriter(); var stopwatch = Stopwatch.StartNew(); var outcome = await new ResultSerializer(new DefaultResultValueFormatter(), ResultSerializationFormat.TabSeparatedValues).SerializeAsync(session.ResultSets[0], new ResultSelection(0, 99_999, 0, 2), new(ResultSerializationFormat.TabSeparatedValues, IncludeHeaders: false, MaximumOutputCharacters: 50_000_000), writer); stopwatch.Stop();
        Assert.True(outcome.Completed); Assert.Equal(100_000, outcome.RowsSerialized); Assert.True(writer.FirstWriteMilliseconds < stopwatch.Elapsed.TotalMilliseconds + 1); Assert.True(writer.WriteCount > 1);
        Console.WriteLine($"Sprint003 TSV rows={outcome.RowsSerialized} chars={outcome.CharactersWritten} firstWriteMs={writer.FirstWriteMilliseconds:F2} totalMs={stopwatch.Elapsed.TotalMilliseconds:F2}");
    }

    private sealed class MeasuringWriter : StringWriter
    {
        private readonly Stopwatch _clock = Stopwatch.StartNew(); public int WriteCount { get; private set; } public double FirstWriteMilliseconds { get; private set; }
        public override Task WriteAsync(string? value) { WriteCount++; if (WriteCount == 1) FirstWriteMilliseconds = _clock.Elapsed.TotalMilliseconds; return base.WriteAsync(value); }
    }
}
