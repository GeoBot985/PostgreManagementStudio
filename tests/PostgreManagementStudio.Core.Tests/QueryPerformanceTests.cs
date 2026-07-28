using PostgreManagementStudio.Application;

namespace PostgreManagementStudio.Core.Tests;

public sealed class QueryPerformanceTests
{
    private static QueryStatistics Query(string text, long calls, int totalMs, long id = 1) { var identity = QueryPerformanceService.Identity("server", "db", "user", id, text); return new(identity, text, calls, TimeSpan.FromMilliseconds(totalMs), TimeSpan.FromMilliseconds(calls == 0 ? 0 : totalMs / (double)calls), TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(totalMs), 2, calls, 10, 5, 2); }

    [Fact]
    public void UsesCompoundAndFallbackIdentities()
    { Assert.NotEqual(QueryPerformanceService.Identity("s1", "db", "u", 42, "select 1").StableKey, QueryPerformanceService.Identity("s2", "db", "u", 42, "select 1").StableKey); Assert.StartsWith("fallback:", QueryPerformanceService.Identity("s", "db", "u", 0, "select 1").StableKey.Split('|').Last()); }

    [Fact]
    public void ClassifiesCommentsCtesAndUtilityStatements()
    { Assert.Equal(QueryStatementType.Select, QueryPerformanceService.Classify("-- note\nWITH x AS (SELECT 1) SELECT * FROM x")); Assert.Equal(QueryStatementType.Utility, QueryPerformanceService.Classify("/* comment */ SET work_mem='1MB'")); }

    [Fact]
    public void RanksWithStableTieBreakAndFilters()
    { var queries = new[] { Query("select b", 2, 20, 2), Query("select a", 2, 20, 1) }; var ranked = QueryPerformanceService.Rank(queries, QueryRanking.TotalTime, new(MinimumCalls: 2)); Assert.Equal(1, ranked[0].Identity.QueryId); }

    [Fact]
    public void CalculatesCounterDeltasAndRejectsReset()
    { var old = Query("select 1", 10, 100); var now = Query("select 1", 30, 500); var t0 = DateTimeOffset.UtcNow; var first = new QueryStatisticsSnapshot(t0, null, "s", "db", "u", new[] { old }.ToDictionary(x => x.Identity), new(true, false, false, false, false)); var second = new QueryStatisticsSnapshot(t0.AddSeconds(10), null, "s", "db", "u", new[] { now }.ToDictionary(x => x.Identity), first.Capabilities); var interval = QueryPerformanceService.Compare(first, second, old.Identity); Assert.Equal(20, interval.Calls); Assert.Equal(2, interval.CallsPerSecond); var reset = QueryPerformanceService.Compare(first, second with { StatisticsResetAt = t0.AddMinutes(1) }, old.Identity); Assert.False(reset.IsComparable); }

    [Fact]
    public void RegressionAndExplainAreConservative()
    { var previous = new QueryIntervalStatistics(new("s", "d", "u", 1), TimeSpan.FromMinutes(5), 100, TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(50), 0.3, 1, true, null); var current = previous with { ExecutionTime = TimeSpan.FromSeconds(12), MeanExecutionTime = TimeSpan.FromMilliseconds(120) }; var result = QueryPerformanceService.DetectRegression(previous, current); Assert.Equal(RegressionStatus.SignificantRegression, result.Status); var explain = QueryExplainTemplateService.Generate("UPDATE public.items SET value = 1", true); Assert.Contains("WARNING", explain); Assert.Contains("ANALYZE", explain); }

    [Fact]
    public void SnapshotHistoryIsBounded()
    { var history = new QuerySnapshotHistory(2); for (var i = 0; i < 4; i++) history.Add(new(DateTimeOffset.UtcNow, null, "s", "d", "u", new Dictionary<QueryIdentity, QueryStatistics>(), new(false, false, false, false, false))); Assert.Equal(2, history.Snapshots.Count); }
}
