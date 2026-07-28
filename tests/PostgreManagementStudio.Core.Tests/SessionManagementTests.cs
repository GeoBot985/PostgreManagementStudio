using PostgreManagementStudio.Application;

namespace PostgreManagementStudio.Core.Tests;

public sealed class SessionManagementTests
{
    private static BackendSession Session(int pid, string? app = "app") => new(pid, "db", "user", app, "127.0.0.1", 1, "client backend", "active", null, null, null, TimeSpan.FromSeconds(1), null, null, null, false, 0, "select 1", false, DateTimeOffset.UnixEpoch);

    [Fact]
    public void FiltersSessionsByStateAndQuery()
    { var filter = new ActivityFilter(Database: "db", State: BackendState.Active, QueryText: "select"); Assert.True(filter.Matches(Session(1))); Assert.False((filter with { QueryText = "update" }).Matches(Session(1))); }

    [Fact]
    public void StalePidIdentityIsRejectedAndHistoryIsRedactedAndBounded()
    { var expected = new SessionIdentity(1, DateTimeOffset.UnixEpoch, "db", "user", "app"); Assert.True(SessionActionSafety.IdentityMatches(expected, Session(1))); Assert.False(SessionActionSafety.IdentityMatches(expected, Session(1, "other"))); var history = new SessionActionHistory(2); for (var i = 0; i < 4; i++) history.Add(new(DateTimeOffset.UtcNow, "cancel", "server", "db", i, "user", "app", new string('x', 300), true, "accepted", "ok")); Assert.Equal(2, history.Entries.Count); Assert.Equal(240, history.Entries[0].QueryPreview.Length); }

    [Fact]
    public void CapacityAndSamplingRemainBoundedAndExportsRedactQueries()
    { Assert.Equal("Critical", ActivityCapacityService.Classify(95, 100).Severity); var samples = new ActivitySampleHistory(TimeSpan.FromMinutes(1)); samples.Add(new(DateTimeOffset.UtcNow.AddMinutes(-2), 1, 1, 0, 0, 0, 0, 2)); samples.Add(new(DateTimeOffset.UtcNow, 2, 1, 0, 0, 0, 0, 3)); Assert.Single(samples.Samples); Assert.DoesNotContain("select 1", ActivityExportServiceV2.ToMarkdown(new[] { Session(1) })); }
}
