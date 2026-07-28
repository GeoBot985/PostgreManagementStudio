using PostgreManagementStudio.Application;

namespace PostgreManagementStudio.Core.Tests;

public sealed class ExecutionPlanAnalysisTests
{
    private static ExecutionPlanDocument Plan(string nodeType = "Seq Scan", double? rows = 10, double? actual = 200, double? time = 12) => ExecutionPlanParser.Parse("select 1", "[{\"Plan\":{\"Node Type\":\"" + nodeType + "\",\"Relation Name\":\"orders\",\"Schema\":\"public\",\"Plan Rows\":" + rows + ",\"Actual Rows\":" + actual + ",\"Actual Total Time\":" + time + ",\"Actual Loops\":1,\"Plans\":[],\"Unknown Field\":123},\"Planning Time\":2,\"Execution Time\":20}]", PlanType.Actual);

    [Fact]
    public void ParsesUnknownFieldsAndCalculatesLoopAwareMetrics()
    { var plan = Plan(); var analysis = ExecutionPlanAnalysisService.Analyse(plan); Assert.Single(analysis); Assert.Equal(RowEstimateClassification.LargeMismatch, analysis[0].RowClassification); Assert.Equal(TimeSpan.FromMilliseconds(12), analysis[0].InclusiveRuntime); Assert.Contains("Unknown Field", plan.Root.UnknownFields.Keys); Assert.Equal(60, analysis[0].RuntimeContributionPercent); }

    [Fact]
    public void DiagnosticsAreEvidenceBasedAndFileRoundTrips()
    { var plan = Plan(); var diagnostics = PlanDiagnosticEngine.Diagnose(plan); Assert.Contains(diagnostics, x => x.Evidence.Contains("Estimated rows")); var saved = ExecutionPlanFileService.Save(plan, false); Assert.DoesNotContain("select 1", saved); var loaded = ExecutionPlanFileService.Open(saved); Assert.Equal("Seq Scan", loaded.Root.NodeType); }

    [Fact]
    public void ComparisonDetectsScanReplacementAndQueryMismatch()
    { var result = ExecutionPlanComparisonService.Compare(Plan("Seq Scan"), Plan("Index Scan") with { QueryText = "select 2" }); Assert.True(result.PossiblyDifferentQueries); Assert.Contains(result.Changes, x => x.Kind == PlanChangeKind.Replaced); }

    [Fact]
    public void ExplainAnalyseWarnsForDataChanges()
    { var command = ExplainCommandBuilder.Build(new ExplainRequest("UPDATE public.orders SET id = id", new(PlanType.Actual, Safety: ActualPlanSafety.Normal))); Assert.True(command.RequiresConfirmation); Assert.Contains(command.Warnings, x => x.Contains("side effects")); }
}
