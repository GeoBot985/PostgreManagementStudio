using System.Security.Cryptography;
using System.Text;
using PostgreManagementStudio.Core;

namespace PostgreManagementStudio.Application;

public enum ObjectExplorerNodeKind
{
    Database, Schema, Tables, Views, MaterializedViews, Sequences, Functions, Procedures,
    Table, View, MaterializedView, Sequence, Function, Procedure, Column, Constraint, Index, Trigger,
    Types, EnumType, Domain, CompositeType, Extension,
}

public sealed class ObjectExplorerNode : IAsyncDisposable
{
    private readonly object _gate = new();
    private IReadOnlyList<ObjectExplorerNode> _children;
    private Task<ObjectExplorerNode>? _activeLoad;

    internal ObjectExplorerNode(
        ObjectExplorerNodeKind kind,
        string name,
        string? qualifiedName,
        PostgresObjectIdentity identity,
        bool hasChildren,
        bool isLoaded,
        IReadOnlyList<ObjectExplorerNode>? children = null,
        MetadataRequestController? request = null)
    {
        Kind = kind;
        Name = name;
        RawName = identity.NameSnapshot;
        QualifiedName = qualifiedName;
        Identity = identity;
        HasChildren = hasChildren;
        IsLoaded = isLoaded;
        _children = children ?? Array.Empty<ObjectExplorerNode>();
        Request = request ?? new MetadataRequestController();
    }

    public ObjectExplorerNodeKind Kind { get; private set; }
    public string Name { get; private set; }
    public string RawName { get; private set; }
    public string? QualifiedName { get; private set; }
    public PostgresObjectIdentity Identity { get; }
    public bool HasChildren { get; private set; }
    public bool CanModify { get; internal set; }
    public bool IsLoaded { get; private set; }
    public bool IsStale { get; private set; }
    public bool IsLoading => Request.State is MetadataRequestState.Queued or MetadataRequestState.Loading
        or MetadataRequestState.Refreshing or MetadataRequestState.Cancelling;
    public MetadataRequestState State => Request.State;
    public MetadataError? Error { get; private set; }
    public IReadOnlyList<ObjectExplorerNode> Children { get { lock (_gate) return _children; } }
    internal MetadataRequestController Request { get; }

    internal Task<ObjectExplorerNode>? ActiveLoad { get { lock (_gate) return _activeLoad; } }
    internal void SetActiveLoad(Task<ObjectExplorerNode>? value) { lock (_gate) _activeLoad = value; }

    internal void ApplyDescriptor(ObjectMetadataDescriptor descriptor)
    {
        lock (_gate)
        {
            Kind = MapKind(descriptor.Identity.ObjectClass);
            Name = descriptor.DisplayName;
            RawName = descriptor.Name;
            QualifiedName = descriptor.QualifiedName;
            HasChildren = descriptor.HasChildren;
            CanModify = descriptor.CanModify;
        }
    }

    internal void ApplyChildren(IEnumerable<ObjectExplorerNode> incoming)
    {
        List<ObjectExplorerNode> removed;
        lock (_gate)
        {
            var existing = _children.ToDictionary(x => x.Identity);
            var reconciled = new List<ObjectExplorerNode>();
            foreach (var candidate in incoming)
            {
                if (existing.TryGetValue(candidate.Identity, out var retained))
                {
                    retained.Name = candidate.Name;
                    retained.RawName = candidate.RawName;
                    retained.QualifiedName = candidate.QualifiedName;
                    retained.Kind = candidate.Kind;
                    retained.HasChildren = candidate.HasChildren;
                    retained.CanModify = candidate.CanModify;
                    if (candidate.IsLoaded) retained.ApplyChildren(candidate.Children);
                    reconciled.Add(retained);
                }
                else reconciled.Add(candidate);
            }
            removed = existing.Values.Where(x => !reconciled.Contains(x)).ToList();
            _children = reconciled;
            IsLoaded = true;
            IsStale = false;
            Error = null;
        }
        foreach (var node in removed)
            node.DisposeTree();
    }

    internal void ApplyError(MetadataError? error)
    {
        lock (_gate) Error = error;
    }

    public void Cancel() => Request.Cancel();

    internal void MarkStale()
    {
        ObjectExplorerNode[] children;
        lock (_gate)
        {
            IsStale = true;
            children = _children.ToArray();
        }
        Request.Cancel();
        foreach (var child in children) child.MarkStale();
    }

    internal void ClearStale()
    {
        ObjectExplorerNode[] children;
        lock (_gate)
        {
            IsStale = false;
            children = _children.ToArray();
        }
        foreach (var child in children) child.ClearStale();
    }

    public ValueTask DisposeAsync()
    {
        DisposeTree();
        return ValueTask.CompletedTask;
    }

    internal void DisposeTree()
    {
        ObjectExplorerNode[] children;
        lock (_gate)
        {
            children = _children.ToArray();
            _children = Array.Empty<ObjectExplorerNode>();
            _activeLoad = null;
        }
        _ = Request.DisposeAsync();
        foreach (var child in children) child.DisposeTree();
    }

    internal static ObjectExplorerNodeKind MapKind(PostgresObjectClass value) => value switch
    {
        PostgresObjectClass.Database => ObjectExplorerNodeKind.Database,
        PostgresObjectClass.Schema => ObjectExplorerNodeKind.Schema,
        PostgresObjectClass.View => ObjectExplorerNodeKind.View,
        PostgresObjectClass.MaterializedView => ObjectExplorerNodeKind.MaterializedView,
        PostgresObjectClass.Sequence => ObjectExplorerNodeKind.Sequence,
        PostgresObjectClass.Procedure => ObjectExplorerNodeKind.Procedure,
        PostgresObjectClass.Function or PostgresObjectClass.Aggregate or PostgresObjectClass.WindowFunction =>
            ObjectExplorerNodeKind.Function,
        PostgresObjectClass.Column => ObjectExplorerNodeKind.Column,
        PostgresObjectClass.Constraint => ObjectExplorerNodeKind.Constraint,
        PostgresObjectClass.Index => ObjectExplorerNodeKind.Index,
        PostgresObjectClass.Trigger => ObjectExplorerNodeKind.Trigger,
        PostgresObjectClass.EnumType => ObjectExplorerNodeKind.EnumType,
        PostgresObjectClass.Domain => ObjectExplorerNodeKind.Domain,
        PostgresObjectClass.CompositeType => ObjectExplorerNodeKind.CompositeType,
        PostgresObjectClass.Extension => ObjectExplorerNodeKind.Extension,
        _ => ObjectExplorerNodeKind.Table,
    };
}

public sealed class ObjectExplorerService : IAsyncDisposable, IDisposable
{
    private readonly HardenedMetadataService _metadata;
    private ObjectMetadataContext? _context;
    private ObjectExplorerNode? _root;
    private bool _isStale;
    private Guid _connectionGenerationId;
    private long _databaseRoundTrips;
    private readonly SemaphoreSlim _refreshConcurrency = new(4, 4);
    private int _disposed;

    public ObjectExplorerService(
        IPostgresObjectMetadataProvider provider,
        BoundedMetadataCache? cache = null,
        IMetadataDiagnostics? diagnostics = null)
        : this(new HardenedMetadataService(provider, cache ?? new BoundedMetadataCache(), diagnostics)) { }

    public ObjectExplorerService(HardenedMetadataService metadata) => _metadata = metadata;
    public bool IsStale => _isStale;
    public ObjectExplorerNode? CurrentRoot => _root;
    public long DatabaseRoundTrips => Interlocked.Read(ref _databaseRoundTrips);

    public bool IsCurrent(ObjectExplorerNode node, string connectionString, string database)
    {
        var expected = BuildContext(connectionString, database, showSystemObjects: false);
        return !_isStale && !node.IsStale && _context is not null && _root is not null
            && _context.ConfigurationIdentity == expected.ConfigurationIdentity
            && string.Equals(_context.Database, expected.Database, StringComparison.Ordinal)
            && node.Identity.ConfigurationIdentity == _context.ConfigurationIdentity
            && node.Identity.DatabaseOid == _root.Identity.DatabaseOid
            && node.Identity.ServerFingerprint == _root.Identity.ServerFingerprint;
    }

    public async Task<ObjectExplorerNode> LoadRootAsync(
        string connectionString,
        string database,
        bool showSystemObjects = false,
        bool refresh = false,
        Guid connectionGenerationId = default,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(database);
        var context = BuildContext(connectionString, database, showSystemObjects);
        if (_connectionGenerationId != Guid.Empty && connectionGenerationId != Guid.Empty
            && _connectionGenerationId != connectionGenerationId)
        {
            if (_context is not null) _metadata.Invalidate(_context);
            refresh = true;
        }
        if (connectionGenerationId != Guid.Empty) _connectionGenerationId = connectionGenerationId;
        if (_context is not null && (_context.ConfigurationIdentity != context.ConfigurationIdentity
            || _context.Database != context.Database || _context.ShowSystemObjects != context.ShowSystemObjects))
        {
            if (_root is not null) await _root.DisposeAsync();
            _root = null;
        }
        _context = context;
        var controller = _root?.Request ?? new MetadataRequestController();
        var result = await _metadata.LoadRootAsync(context, controller, refresh, cancellationToken).ConfigureAwait(false);
        if (!result.CacheHit) Interlocked.Increment(ref _databaseRoundTrips);
        if (result.State == MetadataRequestState.Cancelled) throw new OperationCanceledException(cancellationToken);
        if (result.State == MetadataRequestState.Stale) throw new OperationCanceledException("A newer metadata request superseded this request.");
        if (result.Value is null) throw new MetadataLoadException(result.Error!);
        var rootData = result.Value;
        if (_root is null || !_root.Identity.Equals(rootData.DatabaseIdentity))
        {
            if (_root is not null) await _root.DisposeAsync();
            _root = new(ObjectExplorerNodeKind.Database, rootData.DatabaseName, rootData.DatabaseName,
                rootData.DatabaseIdentity, true, true, request: controller);
        }
        _root.ApplyChildren(rootData.Schemas.Select(ToNode));
        _root.ClearStale();
        _isStale = false;
        if (refresh)
            await RefreshLoadedDescendantsAsync(_root, cancellationToken).ConfigureAwait(false);
        return _root;
    }

    public async Task<ObjectExplorerNode> LoadDatabaseAsync(
        string connectionString,
        string database,
        CancellationToken cancellationToken = default)
    {
        var root = await LoadRootAsync(connectionString, database, cancellationToken: cancellationToken);
        foreach (var schema in root.Children)
            await ExpandAsync(schema, cancellationToken: cancellationToken);
        return root;
    }

    public Task<ObjectExplorerNode> ExpandAsync(
        ObjectExplorerNode node,
        bool refresh = false,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (_isStale || node.IsStale)
            throw new InvalidOperationException("Object Explorer metadata is stale. Reconnect and refresh before loading more objects.");
        if (!refresh && node.IsLoaded) return Task.FromResult(node);
        if (!refresh && node.ActiveLoad is { } active) return active;
        var task = ExpandCoreAsync(node, refresh, cancellationToken);
        node.SetActiveLoad(task);
        if (task.IsCompleted) node.SetActiveLoad(null);
        return task;
    }

    private async Task<ObjectExplorerNode> ExpandCoreAsync(
        ObjectExplorerNode node,
        bool refresh,
        CancellationToken cancellationToken)
    {
        try
        {
            var context = _context ?? throw new InvalidOperationException("Load the database root before expanding a node.");
            var result = await _metadata.LoadChildrenAsync(context, node.Identity, node.Request, refresh, cancellationToken)
                .ConfigureAwait(false);
            if (!result.CacheHit) Interlocked.Increment(ref _databaseRoundTrips);
            if (result.State is MetadataRequestState.Cancelled or MetadataRequestState.Stale) return node;
            if (result.Value is null)
            {
                node.ApplyError(result.Error);
                return node;
            }
            node.ApplyChildren(BuildChildren(node, result.Value.Objects));
            return node;
        }
        finally
        {
            node.SetActiveLoad(null);
        }
    }

    private static IReadOnlyList<ObjectExplorerNode> BuildChildren(
        ObjectExplorerNode parent,
        IReadOnlyList<ObjectMetadataDescriptor> descriptors)
    {
        if (parent.Kind != ObjectExplorerNodeKind.Schema)
            return descriptors.Select(ToNode).ToArray();

        var groups = new[]
        {
            Group(parent, ObjectExplorerNodeKind.Tables, "Tables", descriptors.Where(x =>
                x.Identity.ObjectClass is PostgresObjectClass.Table or PostgresObjectClass.PartitionedTable
                    or PostgresObjectClass.Partition or PostgresObjectClass.ForeignTable)),
            Group(parent, ObjectExplorerNodeKind.Views, "Views", descriptors.Where(x => x.Identity.ObjectClass == PostgresObjectClass.View)),
            Group(parent, ObjectExplorerNodeKind.MaterializedViews, "Materialized Views", descriptors.Where(x => x.Identity.ObjectClass == PostgresObjectClass.MaterializedView)),
            Group(parent, ObjectExplorerNodeKind.Sequences, "Sequences", descriptors.Where(x => x.Identity.ObjectClass == PostgresObjectClass.Sequence)),
            Group(parent, ObjectExplorerNodeKind.Functions, "Functions", descriptors.Where(x =>
                x.Identity.ObjectClass is PostgresObjectClass.Function or PostgresObjectClass.Aggregate or PostgresObjectClass.WindowFunction)),
            Group(parent, ObjectExplorerNodeKind.Procedures, "Procedures", descriptors.Where(x => x.Identity.ObjectClass == PostgresObjectClass.Procedure)),
            Group(parent, ObjectExplorerNodeKind.Types, "Types", descriptors.Where(x =>
                x.Identity.ObjectClass is PostgresObjectClass.EnumType or PostgresObjectClass.Domain
                    or PostgresObjectClass.CompositeType)),
        };
        return groups;
    }

    private async Task RefreshLoadedDescendantsAsync(
        ObjectExplorerNode parent,
        CancellationToken cancellationToken)
    {
        var tasks = parent.Children.Select(async child =>
        {
            if (child.Identity.ObjectClass == PostgresObjectClass.Unknown)
            {
                await RefreshLoadedDescendantsAsync(child, cancellationToken).ConfigureAwait(false);
                return;
            }
            if (!child.IsLoaded || !child.HasChildren) return;
            await _refreshConcurrency.WaitAsync(cancellationToken).ConfigureAwait(false);
            try { await ExpandAsync(child, refresh: true, cancellationToken).ConfigureAwait(false); }
            finally { _refreshConcurrency.Release(); }
            await RefreshLoadedDescendantsAsync(child, cancellationToken).ConfigureAwait(false);
        });
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private static ObjectExplorerNode Group(
        ObjectExplorerNode parent,
        ObjectExplorerNodeKind kind,
        string name,
        IEnumerable<ObjectMetadataDescriptor> children)
    {
        var identity = new PostgresObjectIdentity
        {
            ConnectionProfileId = parent.Identity.ConnectionProfileId,
            ConfigurationIdentity = parent.Identity.ConfigurationIdentity,
            ServerFingerprint = parent.Identity.ServerFingerprint,
            DatabaseOid = parent.Identity.DatabaseOid,
            ObjectOid = parent.Identity.ObjectOid,
            ObjectClass = PostgresObjectClass.Unknown,
            ParentOid = parent.Identity.ObjectOid,
            SchemaOid = parent.Identity.SchemaOid,
            SubObjectNumber = (int)kind,
            NameSnapshot = name,
        };
        return new(kind, name, null, identity, true, true, children.Select(ToNode).ToArray());
    }

    private static ObjectExplorerNode ToNode(ObjectMetadataDescriptor descriptor)
    {
        var node = new ObjectExplorerNode(ObjectExplorerNode.MapKind(descriptor.Identity.ObjectClass),
            descriptor.DisplayName, descriptor.QualifiedName, descriptor.Identity,
            descriptor.HasChildren, !descriptor.HasChildren);
        node.CanModify = descriptor.CanModify;
        return node;
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return ValueTask.CompletedTask;
        _root?.DisposeTree();
        _root = null;
        _context = null;
        _connectionGenerationId = Guid.Empty;
        _refreshConcurrency.Dispose();
        return ValueTask.CompletedTask;
    }

    public void MarkStale()
    {
        if (_isStale) return;
        _isStale = true;
        if (_context is not null) _metadata.Invalidate(_context);
        _root?.MarkStale();
    }

    public void Dispose() => DisposeAsync();

    private static ObjectMetadataContext BuildContext(
        string connectionString,
        string database,
        bool showSystemObjects)
    {
        var identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(connectionString)));
        return new()
        {
            ConnectionProfileId = "environment:PMS_CONNECTION_STRING",
            ConfigurationIdentity = identity,
            ConnectionString = connectionString,
            Database = database.Trim(),
            ShowSystemObjects = showSystemObjects,
        };
    }
}

public sealed class MetadataLoadException(MetadataError error) : InvalidOperationException(error.Message)
{
    public MetadataError Error { get; } = error;
}
