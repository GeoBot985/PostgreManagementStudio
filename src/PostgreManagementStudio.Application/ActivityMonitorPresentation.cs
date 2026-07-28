using System.Text.Json;

namespace PostgreManagementStudio.Application;

public sealed record ActivityRefreshSettings(TimeSpan Interval = default, bool Automatic = false) { public TimeSpan EffectiveInterval => Interval == default ? TimeSpan.FromSeconds(5) : Interval; }
public sealed record ActivityMonitorState(ActivitySnapshot? Snapshot, DateTimeOffset? LastSuccessfulRefresh, bool IsPaused, bool IsRefreshing, bool IsStale, string? LastError, ActivityRefreshSettings Refresh);
public sealed record ActivityCardSummary(int TotalSessions, int ActiveSessions, int IdleSessions, int IdleInTransactionSessions, int WaitingSessions, int BlockedSessions, int LongRunningQueries, TimeSpan? MaximumTransactionAge);
public sealed record ActivitySelectionIdentity(int ProcessId, DateTimeOffset? BackendStart, string? Database, string? User, string? ApplicationName, string? ClientAddress);
public sealed record ActivityActionConfirmation(string Action, int ProcessId, string? User, string? Database, string? Application, string? QueryPreview, string Warning, bool RequiresStrongConfirmation);

public static class ActivityMonitorPresentationService
{
    public static ActivityCardSummary Cards(ActivitySnapshot snapshot, SessionMonitorThresholds? thresholds = null)
    { thresholds ??= new(); return new(snapshot.Summary.TotalSessions, snapshot.Summary.ActiveSessions, snapshot.Summary.IdleSessions, snapshot.Summary.IdleInTransactionSessions, snapshot.Summary.WaitingSessions, snapshot.Summary.BlockedSessions, snapshot.Sessions.Count(x => x.State == "active" && x.Duration >= thresholds.EffectiveLongQuery), snapshot.Sessions.Select(x => x.TransactionDuration).Where(x => x.HasValue).Select(x => x!.Value).DefaultIfEmpty().Max()); }
    public static ActivitySelectionIdentity Identity(BackendSession session) => new(session.ProcessId, session.BackendStart, session.Database, session.User, session.ApplicationName, session.ClientAddress);
    public static bool SelectionStillMatches(ActivitySelectionIdentity expected, BackendSession current) => SessionActionSafety.IdentityMatches(new(expected.ProcessId, expected.BackendStart, expected.Database, expected.User, expected.ApplicationName), current) && expected.ClientAddress == current.ClientAddress;
    public static ActivityActionConfirmation Confirmation(string action, BackendSession target, QueryTextDisplayMode privacy = QueryTextDisplayMode.Hide) => new(action, target.ProcessId, target.User, target.Database, target.ApplicationName, SessionMonitorService.QueryPreview(target.Query, privacy), action.Equals("Terminate session", StringComparison.OrdinalIgnoreCase) ? "The session will be disconnected and its current transaction will be rolled back by PostgreSQL. External side effects may not be reversed." : "PostgreSQL will be asked to cancel the active query; the session remains connected.", action.Equals("Terminate session", StringComparison.OrdinalIgnoreCase));
    public static string SerializePreset(SessionFilterPreset preset) => JsonSerializer.Serialize(preset);
    public static SessionFilterPreset DeserializePreset(string json) => JsonSerializer.Deserialize<SessionFilterPreset>(json) ?? throw new InvalidDataException("Invalid activity filter preset.");
}

public sealed class ActivityMonitorRefreshCoordinator
{
    private readonly ActivityRefreshCoordinator _coordinator = new(); private CancellationTokenSource? _active;
    public async Task<ActivitySnapshot?> RefreshAsync(Func<long, CancellationToken, Task<ActivitySnapshot>> loader, CancellationToken cancellationToken = default) { _active?.Cancel(); _active?.Dispose(); _active = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); return await _coordinator.RefreshAsync(loader, _active.Token); }
    public void Cancel() => _active?.Cancel();
}
