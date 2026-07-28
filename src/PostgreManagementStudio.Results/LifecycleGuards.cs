using PostgreManagementStudio.Core;

namespace PostgreManagementStudio.Results;

/// <summary>Centralised valid-transition rules for the result-store state machines.</summary>
internal static class LifecycleGuards
{
    private static readonly HashSet<(ResultSetStatus From, ResultSetStatus To)> SetTransitions = new()
    {
        (ResultSetStatus.Created,    ResultSetStatus.Receiving),
        (ResultSetStatus.Created,    ResultSetStatus.Completed),
        (ResultSetStatus.Created,    ResultSetStatus.Cancelled),
        (ResultSetStatus.Created,    ResultSetStatus.Failed),
        (ResultSetStatus.Created,    ResultSetStatus.Disposed),
        (ResultSetStatus.Receiving,  ResultSetStatus.Completed),
        (ResultSetStatus.Receiving,  ResultSetStatus.Cancelled),
        (ResultSetStatus.Receiving,  ResultSetStatus.Failed),
        (ResultSetStatus.Receiving,  ResultSetStatus.Disposed),
        (ResultSetStatus.Completed,  ResultSetStatus.Disposed),
        (ResultSetStatus.Cancelled,  ResultSetStatus.Disposed),
        (ResultSetStatus.Failed,     ResultSetStatus.Disposed)
    };

    private static readonly HashSet<(ResultSessionStatus From, ResultSessionStatus To)> SessionTransitions = new()
    {
        (ResultSessionStatus.Created,   ResultSessionStatus.Running),
        (ResultSessionStatus.Created,   ResultSessionStatus.Completed),
        (ResultSessionStatus.Created,   ResultSessionStatus.Cancelled),
        (ResultSessionStatus.Created,   ResultSessionStatus.Failed),
        (ResultSessionStatus.Running,   ResultSessionStatus.Completed),
        (ResultSessionStatus.Running,   ResultSessionStatus.Cancelled),
        (ResultSessionStatus.Running,   ResultSessionStatus.Failed),
        (ResultSessionStatus.Completed, ResultSessionStatus.Disposed),
        (ResultSessionStatus.Cancelled, ResultSessionStatus.Disposed),
        (ResultSessionStatus.Failed,    ResultSessionStatus.Disposed)
    };

    public static bool IsValid(ResultSetStatus from, ResultSetStatus to) => SetTransitions.Contains((from, to));

    public static bool IsValid(ResultSessionStatus from, ResultSessionStatus to) => SessionTransitions.Contains((from, to));

    public static void EnsureValid(ResultSetStatus from, ResultSetStatus to)
    {
        if (!IsValid(from, to))
            throw new InvalidOperationException($"Invalid result-set transition: {from} -> {to}");
    }

    public static void EnsureValid(ResultSessionStatus from, ResultSessionStatus to)
    {
        if (!IsValid(from, to))
            throw new InvalidOperationException($"Invalid result-session transition: {from} -> {to}");
    }
}