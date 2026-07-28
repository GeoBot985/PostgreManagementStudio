using PostgreManagementStudio.Application;

namespace PostgreManagementStudio.Core.Tests;

public sealed class QueryPerformanceDashboardTests
{
    private static QueryStatistics Query(long calls, int totalMs, long hit = 10, long read = 5) { var id = new QueryIdentity("s", "db", "u", 1); return new(id, "select 1", calls, TimeSpan.FromMilliseconds(totalMs), TimeSpan.FromMilliseconds(calls == 0 ? 0 : totalMs / (double)calls), TimeSpan.Zero, TimeSpan.FromMilliseconds(totalMs), 100, calls, hit, read, 2, 1000); }

    [Fact]
    public void SummarizesAggregatedStatisticsAndCacheHitRatio()
    { var summary = QueryDashboardService.Summarize(new[] { Query(10, 100), Query(5, 50, 0, 5) }); Assert.Equal(2, summary.TrackedStatements); Assert.Equal(15, summary.TotalCalls); Assert.Equal(50d, summary.CacheHitPercent); Assert.Equal(TimeSpan.FromMilliseconds(150), summary.TotalExecutionTime); }

    [Fact]
    public void QuickFiltersUseCentralizedThresholds()
    { var values = new[] { Query(2, 100), Query(20000, 700000) }; Assert.Single(QueryDashboardService.Filter(values, QueryDashboardQuickFilter.FrequentlyExecuted)); Assert.Single(QueryDashboardService.Filter(values, QueryDashboardQuickFilter.HighTotalTime)); }

    [Fact]
    public void DeltaRejectsResetsAndNegativeCounters()
    { var old = Query(10, 100); var current = Query(20, 300); var t = DateTimeOffset.UtcNow; var baseline = new QueryStatisticsSnapshot(t, null, "s", "db", "u", new[] { old }.ToDictionary(x => x.Identity), new(false, false, false, false, false)); var next = new QueryStatisticsSnapshot(t.AddMinutes(1), null, "s", "db", "u", new[] { current }.ToDictionary(x => x.Identity), baseline.Capabilities); Assert.True(QueryDashboardService.Delta(baseline, next, old.Identity).IsComparable); var reset = QueryDashboardService.Delta(baseline, next with { StatisticsResetAt = t.AddSeconds(1) }, old.Identity); Assert.False(reset.IsComparable); }

    [Fact]
    public void AvailabilityAndResetRequireExplicitPermissionAndConfirmation()
    { var state = QueryDashboardService.ExplainAvailability(new(PgStatStatementsState.NotPreloaded, "not loaded")); Assert.True(state.RequiresRestart); Assert.False(QueryDashboardService.ValidateReset(state, true).Allowed); var available = QueryDashboardService.ExplainAvailability(new(PgStatStatementsState.Available, "ok"), true); Assert.False(QueryDashboardService.ValidateReset(available, false).Allowed); Assert.True(QueryDashboardService.ValidateReset(available, true).Allowed); }

    [Fact]
    public void FormattingUsesReadableUnits()
    { Assert.Contains("µs", QueryDashboardService.Duration(TimeSpan.FromTicks(1))); Assert.Contains("MiB", QueryDashboardService.Bytes(2 * 1024 * 1024)); }
}
