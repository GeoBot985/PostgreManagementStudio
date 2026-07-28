using PostgreManagementStudio.Application;

namespace PostgreManagementStudio.Core.Tests;

public sealed class ActivityMonitoringTests
{
    private static BackendSession Session(int pid, string state = "active", bool blocked = false, string? type = "client backend") => new(pid, "db", "user", "app", "127.0.0.1", 5432, type, state, null, null, null, TimeSpan.FromSeconds(40), null, null, null, blocked, 0, "select 1", false, null);

    [Fact]
    public void ClassifiesWaitingBlockedAndIdleTransactionStates()
    {
        Assert.Equal(BackendState.Waiting, (Session(1, "active") with { WaitEvent = "Lock" }).ClassifiedState); Assert.Equal(BackendState.Blocked, Session(2, "active", true).ClassifiedState); Assert.Equal(BackendState.IdleInTransaction, Session(3, "idle in transaction").ClassifiedState);
    }

    [Fact]
    public void BlockingTreesHandleChainsAndCycles()
    {
        var service = new BlockingAnalysisService(); var tree = service.BuildTree(new[] { new BlockingRelationship(3, 2, 1, null, null, null, null, null), new BlockingRelationship(2, 1, 1, null, null, null, null, null) });
        Assert.Single(tree); Assert.Equal(1, tree[0].ProcessId); Assert.Equal(2, tree[0].Children[0].ProcessId); Assert.Equal(3, tree[0].Children[0].Children[0].ProcessId);
    }

    [Fact]
    public void HistoryIsBoundedAndSafetyBlocksProtectedSessions()
    {
        var history = new ActivityHistoryService(2); for (var i = 0; i < 4; i++) history.Add(new(i, DateTimeOffset.UtcNow, Array.Empty<BackendSession>(), Array.Empty<BlockingRelationship>(), Array.Empty<BackendLock>(), new(0, 0, 0, 0, 0, 0, 0, 0, new Dictionary<string, int>(), null, DateTimeOffset.UtcNow))); Assert.Equal(2, history.Snapshots.Count);
        var result = BackendSafety.ValidateTermination(Session(9), 9, null, null); Assert.False(result.Accepted); Assert.Equal("self", result.Code); Assert.False(BackendSafety.ValidateTermination(Session(10, type: "autovacuum worker"), 1, null, null).Accepted);
    }

    [Fact]
    public void ExportRedactsSensitiveFields()
    {
        var s = Session(1); var snapshot = new ActivitySnapshot(1, DateTimeOffset.UtcNow, new[] { s }, Array.Empty<BlockingRelationship>(), Array.Empty<BackendLock>(), new(1, 1, 0, 0, 0, 0, 0, 0, new Dictionary<string, int>(), null, DateTimeOffset.UtcNow)); var json = ActivityExportService.ToJson(snapshot, false, false, false); Assert.DoesNotContain("select 1", json); Assert.Contains("[redacted]", json);
    }
}
