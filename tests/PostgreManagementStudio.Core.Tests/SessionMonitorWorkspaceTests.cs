using PostgreManagementStudio.Application;

namespace PostgreManagementStudio.Core.Tests;

public sealed class SessionMonitorWorkspaceTests
{
    private static BackendSession Session(int pid, string state = "active", TimeSpan? duration = null, bool blocked = false, int blocking = 0) => new(pid, "db", "user", "app", "127.0.0.1", 5432, "client backend", state, null, null, null, duration ?? TimeSpan.FromSeconds(1), null, null, null, blocked, blocking, "select 'secret'", false, null);
    private static ActivitySnapshot Snapshot(params BackendSession[] sessions) => new(1, DateTimeOffset.UtcNow, sessions, Array.Empty<BlockingRelationship>(), Array.Empty<BackendLock>(), new(sessions.Length, sessions.Count(x => x.State == "active"), sessions.Count(x => x.State == "idle"), 0, 0, sessions.Count(x => x.Blocked), sessions.Count(x => x.BlockingCount > 0), 0, new Dictionary<string, int>(), null, DateTimeOffset.UtcNow));

    [Fact]
    public void FiltersAndDiagnosesLongBlockedActivity()
    { var snapshot = Snapshot(Session(1, duration: TimeSpan.FromMinutes(3), blocked: true), Session(2, "idle in transaction") with { TransactionDuration = TimeSpan.FromMinutes(6) }); var filtered = new SessionMonitorFilter(Blocked: true); Assert.Single(snapshot.Sessions, x => filtered.Matches(x)); var diagnostics = SessionMonitorService.Diagnose(snapshot); Assert.Contains(diagnostics, x => x.Rule == "long-running-query"); Assert.Contains(diagnostics, x => x.Rule == "idle-in-transaction"); }

    [Fact]
    public void QueryPreviewRespectsPrivacyAndTruncation()
    { Assert.DoesNotContain("secret", SessionMonitorService.QueryPreview("select 'secret'", QueryTextDisplayMode.MaskLiterals)); Assert.Contains("truncated", SessionMonitorService.QueryPreview(new string('x', 300), QueryTextDisplayMode.Show)); }

    [Fact]
    public void SnapshotComparisonUsesSessionStateAndBlockerChanges()
    { var before = Snapshot(Session(1)); var after = Snapshot(Session(1, "idle"), Session(2, blocked: true)); var comparison = SessionMonitorService.Compare(before, after); Assert.Contains(2, comparison.NewSessions); Assert.Contains(1, comparison.StateChanged); }

    [Fact]
    public void ExportsSnapshotWithoutQueryByDefault()
    { var csv = SessionMonitorService.ExportCsv(new[] { Session(1) }); Assert.Contains("PID,Database", csv); Assert.DoesNotContain("secret", csv); var json = SessionMonitorService.SaveSnapshot(Snapshot(Session(1)), QueryTextDisplayMode.Hide); Assert.Contains("SchemaVersion", json); Assert.DoesNotContain("secret", json); }
}
