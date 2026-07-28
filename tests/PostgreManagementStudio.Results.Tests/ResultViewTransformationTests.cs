using PostgreManagementStudio.Core;
using PostgreManagementStudio.Results;

namespace PostgreManagementStudio.Results.Tests;

public sealed class ResultViewTransformationTests
{
    private static readonly ResultSetSchema Schema = new(new[] { new ResultColumn(0, "n", "integer", null, typeof(int), true), new ResultColumn(1, "text", "text", null, typeof(string), true) });
    private static ResultRow Row(object? n, object? text) => new(new[] { new ResultCell(n, n is null), new ResultCell(text, text is null) });

    [Fact]
    public void SortsTypedValuesAndRestoresOriginalOrder()
    {
        var rows = new[] { Row(10, "z"), Row(2, "a"), Row(null, "n") }; var service = new ResultViewTransformationService();
        var sorted = service.Transform(Schema, rows, new(new[] { new SortDescriptor(0, SortDirection.Ascending, NullPlacement.Last) }, null, new()));
        Assert.Equal(new[] { 1, 0, 2 }, sorted.VisibleRowIndexes);
        Assert.Equal(new[] { 0, 1, 2 }, service.Transform(Schema, rows, ResultViewState.Empty).VisibleRowIndexes);
        Assert.Equal(10, rows[0].Cells[0].Value);
    }

    [Fact]
    public void FiltersCompoundAndSearchesWithoutMutatingSource()
    {
        var rows = new[] { Row(1, "Alpha"), Row(2, "beta"), Row(3, "alphabet") }; var service = new ResultViewTransformationService();
        var filter = new FilterGroup(LogicalOperator.And, new FilterExpression[] { new FilterCondition(0, FilterOperator.GreaterThan, 1), new FilterCondition(1, FilterOperator.Contains, "alpha") });
        var result = service.Transform(Schema, rows, new(Array.Empty<SortDescriptor>(), filter, new("ALPHA")));
        Assert.Equal(new[] { 2 }, result.VisibleRowIndexes); Assert.Null(result.Error); Assert.Equal("beta", rows[1].Cells[1].Value);
    }

    [Fact]
    public void InvalidRegexIsReportedAndDoesNotPretendToBeAValidView()
    {
        var result = new ResultViewTransformationService().Transform(Schema, new[] { Row(1, "x") }, new(Array.Empty<SortDescriptor>(), null, new("[", false, true)));
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task CancellationIsHonoured()
    {
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ResultViewTransformationService().TransformAsync(Schema, new[] { Row(1, "x") }, ResultViewState.Empty, cts.Token));
    }
}
