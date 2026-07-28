using System.Text;
using System.Text.Json;

namespace PostgreManagementStudio.Application;

public enum BackendState { Active, Idle, IdleInTransaction, IdleInTransactionAborted, Waiting, Blocked, BackgroundWorker, Disabled, Unknown }
public sealed record BackendSession(int ProcessId, string? Database, string? User, string? ApplicationName, string? ClientAddress, int? ClientPort, string? BackendType, string? State, DateTimeOffset? QueryStart, DateTimeOffset? TransactionStart, DateTimeOffset? StateChange, TimeSpan Duration, TimeSpan? TransactionDuration, string? WaitEventType, string? WaitEvent, bool Blocked, int BlockingCount, string? Query, bool QueryUnavailable, DateTimeOffset? BackendStart, bool IsMonitoringSession = false, bool IsCurrentEditorSession = false)
{ public BackendState ClassifiedState => Blocked ? BackendState.Blocked : State switch { "active" => string.IsNullOrWhiteSpace(WaitEvent) ? BackendState.Active : BackendState.Waiting, "idle" => BackendState.Idle, "idle in transaction" => BackendState.IdleInTransaction, "idle in transaction (aborted)" => BackendState.IdleInTransactionAborted, _ when BackendType is not null && BackendType != "client backend" => BackendState.BackgroundWorker, _ => BackendState.Unknown }; }
public sealed record BlockingRelationship(int BlockedProcessId, int BlockingProcessId, int Depth, string? Database, string? User, string? BlockedQuery, string? BlockingQuery, string? WaitEvent);
public sealed record BackendLock(int ProcessId, string? LockType, string? Database, string? Relation, int? Page, int? Tuple, string? VirtualTransaction, long? TransactionId, string? Mode, bool Granted, bool FastPath, DateTimeOffset? WaitStart);
public sealed record ActivitySummary(int TotalSessions, int ActiveSessions, int IdleSessions, int IdleInTransactionSessions, int WaitingSessions, int BlockedSessions, int BlockingSessions, int LongRunningQueries, IReadOnlyDictionary<string, int> SessionsByDatabase, int? MaxConnections, DateTimeOffset CollectedAt);
public sealed record ActivitySnapshot(long Sequence, DateTimeOffset ServerTime, IReadOnlyList<BackendSession> Sessions, IReadOnlyList<BlockingRelationship> Blocking, IReadOnlyList<BackendLock> Locks, ActivitySummary Summary);
public sealed record ActivityMonitorSettings(TimeSpan LongRunningThreshold = default, TimeSpan IdleTransactionThreshold = default, int MaxHistorySnapshots = 60, bool HideMonitoringSession = true) { public TimeSpan EffectiveLongRunning => LongRunningThreshold == default ? TimeSpan.FromSeconds(30) : LongRunningThreshold; public TimeSpan EffectiveIdleTransaction => IdleTransactionThreshold == default ? TimeSpan.FromMinutes(5) : IdleTransactionThreshold; }
public sealed class BlockingAnalysisService
{
    public IReadOnlyList<BlockingTreeNode> BuildTree(IEnumerable<BlockingRelationship> relations) { var byBlocker = relations.GroupBy(x => x.BlockingProcessId).ToDictionary(x => x.Key, x => x.ToArray()); var roots = relations.Select(x => x.BlockingProcessId).Except(relations.Select(x => x.BlockedProcessId)).Distinct(); return roots.Select(x => Build(x, byBlocker, new HashSet<int>())).ToArray(); }
    private static BlockingTreeNode Build(int pid, IReadOnlyDictionary<int, BlockingRelationship[]> map, HashSet<int> path) { if (!path.Add(pid)) return new(pid, true, Array.Empty<BlockingTreeNode>()); var children = map.TryGetValue(pid, out var links) ? links.Select(x => Build(x.BlockedProcessId, map, new(path))).ToArray() : Array.Empty<BlockingTreeNode>(); return new(pid, false, children); }
}
public sealed record BlockingTreeNode(int ProcessId, bool CycleDetected, IReadOnlyList<BlockingTreeNode> Children);
public sealed record BlockingGraph(IReadOnlyList<BlockingTreeNode> Roots, IReadOnlyList<BlockingRelationship> Edges, bool HasCycles, IReadOnlyList<string> Warnings);
public static class SessionStateClassifier
{
    public static BackendState Classify(BackendSession session) => session.ClassifiedState;
    public static bool IsLongRunning(BackendSession session, ActivityMonitorSettings settings) => session.Duration >= settings.EffectiveLongRunning || session.TransactionDuration >= settings.EffectiveIdleTransaction;
}
public static class BlockingGraphService
{
    public static BlockingGraph Build(IEnumerable<BlockingRelationship> relationships)
    {
        var edges = relationships.DistinctBy(x => (x.BlockedProcessId, x.BlockingProcessId)).OrderBy(x => x.BlockingProcessId).ThenBy(x => x.BlockedProcessId).ToArray();
        var byBlocker = edges.GroupBy(x => x.BlockingProcessId).ToDictionary(x => x.Key, x => x.OrderBy(y => y.BlockedProcessId).ToArray());
        var ids = edges.SelectMany(x => new[] { x.BlockedProcessId, x.BlockingProcessId }).ToHashSet();
        var roots = ids.Except(edges.Select(x => x.BlockedProcessId)).OrderBy(x => x).Select(x => BuildNode(x, byBlocker, new HashSet<int>())).ToList();
        var cycle = edges.Any(x => !roots.Any(r => Contains(r, x.BlockedProcessId)));
        if (cycle) foreach (var id in ids.Where(x => !roots.Any(r => Contains(r, x)))) roots.Add(BuildNode(id, byBlocker, new HashSet<int>()));
        return new(roots.OrderBy(x => x.ProcessId).ToArray(), edges, cycle, cycle ? new[] { "The blocking snapshot contains a cycle or inconsistent session relationships." } : Array.Empty<string>());
    }
    private static BlockingTreeNode BuildNode(int pid, IReadOnlyDictionary<int, BlockingRelationship[]> map, HashSet<int> path) { if (!path.Add(pid)) return new(pid, true, Array.Empty<BlockingTreeNode>()); var children = map.TryGetValue(pid, out var links) ? links.Select(x => BuildNode(x.BlockedProcessId, map, new(path))).ToArray() : Array.Empty<BlockingTreeNode>(); return new(pid, children.Any(x => x.CycleDetected), children); }
    private static bool Contains(BlockingTreeNode node, int pid) => node.ProcessId == pid || node.Children.Any(x => Contains(x, pid));
}
public sealed record ActivityMetricRate(double TransactionsPerSecond, double RollbackRatio, TimeSpan SampleWindow);
public static class ActivityMetricsService
{
    public static ActivityMetricRate Rate(long commits, long rollbacks, long previousCommits, long previousRollbacks, TimeSpan interval)
    { if (interval <= TimeSpan.Zero || commits < previousCommits || rollbacks < previousRollbacks) return new(0, 0, interval); var total = commits - previousCommits + rollbacks - previousRollbacks; return new(total / interval.TotalSeconds, total == 0 ? 0 : (rollbacks - previousRollbacks) / (double)total, interval); }
}
public sealed class ActivityHistoryService(int maximumSnapshots = 60)
{
    private readonly LinkedList<ActivitySnapshot> _snapshots = new(); public IReadOnlyList<ActivitySnapshot> Snapshots => _snapshots.ToArray();
    public void Add(ActivitySnapshot snapshot) { _snapshots.AddLast(snapshot); while (_snapshots.Count > Math.Max(1, maximumSnapshots)) _snapshots.RemoveFirst(); }
    public void Clear() => _snapshots.Clear();
}
public sealed class ActivityRefreshCoordinator
{
    private readonly SemaphoreSlim _gate = new(1, 1); private long _sequence; private long _applied;
    public async Task<ActivitySnapshot?> RefreshAsync(Func<long, CancellationToken, Task<ActivitySnapshot>> loader, CancellationToken cancellationToken = default) { if (!await _gate.WaitAsync(0, cancellationToken)) return null; try { var sequence = Interlocked.Increment(ref _sequence); var snapshot = await loader(sequence, cancellationToken); if (snapshot.Sequence < Interlocked.Read(ref _applied)) return null; Interlocked.Exchange(ref _applied, snapshot.Sequence); return snapshot; } finally { _gate.Release(); } }
}
public static class ActivityExportService
{
    public static string ToJson(ActivitySnapshot snapshot, bool includeQueryText = true, bool includeClientData = true, bool includeUsers = true) { var sessions = snapshot.Sessions.Select(s => new { s.ProcessId, s.Database, User = includeUsers ? s.User : "[redacted]", s.ApplicationName, ClientAddress = includeClientData ? s.ClientAddress : "[redacted]", s.State, Query = includeQueryText && !s.QueryUnavailable ? s.Query : s.QueryUnavailable ? "[unavailable]" : "[redacted]", s.Duration, s.Blocked }); return JsonSerializer.Serialize(new { snapshot.Sequence, snapshot.ServerTime, snapshot.Summary, Sessions = sessions, snapshot.Blocking }, new JsonSerializerOptions { WriteIndented = true }); }
    public static string ToCsv(ActivitySnapshot snapshot, bool includeQueryText = true) { var b = new StringBuilder("ProcessId,Database,User,State,Duration,Blocked,Query\r\n"); foreach (var s in snapshot.Sessions) b.AppendLine(string.Join(',', s.ProcessId, Csv(s.Database), Csv(s.User), Csv(s.State), s.Duration.TotalSeconds.ToString("F1"), s.Blocked, Csv(includeQueryText && !s.QueryUnavailable ? s.Query : s.QueryUnavailable ? "[unavailable]" : "[redacted]"))); return b.ToString(); }
    private static string Csv(string? value) => '"' + (value ?? "").Replace("\"", "\"\"").Replace("\r", " ").Replace("\n", " ") + '"';
}
public sealed record BackendActionResult(bool Accepted, string Code, string Message);
public static class BackendSafety
{
    public static BackendActionResult ValidateTermination(BackendSession target, int currentProcessId, int? monitoringProcessId, int? editorProcessId) { if (target.ProcessId == currentProcessId) return new(false, "self", "Cannot terminate the connection executing this administrative action."); if (target.ProcessId == monitoringProcessId) return new(false, "monitoring-session", "Cannot terminate the Activity Monitor connection."); if (target.ProcessId == editorProcessId || target.IsCurrentEditorSession) return new(false, "query-editor-session", "Cannot terminate the active query-editor session."); if (target.BackendType is "autovacuum worker" or "logical replication worker" or "walreceiver" or "background worker") return new(false, "protected-backend", "This backend type is protected from termination."); return new(true, "eligible", "Backend termination is eligible for PostgreSQL permission checks."); }
}
