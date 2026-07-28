using PostgreManagementStudio.Application;

namespace PostgreManagementStudio.Core.Tests;

public sealed class PlanComparisonRegressionTests
{
    private static ExecutionPlanDocument Plan(string node, double cost, double? time, string sql = "select 1") => ExecutionPlanParser.Parse(sql, "[{\"Plan\":{\"Node Type\":\"" + node + "\",\"Total Cost\":" + cost + ",\"Plan Rows\":10,\"Actual Total Time\":" + (time?.ToString() ?? "null") + ",\"Actual Rows\":10,\"Actual Loops\":1,\"Plans\":[]},\"Execution Time\":" + (time?.ToString() ?? "null") + ",\"Planning Time\":1}]", time is null ? PlanType.Estimated : PlanType.Actual);

    [Fact]
    public void FingerprintNormalizesWhitespaceAndTrailingSemicolon()
    { Assert.Equal(PlanComparisonService.QueryFingerprint(" select  *  from t; "), PlanComparisonService.QueryFingerprint("select * from t")); }

    [Fact]
    public void ComparesNodesAndProducesCompatibilityWarnings()
    { var comparison = PlanComparisonService.Compare(Plan("Seq Scan", 100, null), Plan("Index Scan", 100, null, "select 2")); Assert.Contains(comparison.CompatibilityWarnings, x => x.Contains("differs")); Assert.Contains(comparison.Matches, x => x.Change == PlanNodeChangeType.Modified && x.Confidence == PlanMatchConfidence.Low); }

    [Fact]
    public void DetectsRegressionAndHandlesZeroBaselinePercentages()
    { var comparison = PlanComparisonService.Compare(Plan("Index Scan", 100, 10), Plan("Seq Scan", 200, 30)); Assert.Equal(PlanRegressionClassification.Regressed, comparison.Assessment.Classification); var difference = PlanComparisonService.Difference(0, 10); Assert.Null(difference.Percent); Assert.Contains("zero", difference.Status, StringComparison.OrdinalIgnoreCase); }

    [Fact]
    public void MarkdownAndSessionRoundTripAreOffline()
    { var comparison = PlanComparisonService.Compare(Plan("Seq Scan", 100, null), Plan("Seq Scan", 100, null)); var markdown = PlanComparisonService.Markdown(comparison); Assert.Contains("Execution Plan Comparison", markdown); var copy = PlanComparisonService.Open(PlanComparisonService.Save(comparison)); Assert.Single(copy.Matches); }

    [Fact]
    public void ComparisonHonoursCancellation()
    { using var cts = new CancellationTokenSource(); cts.Cancel(); Assert.Throws<OperationCanceledException>(() => PlanComparisonService.Compare(Plan("Seq Scan", 1, null), Plan("Seq Scan", 1, null), cancellationToken: cts.Token)); }
}
