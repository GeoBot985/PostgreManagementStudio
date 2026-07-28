using PostgreManagementStudio.Application;

namespace PostgreManagementStudio.Core.Tests;

public sealed class ExecutionPlanTests
{
    [Fact]
    public void BuildsEstimatedPlanWithoutAnalyzeAndDetectsMutation()
    {
        var estimated = ExplainCommandBuilder.Build(new("SELECT * FROM public.orders;", new())); Assert.Contains("FORMAT JSON", estimated.Sql); Assert.DoesNotContain("ANALYZE TRUE", estimated.Sql); Assert.False(estimated.IsDataChanging);
        var changing = ExplainCommandBuilder.Build(new("UPDATE public.orders SET value = 1", new(PlanType.Actual))); Assert.True(changing.IsDataChanging); Assert.True(changing.RequiresConfirmation); Assert.Contains(changing.Warnings, x => x.Contains("change data"));
    }

    [Fact]
    public void RejectsAmbiguousMultiStatementPlans()
    { Assert.Throws<ArgumentException>(() => ExplainCommandBuilder.Build(new("SELECT 1; SELECT 2", new()))); Assert.Contains("'a;b'", ExplainCommandBuilder.Build(new("SELECT 'a;b'", new())).Sql); }

    [Fact]
    public void ParsesPlanTreeAndSummarizesMetrics()
    {
        const string json = "[{\"Plan\":{\"Node Type\":\"Seq Scan\",\"Relation Name\":\"orders\",\"Plan Rows\":20000,\"Total Cost\":42,\"Actual Rows\":19000,\"Actual Total Time\":3.5,\"Actual Loops\":1,\"Plans\":[{\"Node Type\":\"Index Scan\",\"Index Name\":\"ix_orders\",\"Plan Rows\":10}]},\"Planning Time\":1.2,\"Execution Time\":4.5}]"; var plan = ExecutionPlanParser.Parse("SELECT", json, PlanType.Actual); var summary = PlanMetricsService.Summarize(plan); Assert.Equal(2, summary.NodeCount); Assert.Equal(1, summary.SequentialScans); Assert.Equal(1, summary.IndexScans); Assert.Equal(42, summary.TotalCost); Assert.Single(PlanMetricsService.Diagnose(plan));
    }

    [Fact]
    public void HistoryIsBounded()
    { var history = new PlanHistoryService(2); for (var i = 0; i < 4; i++) history.Add(new(i.ToString(), "SELECT 1", PlanType.Estimated, "server", "db", DateTimeOffset.UtcNow, "{}", "", Array.Empty<string>())); Assert.Equal(2, history.Entries.Count); }
}
