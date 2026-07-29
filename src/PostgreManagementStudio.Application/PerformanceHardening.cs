using System.Diagnostics;

namespace PostgreManagementStudio.Application;

public enum PerformanceOperationState { Succeeded, Cancelled, Failed }

public sealed record PerformanceDiagnostic(
    string Operation,
    Guid CorrelationId,
    Guid? LogicalSessionId,
    Guid? ConnectionGenerationId,
    DateTimeOffset StartedAt,
    TimeSpan Duration,
    PerformanceOperationState State,
    long ResultCount,
    long RowsRead,
    long RowsDisplayed,
    long BytesProcessed,
    int DatabaseRoundTrips,
    bool? CacheHit,
    long ApproximateAllocatedBytes,
    TimeSpan UiThreadBlockedTime,
    string? FailureCategory);

public interface IPerformanceDiagnostics
{
    void Record(PerformanceDiagnostic diagnostic);
}

public sealed class NullPerformanceDiagnostics : IPerformanceDiagnostics
{
    public static NullPerformanceDiagnostics Instance { get; } = new();
    private NullPerformanceDiagnostics() { }
    public void Record(PerformanceDiagnostic diagnostic) { }
}

public sealed class TracePerformanceDiagnostics : IPerformanceDiagnostics
{
    public void Record(PerformanceDiagnostic value) => Trace.WriteLine(
        $"performance operation={Safe(value.Operation)} correlation_id={value.CorrelationId:N} " +
        $"logical_session_id={value.LogicalSessionId?.ToString("N") ?? "none"} " +
        $"generation_id={value.ConnectionGenerationId?.ToString("N") ?? "none"} " +
        $"started={value.StartedAt:O} duration_ms={value.Duration.TotalMilliseconds:F1} state={value.State} " +
        $"results={value.ResultCount} rows_read={value.RowsRead} rows_displayed={value.RowsDisplayed} " +
        $"bytes={value.BytesProcessed} round_trips={value.DatabaseRoundTrips} " +
        $"cache_hit={value.CacheHit?.ToString() ?? "unknown"} allocated_bytes={value.ApproximateAllocatedBytes} " +
        $"ui_blocked_ms={value.UiThreadBlockedTime.TotalMilliseconds:F1} " +
        $"failure={Safe(value.FailureCategory ?? "none")}");

    private static string Safe(string value) =>
        value.Replace('\r', '_').Replace('\n', '_').Replace(' ', '_');
}

public sealed class PerformanceOperation : IDisposable
{
    private readonly IPerformanceDiagnostics _diagnostics;
    private readonly long _startedTimestamp = Stopwatch.GetTimestamp();
    private readonly long _allocatedAtStart = GC.GetTotalAllocatedBytes(false);
    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;
    private int _disposed;
    private PerformanceOperationState _state = PerformanceOperationState.Succeeded;
    private string? _failureCategory;

    public PerformanceOperation(
        string operation,
        IPerformanceDiagnostics? diagnostics = null,
        Guid? logicalSessionId = null,
        Guid? connectionGenerationId = null)
    {
        Operation = string.IsNullOrWhiteSpace(operation)
            ? throw new ArgumentException("An operation name is required.", nameof(operation))
            : operation;
        _diagnostics = diagnostics ?? NullPerformanceDiagnostics.Instance;
        LogicalSessionId = logicalSessionId;
        ConnectionGenerationId = connectionGenerationId;
    }

    public string Operation { get; }
    public Guid CorrelationId { get; } = Guid.NewGuid();
    public Guid? LogicalSessionId { get; }
    public Guid? ConnectionGenerationId { get; }
    public long ResultCount { get; set; }
    public long RowsRead { get; set; }
    public long RowsDisplayed { get; set; }
    public long BytesProcessed { get; set; }
    public int DatabaseRoundTrips { get; set; }
    public bool? CacheHit { get; set; }
    public TimeSpan UiThreadBlockedTime { get; set; }

    public void Cancel() => _state = PerformanceOperationState.Cancelled;

    public void Fail(string failureCategory)
    {
        _state = PerformanceOperationState.Failed;
        _failureCategory = string.IsNullOrWhiteSpace(failureCategory) ? "unknown" : failureCategory;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try
        {
            _diagnostics.Record(new(
                Operation,
                CorrelationId,
                LogicalSessionId,
                ConnectionGenerationId,
                _startedAt,
                Stopwatch.GetElapsedTime(_startedTimestamp),
                _state,
                ResultCount,
                RowsRead,
                RowsDisplayed,
                BytesProcessed,
                DatabaseRoundTrips,
                CacheHit,
                Math.Max(0, GC.GetTotalAllocatedBytes(false) - _allocatedAtStart),
                UiThreadBlockedTime,
                _failureCategory));
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"performance diagnostics_failure={ex.GetType().Name}");
        }
    }
}

public enum LatestRequestState { Applied, Superseded, Cancelled, Failed }

public sealed record LatestRequestResult<T>(
    long RequestId,
    long ContextVersion,
    LatestRequestState State,
    T? Value,
    Exception? Error = null)
{
    public bool Applied => State == LatestRequestState.Applied;
}

public sealed class LatestRequestCoordinator<T> : IAsyncDisposable
{
    private readonly object _gate = new();
    private CancellationTokenSource? _current;
    private long _requestId;
    private bool _disposed;
    private int _activeCount;

    public int ActiveCount => Volatile.Read(ref _activeCount);
    public long LatestRequestId => Interlocked.Read(ref _requestId);

    public async Task<LatestRequestResult<T>> RunAsync(
        long contextVersion,
        TimeSpan debounce,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (debounce < TimeSpan.Zero || debounce > TimeSpan.FromSeconds(5))
            throw new ArgumentOutOfRangeException(nameof(debounce));

        CancellationTokenSource owner;
        long requestId;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _current?.Cancel();
            owner = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _current = owner;
            requestId = ++_requestId;
        }

        Interlocked.Increment(ref _activeCount);
        try
        {
            if (debounce > TimeSpan.Zero)
                await Task.Delay(debounce, owner.Token).ConfigureAwait(false);
            var value = await operation(owner.Token).ConfigureAwait(false);
            lock (_gate)
            {
                if (_disposed || _current != owner || requestId != _requestId)
                    return new(requestId, contextVersion, LatestRequestState.Superseded, default);
            }
            return new(requestId, contextVersion, LatestRequestState.Applied, value);
        }
        catch (OperationCanceledException) when (owner.IsCancellationRequested)
        {
            lock (_gate)
            {
                var superseded = !_disposed && (_current != owner || requestId != _requestId);
                return new(requestId, contextVersion,
                    superseded ? LatestRequestState.Superseded : LatestRequestState.Cancelled,
                    default);
            }
        }
        catch (Exception ex)
        {
            return new(requestId, contextVersion, LatestRequestState.Failed, default, ex);
        }
        finally
        {
            lock (_gate)
            {
                if (_current == owner) _current = null;
            }
            owner.Dispose();
            Interlocked.Decrement(ref _activeCount);
        }
    }

    public ValueTask DisposeAsync()
    {
        CancellationTokenSource? current;
        lock (_gate)
        {
            if (_disposed) return ValueTask.CompletedTask;
            _disposed = true;
            current = _current;
            _current = null;
            ++_requestId;
        }
        current?.Cancel();
        return ValueTask.CompletedTask;
    }
}

public static class PerformanceBudgets
{
    public static IReadOnlyDictionary<string, TimeSpan> InteractiveP95 { get; } =
        new Dictionary<string, TimeSpan>(StringComparer.Ordinal)
        {
            ["ApplicationStartup"] = TimeSpan.FromSeconds(2),
            ["ConnectionDialogOpen"] = TimeSpan.FromMilliseconds(250),
            ["LocalConnection"] = TimeSpan.FromSeconds(2),
            ["NewQueryEditor"] = TimeSpan.FromMilliseconds(150),
            ["FirstEditorInput"] = TimeSpan.FromMilliseconds(50),
            ["TrivialQuery"] = TimeSpan.FromSeconds(1),
            ["FirstResultPage"] = TimeSpan.FromMilliseconds(250),
            ["ObjectExplorerExpansion"] = TimeSpan.FromSeconds(2),
            ["IntelliSenseMetadata"] = TimeSpan.FromSeconds(2),
            ["DatabaseObjectSearch"] = TimeSpan.FromSeconds(1),
            ["TabSwitch"] = TimeSpan.FromMilliseconds(100),
            ["BackgroundMonitorUiBlock"] = TimeSpan.FromMilliseconds(50),
            ["CloseLargeResultEditor"] = TimeSpan.FromSeconds(1),
            ["Reconnect"] = TimeSpan.FromSeconds(3),
            ["Shutdown"] = TimeSpan.FromSeconds(5),
        };
}
