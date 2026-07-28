using PostgreManagementStudio.Application;

namespace PostgreManagementStudio.Core.Tests;

public sealed class ExecutionPlanExplorerTests
{
    private static ExecutionPlanDocument Plan(string node = "Seq Scan", double rows = 20000, double actual = 3000000) => ExecutionPlanParser.Parse("select 1", "[{\"Plan\":{\"Node Type\":\"" + node + "\",\"Total Cost\":100,\"Plan Rows\":" + rows + ",\"Actual Rows\":" + actual + ",\"Actual Total Time\":10,\"Actual Loops\":1,\"Relation Name\":\"orders\",\"Plans\":[{\"Node Type\":\"Index Scan\",\"Total Cost\":10,\"Plan Rows\":1,\"Plans\":[]}]},\"Execution Time\":20}]", PlanType.Actual);

    [Fact]
    public void FlattensTreeWithParentDepthAndStableIds()
    { var rows = ExecutionPlanExplorerService.Flatten(Plan()); Assert.Equal(2, rows.Count); Assert.Equal(0, rows[0].Depth); Assert.Equal(1, rows[1].Depth); Assert.Equal(rows[0].NodeId, rows[1].ParentId); Assert.Equal(100d, rows[0].CostPercent); }

    [Fact]
    public void SearchesNodesAndProducesEvidenceWarnings()
    { var plan = Plan(); Assert.Single(ExecutionPlanExplorerService.Search(plan, "orders").Matches); var warnings = ExecutionPlanExplorerService.Warnings(plan); Assert.Contains(warnings, x => x.Summary.Contains("sequential")); Assert.Contains(warnings, x => x.Summary.Contains("estimation")); }

    [Fact]
    public void ImportsMalformedPlansSafelyAndMapsCapabilities()
    { var invalid = ExecutionPlanExplorerService.Import("{not json"); Assert.False(invalid.IsValid); Assert.NotNull(invalid.Error); Assert.True(ExecutionPlanExplorerService.ForPostgreSqlMajor(13).SupportsWal); Assert.False(ExecutionPlanExplorerService.ForPostgreSqlMajor(12).SupportsMemory); }

    [Fact]
    public void MissingRuntimeValuesRemainUnavailable()
    { var plan = ExecutionPlanParser.Parse("select 1", "[{\"Plan\":{\"Node Type\":\"Seq Scan\",\"Plan Rows\":1,\"Plans\":[]}}]", PlanType.Estimated); var row = Assert.Single(ExecutionPlanExplorerService.Flatten(plan)); Assert.Null(row.InclusiveTime); Assert.Null(row.ExclusiveTime); }
}
