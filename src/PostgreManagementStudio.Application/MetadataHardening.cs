using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;
using PostgreManagementStudio.Core;

namespace PostgreManagementStudio.Application;

public enum MetadataRequestState
{
    Idle, Queued, Loading, Refreshing, Cancelling, Completed, Failed, Cancelled, Stale, Disposed,
}

public enum MetadataOperation { LoadRoot, Expand, Refresh, LoadProperties, LoadDependencies }
public enum MetadataFailureCategory
{
    Cancelled, PermissionDenied, ConnectionLost, ObjectNotFound, DatabaseUnavailable,
    Timeout, UnsupportedVersion, InvalidMetadata, ProviderFailure, Disposed, Unknown,
}

public sealed record MetadataError(
    MetadataFailureCategory Category,
    string Message,
    string? SqlState = null);

public sealed class MetadataObjectNotFoundException(string message) : InvalidOperationException(message);

public sealed record MetadataDiagnostic(
    Guid RequestId,
    string ConnectionProfileId,
    string DatabaseIdentity,
    string NodeIdentity,
    MetadataOperation Operation,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    int RowsReturned,
    bool CacheHit,
    bool CancellationRequested,
    MetadataRequestState FinalState,
    MetadataFailureCategory? FailureCategory,
    string? SqlState);

public interface IMetadataDiagnostics { void Record(MetadataDiagnostic diagnostic); }
public sealed class NullMetadataDiagnostics : IMetadataDiagnostics
{
    public static NullMetadataDiagnostics Instance { get; } = new();
    public void Record(MetadataDiagnostic diagnostic) { }
}
public sealed class DiagnosticMetadataDiagnostics : IMetadataDiagnostics
{
    public void Record(MetadataDiagnostic value) => Trace.WriteLine(
        $"metadata request_id={value.RequestId} profile_id={Safe(value.ConnectionProfileId)} " +
        $"database={Safe(value.DatabaseIdentity)} node={Safe(value.NodeIdentity)} operation={value.Operation} " +
        $"started={value.StartedAt:O} completed={value.CompletedAt:O} rows={value.RowsReturned} " +
        $"cache_hit={value.CacheHit} cancel_requested={value.CancellationRequested} state={value.FinalState} " +
        $"failure={value.FailureCategory?.ToString() ?? "none"} sqlstate={value.SqlState ?? "none"}");
    private static string Safe(string value) => value.Replace('\r', '_').Replace('\n', '_').Replace(' ', '_');
}

public static class MetadataFailureClassifier
{
    public static MetadataError Classify(Exception exception)
    {
        if (exception is OperationCanceledException)
            return new(MetadataFailureCategory.Cancelled, "Metadata loading was cancelled.");
        if (exception is ObjectDisposedException)
            return new(MetadataFailureCategory.Disposed, "The metadata owner has been closed.");
        if (exception is MetadataObjectNotFoundException)
            return new(MetadataFailureCategory.ObjectNotFound, UserMessage(MetadataFailureCategory.ObjectNotFound));
        if (exception is TimeoutException || exception.InnerException is TimeoutException)
            return new(MetadataFailureCategory.Timeout, "Metadata loading timed out.");
        if (exception is DbException databaseException)
        {
            var sqlState = databaseException.GetType().GetProperty("SqlState")?.GetValue(databaseException) as string;
            var category = sqlState switch
            {
                "42501" => MetadataFailureCategory.PermissionDenied,
                "3D000" => MetadataFailureCategory.DatabaseUnavailable,
                "42P01" or "42704" => MetadataFailureCategory.ObjectNotFound,
                "57P01" or "57P02" or "57P03" => MetadataFailureCategory.ConnectionLost,
                _ when databaseException.GetType().Name.Contains("Npgsql", StringComparison.Ordinal) =>
                    MetadataFailureCategory.ConnectionLost,
                _ => MetadataFailureCategory.ProviderFailure,
            };
            return new(category, UserMessage(category), sqlState);
        }
        return new(MetadataFailureCategory.Unknown, UserMessage(MetadataFailureCategory.Unknown));
    }

    public static string UserMessage(MetadataFailureCategory category) => category switch
    {
        MetadataFailureCategory.PermissionDenied => "PostgreSQL denied access to this metadata. Other visible objects remain available.",
        MetadataFailureCategory.ObjectNotFound => "The object no longer exists or changed while metadata was loading.",
        MetadataFailureCategory.DatabaseUnavailable => "The selected database is unavailable. No fallback database was used.",
        MetadataFailureCategory.ConnectionLost => "The connection was lost while metadata was loading.",
        MetadataFailureCategory.Timeout => "Metadata loading timed out. The node can be retried.",
        MetadataFailureCategory.UnsupportedVersion => "This metadata operation is not supported by the connected PostgreSQL version.",
        MetadataFailureCategory.InvalidMetadata => "PostgreSQL returned metadata that could not be interpreted safely.",
        MetadataFailureCategory.Disposed => "The object browser has been closed.",
        MetadataFailureCategory.Cancelled => "Metadata loading was cancelled.",
        _ => "Metadata could not be loaded.",
    };
}

public readonly record struct MetadataCacheKey(
    string ConnectionProfileId,
    string ConfigurationIdentity,
    string Database,
    PostgresObjectIdentity? ObjectIdentity,
    MetadataOperation Operation,
    bool ShowSystemObjects);

public sealed class BoundedMetadataCache
{
    private sealed record Entry(object Value, DateTimeOffset ExpiresAt, long Sequence);
    private readonly ConcurrentDictionary<MetadataCacheKey, Entry> _entries = new();
    private readonly TimeSpan _lifetime;
    private readonly int _capacity;
    private long _sequence;

    public BoundedMetadataCache(int capacity = 256, TimeSpan? lifetime = null)
    {
        if (capacity is < 1 or > 4096) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
        _lifetime = lifetime ?? TimeSpan.FromMinutes(2);
        if (_lifetime <= TimeSpan.Zero || _lifetime > TimeSpan.FromHours(1))
            throw new ArgumentOutOfRangeException(nameof(lifetime));
    }

    public bool TryGet<T>(MetadataCacheKey key, out T? value)
    {
        if (_entries.TryGetValue(key, out var entry))
        {
            if (entry.ExpiresAt > DateTimeOffset.UtcNow && entry.Value is T typed)
            {
                value = typed;
                return true;
            }
            _entries.TryRemove(key, out _);
        }
        value = default;
        return false;
    }

    public void Store<T>(MetadataCacheKey key, T value) where T : notnull
    {
        _entries[key] = new(value, DateTimeOffset.UtcNow + _lifetime, Interlocked.Increment(ref _sequence));
        while (_entries.Count > _capacity && _entries.MinBy(x => x.Value.Sequence) is { } oldest)
            _entries.TryRemove(oldest.Key, out _);
    }

    public void Invalidate(ObjectMetadataContext context, PostgresObjectIdentity? identity = null)
    {
        foreach (var key in _entries.Keys.Where(x =>
            x.ConnectionProfileId == context.ConnectionProfileId
            && x.ConfigurationIdentity == context.ConfigurationIdentity
            && x.Database == context.Database
            && (identity is null || Equals(x.ObjectIdentity, identity))))
            _entries.TryRemove(key, out _);
    }

    public void InvalidateProfile(string profileId)
    {
        foreach (var key in _entries.Keys.Where(x => x.ConnectionProfileId == profileId))
            _entries.TryRemove(key, out _);
    }

    public int Count => _entries.Count;
}

public sealed record MetadataLoadResult<T>(
    Guid RequestId,
    long Generation,
    MetadataRequestState State,
    T? Value,
    MetadataError? Error,
    bool CacheHit);

public sealed class MetadataRequestController : IAsyncDisposable
{
    private readonly object _gate = new();
    private CancellationTokenSource? _active;
    private Guid? _requestId;
    private long _generation;
    private MetadataRequestState _state;
    private bool _disposed;

    public MetadataRequestState State { get { lock (_gate) return _state; } }
    public Guid? RequestId { get { lock (_gate) return _requestId; } }
    public long Generation { get { lock (_gate) return _generation; } }

    public async Task<MetadataLoadResult<T>> RunAsync<T>(
        bool refresh,
        Func<CancellationToken, Task<(T Value, bool CacheHit)>> loader,
        CancellationToken cancellationToken = default)
    {
        Guid requestId;
        long generation;
        CancellationToken token;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _active?.Cancel();
            _active?.Dispose();
            _active = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            requestId = Guid.NewGuid();
            _requestId = requestId;
            generation = ++_generation;
            _state = refresh ? MetadataRequestState.Refreshing : MetadataRequestState.Queued;
            token = _active.Token;
        }
        lock (_gate) if (_requestId == requestId) _state = refresh ? MetadataRequestState.Refreshing : MetadataRequestState.Loading;
        try
        {
            var loaded = await loader(token).ConfigureAwait(false);
            lock (_gate)
            {
                if (_disposed || _requestId != requestId)
                    return new(requestId, generation, MetadataRequestState.Stale, default, null, loaded.CacheHit);
                _state = MetadataRequestState.Completed;
            }
            return new(requestId, generation, MetadataRequestState.Completed, loaded.Value, null, loaded.CacheHit);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            lock (_gate)
            {
                if (_disposed || _requestId != requestId)
                    return new(requestId, generation, MetadataRequestState.Stale, default, null, false);
                _state = MetadataRequestState.Cancelled;
            }
            return new(requestId, generation, MetadataRequestState.Cancelled, default,
                MetadataFailureClassifier.Classify(new OperationCanceledException()), false);
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                if (_disposed || _requestId != requestId)
                    return new(requestId, generation, MetadataRequestState.Stale, default, null, false);
                _state = MetadataRequestState.Failed;
            }
            return new(requestId, generation, MetadataRequestState.Failed, default, MetadataFailureClassifier.Classify(ex), false);
        }
    }

    public void Cancel()
    {
        lock (_gate)
        {
            if (_disposed || _state is MetadataRequestState.Idle or MetadataRequestState.Completed
                or MetadataRequestState.Failed or MetadataRequestState.Cancelled or MetadataRequestState.Stale) return;
            _state = MetadataRequestState.Cancelling;
            _active?.Cancel();
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposed) return ValueTask.CompletedTask;
            _disposed = true;
            _state = MetadataRequestState.Disposed;
            _requestId = Guid.NewGuid();
            _active?.Cancel();
            _active?.Dispose();
            _active = null;
        }
        return ValueTask.CompletedTask;
    }
}

public sealed class HardenedMetadataService(
    IPostgresObjectMetadataProvider provider,
    BoundedMetadataCache cache,
    IMetadataDiagnostics? diagnostics = null)
{
    private readonly IMetadataDiagnostics _diagnostics = diagnostics ?? NullMetadataDiagnostics.Instance;

    public Task<MetadataLoadResult<ObjectMetadataRoot>> LoadRootAsync(
        ObjectMetadataContext context,
        MetadataRequestController controller,
        bool refresh = false,
        CancellationToken cancellationToken = default) =>
        LoadAsync(context, null, MetadataOperation.LoadRoot, controller, refresh,
            token => provider.LoadRootAsync(context, token), cancellationToken);

    public void Invalidate(ObjectMetadataContext context) => cache.Invalidate(context);

    public Task<MetadataLoadResult<ObjectMetadataBatch>> LoadChildrenAsync(
        ObjectMetadataContext context,
        PostgresObjectIdentity parent,
        MetadataRequestController controller,
        bool refresh = false,
        CancellationToken cancellationToken = default) =>
        LoadAsync(context, parent, refresh ? MetadataOperation.Refresh : MetadataOperation.Expand,
            controller, refresh, token => provider.LoadChildrenAsync(context, parent, token), cancellationToken);

    private async Task<MetadataLoadResult<T>> LoadAsync<T>(
        ObjectMetadataContext context,
        PostgresObjectIdentity? parent,
        MetadataOperation operation,
        MetadataRequestController controller,
        bool refresh,
        Func<CancellationToken, Task<T>> loader,
        CancellationToken cancellationToken) where T : notnull
    {
        var key = new MetadataCacheKey(context.ConnectionProfileId, context.ConfigurationIdentity,
            context.Database, parent, operation == MetadataOperation.Refresh ? MetadataOperation.Expand : operation,
            context.ShowSystemObjects);
        if (refresh) cache.Invalidate(context, parent);
        var startedAt = DateTimeOffset.UtcNow;
        var result = await controller.RunAsync(refresh, async token =>
        {
            token.ThrowIfCancellationRequested();
            if (!refresh && cache.TryGet<T>(key, out var cached)) return (cached!, true);
            var value = await loader(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            cache.Store(key, value);
            return (value, false);
        }, cancellationToken).ConfigureAwait(false);
        var rows = result.Value switch
        {
            ObjectMetadataRoot root => root.Schemas.Count,
            ObjectMetadataBatch batch => batch.Objects.Count,
            _ => 0,
        };
        _diagnostics.Record(new(result.RequestId, context.ConnectionProfileId, context.Database,
            parent?.ToString() ?? "database", operation, startedAt, DateTimeOffset.UtcNow, rows,
            result.CacheHit, cancellationToken.IsCancellationRequested,
            result.State, result.Error?.Category, result.Error?.SqlState));
        return result;
    }
}

public static class ObjectMetadataRules
{
    public static MetadataSystemClassification ClassifySchema(string name) =>
        name.Equals("pg_catalog", StringComparison.Ordinal) ? MetadataSystemClassification.Catalog
        : name.Equals("information_schema", StringComparison.Ordinal) ? MetadataSystemClassification.InformationSchema
        : name.StartsWith("pg_toast_temp_", StringComparison.Ordinal) ? MetadataSystemClassification.TemporaryToast
        : name.StartsWith("pg_temp_", StringComparison.Ordinal) ? MetadataSystemClassification.Temporary
        : name.Equals("pg_toast", StringComparison.Ordinal) || name.StartsWith("pg_toast_", StringComparison.Ordinal)
            ? MetadataSystemClassification.Toast
        : MetadataSystemClassification.User;

    public static IReadOnlyList<ObjectMetadataDescriptor> Sort(IEnumerable<ObjectMetadataDescriptor> values) =>
        Array.AsReadOnly(values.OrderBy(x => x.Identity.ObjectClass)
            .ThenBy(x => x.SchemaName, StringComparer.Ordinal)
            .ThenBy(x => x.Ordinal ?? int.MaxValue)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Name, StringComparer.Ordinal)
            .ThenBy(x => x.RoutineSignature, StringComparer.Ordinal)
            .ThenBy(x => x.Identity.ObjectOid)
            .ToArray());

    public static IReadOnlyList<ObjectMetadataDescriptor> Filter(
        IEnumerable<ObjectMetadataDescriptor> values,
        bool showSystemObjects) =>
        Sort(showSystemObjects ? values : values.Where(x =>
            x.SystemClassification is MetadataSystemClassification.User or MetadataSystemClassification.ExtensionOwned));
}

public static class MetadataReconciler
{
    public static IReadOnlyList<ObjectMetadataDescriptor> Reconcile(
        IEnumerable<ObjectMetadataDescriptor> current,
        IEnumerable<ObjectMetadataDescriptor> incoming) =>
        ObjectMetadataRules.Sort(incoming
            .GroupBy(x => x.Identity)
            .Select(x => x.Last()));
}
