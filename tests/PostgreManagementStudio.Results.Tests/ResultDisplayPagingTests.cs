using PostgreManagementStudio.Core;
using PostgreManagementStudio.Results;

namespace PostgreManagementStudio.Results.Tests;

public sealed class ResultDisplayPagingTests
{
    [Fact]
    public async Task PageLoadingIsBoundedAndReportsGlobalRowIndexes()
    {
        await using var session = await BuildSessionAsync(
            Enumerable.Range(0, 1_000).Select(value => QueryEventFactory.Row(value)).ToArray());
        var store = Assert.Single(session.ResultSets);

        var page = await new ResultDisplayPageService().LoadAsync(
            store, 500, pageSize: 250, maximumTextLength: 64);

        Assert.Equal(250, page.SourceRows.Count);
        Assert.Equal(501, page.DisplayRows[0].RowIndex);
        Assert.Equal(750, page.DisplayRows[^1].RowIndex);
        Assert.True(page.HasPrevious);
        Assert.True(page.HasNext);
        Assert.Equal(1_000, page.RetainedRowCount);
    }

    [Fact]
    public async Task FirstPageFormatsOnlyTheVisiblePage()
    {
        await using var session = await BuildSessionAsync(
            Enumerable.Range(0, 10_000).Select(value => QueryEventFactory.Row(value)).ToArray());
        var formatter = new CountingFormatter();

        var page = await new ResultDisplayPageService(formatter).LoadAsync(
            session.ResultSets[0], 0);

        Assert.Equal(ResultDisplayPageService.DefaultPageSize, page.DisplayRows.Count);
        Assert.Equal(ResultDisplayPageService.DefaultPageSize, formatter.DisplayCalls);
        Assert.Equal(40, session.ResultSets[0].LoadedRowCount / formatter.DisplayCalls);
    }

    [Fact]
    public async Task LargeTextBinaryJsonAndArraysUseBoundedPreviews()
    {
        using var json = System.Text.Json.JsonDocument.Parse(
            "{\"payload\":\"" + new string('j', 20_000) + "\"}");
        var values = new object?[]
        {
            new string('x', 20_000),
            Enumerable.Repeat((byte)0xAB, 20_000).ToArray(),
            json,
            Enumerable.Range(0, 10_000).ToArray(),
        };
        var schema = new ResultSetSchema(values.Select((value, index) =>
            new ResultColumn(index, $"c{index}", "text", null, value?.GetType(), true)).ToArray());
        var executor = new FakeQueryExecutor(
        [
            new ExecutionStarted(DateTimeOffset.UtcNow),
            new ResultSetStarted(0, schema),
            new RowBatchReceived(0, new ResultRowBatch(0,
                [new ResultRow(values.Select(value => new ResultCell(value, false)).ToArray())])),
            new ResultSetCompleted(0, 1),
            new ExecutionCompleted(TimeSpan.Zero, 1),
        ]);
        await using var session = await new ResultSessionBuilder(executor).ExecuteAndBuildAsync(
            new QueryRequest("SELECT values", "Host=stub"),
            CancellationToken.None);

        var page = await new ResultDisplayPageService().LoadAsync(
            session.ResultSets[0], 0, pageSize: 10, maximumTextLength: 128);

        Assert.All(page.DisplayRows[0].Values, preview => Assert.InRange(preview.Length, 1, 128));
        Assert.True(page.IncompletePreviewCount >= 3);
        Assert.Contains("bytes", page.DisplayRows[0].Values[1]);
    }

    [Fact]
    public async Task CancelledPageLoadStopsBeforeFormatting()
    {
        await using var session = await BuildSessionAsync(
            Enumerable.Range(0, 10).Select(value => QueryEventFactory.Row(value)).ToArray());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new ResultDisplayPageService().LoadAsync(
                session.ResultSets[0], 0, cancellationToken: cancellation.Token));
    }

    [Fact]
    public async Task DisposedStoreCannotBeResurrectedByLateCancellation()
    {
        var session = await BuildSessionAsync([QueryEventFactory.Row(1)]);
        var store = Assert.Single(session.ResultSets);
        await session.DisposeAsync();

        await store.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedResultStoreException>(async () =>
            await store.GetRowAsync(0, CancellationToken.None));
        Assert.Equal(ResultSetStatus.Disposed, store.Status);
    }

    [Fact]
    public async Task RegexCacheRemainsBoundedAcrossManySearchPatterns()
    {
        var schema = QueryEventFactory.Schema(["value"]);
        var rows = new[] { QueryEventFactory.Row("value") };
        var service = new ResultViewTransformationService();
        for (var index = 0; index < 200; index++)
            service.Transform(schema, rows,
                ResultViewState.Empty with { Search = new($"value-{index}", Regex: true) });

        Assert.InRange(ResultViewTransformationService.CachedRegexCount, 1,
            ResultViewTransformationService.MaximumRegexCacheEntries);
    }

    private static async Task<IResultSession> BuildSessionAsync(ResultRow[] rows)
    {
        var executor = new FakeQueryExecutor(
        [
            new ExecutionStarted(DateTimeOffset.UtcNow),
            new ResultSetStarted(0, QueryEventFactory.Schema(["value"])),
            new RowBatchReceived(0, new ResultRowBatch(0, rows)),
            new ResultSetCompleted(0, rows.Length),
            new ExecutionCompleted(TimeSpan.Zero, 1),
        ]);
        return await new ResultSessionBuilder(executor, new ResultStorageOptions(
            32L * 1024 * 1024,
            16L * 1024 * 1024,
            10_000)).ExecuteAndBuildAsync(
            new QueryRequest("SELECT values", "Host=stub"),
            CancellationToken.None);
    }

    private sealed class CountingFormatter : IResultValueFormatter
    {
        public int DisplayCalls { get; private set; }

        public string FormatForDisplay(
            ResultCell cell,
            ResultColumn column,
            ResultDisplayFormattingOptions options)
        {
            DisplayCalls++;
            return cell.Value?.ToString() ?? options.NullText;
        }

        public string FormatForSerialization(
            ResultCell cell,
            ResultColumn column,
            ResultSerializationFormattingOptions options) =>
            cell.Value?.ToString() ?? options.NullText;
    }
}
