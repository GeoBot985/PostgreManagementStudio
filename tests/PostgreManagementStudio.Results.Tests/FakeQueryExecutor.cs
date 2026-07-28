using System.Runtime.CompilerServices;
using System.Threading.Channels;
using PostgreManagementStudio.Core;

namespace PostgreManagementStudio.Results.Tests;

/// <summary>
/// Test-only executor that replays a fixed event list. Used by builder tests that
/// don't need a live PostgreSQL connection.
/// </summary>
internal sealed class FakeQueryExecutor : IQueryExecutor
{
    private readonly IReadOnlyList<QueryExecutionEvent> _events;
    public FakeQueryExecutor(IReadOnlyList<QueryExecutionEvent> events) { _events = events; }

    public async IAsyncEnumerable<QueryExecutionEvent> ExecuteAsync(
        QueryRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var ev in _events)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return ev;
        }
    }
}

/// <summary>Convenience factory for tests that need to compose query events in-memory.</summary>
internal static class QueryEventFactory
{
    public static ResultSetSchema Schema(string[] columns) => new(columns.Select((n, i) => new ResultColumn(i, n, "text", null, typeof(string), true)).ToArray());

    public static ResultRow Row(params object?[] values) => new(values.Select(v => new ResultCell(v, v is null)).ToArray());

    public static ResultRowBatch Batch(long startIndex, params ResultRow[] rows) => new(startIndex, rows);

    public static IReadOnlyList<QueryExecutionEvent> Build(params QueryExecutionEvent[] events) => events;
}