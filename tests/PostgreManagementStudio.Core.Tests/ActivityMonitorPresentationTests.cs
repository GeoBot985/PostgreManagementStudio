using PostgreManagementStudio.Application;

namespace PostgreManagementStudio.Core.Tests;

public sealed class ActivityMonitorPresentationTests
{
    private static BackendSession Session(int pid, string state = "active", TimeSpan? transaction = null) => new(pid, "db", "user", "app", "127.0.0.1", 5432, "client backend", state, null, null, null, TimeSpan.FromMinutes(1), transaction, null, null, false, 0, "select 1", false, DateTimeOffset.UnixEpoch);
    private static ActivitySnapshot Snapshot(params BackendSession[] sessions) => new(1, DateTimeOffset.UtcNow, sessions, Array.Empty<BlockingRelationship>(), Array.Empty<BackendLock>(), new(sessions.Length, sessions.Count(x => x.State == "active"), sessions.Count(x => x.State == "idle"), sessions.Count(x => x.ClassifiedState == BackendState.IdleInTransaction), 0, 0, 0, 1, new Dictionary<string, int>(), null, DateTimeOffset.UtcNow));

    [Fact]
    public void CardsUseConfiguredThresholdsAndMaximumTransactionAge()
    { var snapshot = Snapshot(Session(1, transaction: TimeSpan.FromMinutes(2)), Session(2, "idle")); var cards = ActivityMonitorPresentationService.Cards(snapshot, new(LongQuery: TimeSpan.FromSeconds(30))); Assert.Equal(1, cards.LongRunningQueries); Assert.Equal(TimeSpan.FromMinutes(2), cards.MaximumTransactionAge); }

    [Fact]
    public void SelectionIdentityRejectsPidReuseAndConfirmationIsExplicit()
    { var original = Session(42); var expected = ActivityMonitorPresentationService.Identity(original); Assert.True(ActivityMonitorPresentationService.SelectionStillMatches(expected, original)); Assert.False(ActivityMonitorPresentationService.SelectionStillMatches(expected, original with { BackendStart = DateTimeOffset.UtcNow })); var confirmation = ActivityMonitorPresentationService.Confirmation("Terminate session", original); Assert.True(confirmation.RequiresStrongConfirmation); Assert.Contains("rolled back", confirmation.Warning); }

    [Fact]
    public void FilterPresetsRoundTripWithoutCredentials()
    { var preset = new SessionFilterPreset("Blocked", new(Blocked: true), 5); var copy = ActivityMonitorPresentationService.DeserializePreset(ActivityMonitorPresentationService.SerializePreset(preset)); Assert.True(copy.Filter.Blocked); Assert.DoesNotContain("password", ActivityMonitorPresentationService.SerializePreset(copy), StringComparison.OrdinalIgnoreCase); }

    [Fact]
    public async Task RefreshCoordinatorCancelsObsoleteRefresh()
    { var coordinator = new ActivityMonitorRefreshCoordinator(); using var firstStarted = new ManualResetEventSlim(); using var firstCancelled = new ManualResetEventSlim(); var first = coordinator.RefreshAsync(async (_, token) => { firstStarted.Set(); try { await Task.Delay(5000, token); } catch (OperationCanceledException) { firstCancelled.Set(); throw; } return Snapshot(Session(1)); }); firstStarted.Wait(); var second = coordinator.RefreshAsync((_, _) => Task.FromResult(Snapshot(Session(2)))); firstCancelled.Wait(); await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await first); await second; }
}
