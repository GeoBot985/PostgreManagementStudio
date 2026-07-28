using PostgreManagementStudio.Application;

namespace PostgreManagementStudio.Core.Tests;

public sealed class QueryPerformanceHistoryTests
{
    private static QueryExecutionSample Sample(double ms, DateTimeOffset at, QueryExecutionOutcome outcome = QueryExecutionOutcome.Succeeded, ExecutionInclusionState inclusion = ExecutionInclusionState.Included, string server = "s") => new(Guid.NewGuid(), QueryHistoryService.Identity("select * from orders", server, "db", "SELECT"), at, TimeSpan.FromMilliseconds(ms), null, 1, outcome, inclusion, "select * from orders", "select * from orders", null, false, 16);

    [Fact]
    public void BaselineUsesMedianMadAndExcludesFailuresWarmups()
    { var now = DateTimeOffset.UtcNow; var samples = new[] { Sample(10, now.AddMinutes(-5)), Sample(12, now.AddMinutes(-4)), Sample(14, now.AddMinutes(-3)), Sample(16, now.AddMinutes(-2)), Sample(18, now.AddMinutes(-1)), Sample(100, now, QueryExecutionOutcome.Failed), Sample(1, now, inclusion: ExecutionInclusionState.WarmUp) }; var baseline = QueryHistoryService.Baseline(samples, new(MinimumSamples: 5), now); Assert.Equal(14, baseline.MedianMilliseconds); Assert.Equal(BaselineConfidence.Medium, baseline.Confidence); Assert.Equal(5, baseline.SampleCount); }

    [Fact]
    public void ClassifiesAbsoluteAndRelativeChanges()
    { var now = DateTimeOffset.UtcNow; var baseline = QueryHistoryService.Baseline(Enumerable.Range(0, 10).Select(i => Sample(100, now.AddMinutes(-i - 1))), now: now); var slow = QueryHistoryService.Classify(Sample(650, now), baseline); Assert.Equal(QueryPerformanceClassification.SignificantlySlower, slow.Classification); var failed = QueryHistoryService.Classify(Sample(1, now, QueryExecutionOutcome.Failed), baseline); Assert.Equal(QueryPerformanceClassification.Failed, failed.Classification); }

    [Fact]
    public void CompatibilitySeparatesEnvironments()
    { var now = DateTimeOffset.UtcNow; var result = QueryHistoryService.CheckCompatibility(Sample(1, now), Sample(1, now, server: "other")); Assert.False(result.IsComparable); Assert.Contains(result.Warnings, x => x.Contains("Server")); }

    [Fact]
    public void PrivacyRedactsBeforeStorageAndRetentionPreservesPins()
    { var now = DateTimeOffset.UtcNow; var sample = Sample(1, now) with { OriginalSql = "select * from users where email = 'secret@example.com' and id = 42" }; var stored = QueryHistoryService.ApplyPrivacy(sample, QueryTextStorageMode.NoText); Assert.DoesNotContain("secret@example.com", stored); Assert.Contains("\"OriginalSql\":null", stored, StringComparison.Ordinal); var old = sample with { StartedAt = now.AddDays(-100), Pinned = true }; Assert.Single(QueryHistoryService.Retain(new[] { old }, new(TimeSpan.FromDays(30), 1), now)); }

    [Fact]
    public void PercentilesAndMarkdownAreDeterministic()
    { Assert.Equal(2.5, QueryHistoryService.Percentile(new[] { 1d, 2, 3, 4 }, 50)); var markdown = QueryHistoryService.ExportMarkdown(new[] { Sample(1, DateTimeOffset.UnixEpoch) }); Assert.Contains("Query Performance History", markdown); Assert.Contains("Duration", markdown); }
}
