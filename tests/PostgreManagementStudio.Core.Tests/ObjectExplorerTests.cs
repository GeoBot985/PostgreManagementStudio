using PostgreManagementStudio.Application;
using PostgreManagementStudio.Core;

namespace PostgreManagementStudio.Core.Tests;

public sealed class ObjectExplorerTests
{
    [Fact]
    [Trait("Category", "Component")]
    [Trait("Priority", "P0")]
    public async Task ObjectExplorerLoadsRootLazilyAndExpansionIsDeduplicated()
    {
        var provider = new RecordingProvider();
        await using var service = new ObjectExplorerService(provider);
        var root = await service.LoadRootAsync("Host=example", "regression");

        Assert.Equal(1, provider.RootLoads);
        Assert.Equal(0, provider.ChildLoads);
        var schema = Assert.Single(root.Children);
        Assert.False(schema.IsLoaded);

        var first = service.ExpandAsync(schema);
        var second = service.ExpandAsync(schema);
        Assert.Same(first, second);
        provider.CompleteChildren();
        await Task.WhenAll(first, second);

        Assert.Equal(1, provider.ChildLoads);
        Assert.True(schema.IsLoaded);
        var tables = schema.Children.Single(x => x.Kind == ObjectExplorerNodeKind.Tables);
        Assert.Single(tables.Children);
        Assert.Equal("\"Sales Data\".\"Order\"", tables.Children[0].QualifiedName);
        Assert.Contains(schema.Children.SelectMany(x => x.Children), x => x.Name.Contains("Résumé", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RefreshPreservesNodeByOidAcrossRenameAndRemovesDroppedObjects()
    {
        var provider = new MutableProvider();
        await using var service = new ObjectExplorerService(provider);
        var root = await service.LoadRootAsync("Host=example", "regression");
        var schema = Assert.Single(root.Children);
        await service.ExpandAsync(schema);
        var original = schema.Children.SelectMany(x => x.Children).Single(x => x.Name == "Before");

        provider.Version = 2;
        await service.ExpandAsync(schema, refresh: true);
        var renamed = schema.Children.SelectMany(x => x.Children).Single(x => x.Name == "After");
        Assert.Same(original, renamed);
        Assert.DoesNotContain(schema.Children.SelectMany(x => x.Children), x => x.Name == "Dropped");
    }

    [Fact]
    [Trait("Category", "MutationSample")]
    [Trait("Priority", "P1")]
    public async Task ObjectExplorerPropagatesRootCancellationAndRemainsRetryable()
    {
        var provider = new CancellingProvider();
        await using var service = new ObjectExplorerService(provider);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.LoadRootAsync("Host=example", "regression", cancellationToken: cancellation.Token));
    }

    [Fact]
    public async Task DatabaseRefreshRecursivelyRefreshesOnlyPreviouslyLoadedBranches()
    {
        var provider = new RecursiveProvider();
        await using var service = new ObjectExplorerService(provider);
        var root = await service.LoadRootAsync("Host=example", "regression");
        var schema = Assert.Single(root.Children);
        await service.ExpandAsync(schema);
        var table = Assert.Single(schema.Children.SelectMany(x => x.Children));
        await service.ExpandAsync(table);
        Assert.Equal("old_column", Assert.Single(table.Children).Name);

        provider.Version = 2;
        var refreshed = await service.LoadRootAsync("Host=example", "regression", refresh: true);
        var retainedSchema = Assert.Single(refreshed.Children);
        var retainedTable = Assert.Single(retainedSchema.Children.SelectMany(x => x.Children));
        Assert.Same(schema, retainedSchema);
        Assert.Same(table, retainedTable);
        Assert.Equal("new_column", Assert.Single(retainedTable.Children).Name);
        Assert.Equal(2, provider.SchemaLoads);
        Assert.Equal(2, provider.TableLoads);
    }

    [Fact]
    public async Task NodeFailureIsLocalRetryableAndBrowserDisposeIsTerminal()
    {
        var provider = new FailingChildProvider();
        var service = new ObjectExplorerService(provider);
        var root = await service.LoadRootAsync("Host=example", "regression");
        var schema = Assert.Single(root.Children);
        await service.ExpandAsync(schema);
        Assert.False(schema.IsLoaded);
        Assert.Equal(MetadataFailureCategory.PermissionDenied, schema.Error!.Category);
        Assert.Empty(schema.Children);

        provider.Fail = false;
        await service.ExpandAsync(schema);
        Assert.True(schema.IsLoaded);
        Assert.Null(schema.Error);
        await service.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            service.LoadRootAsync("Host=example", "regression"));
    }

    private static PostgresObjectIdentity Identity(
        uint oid,
        PostgresObjectClass objectClass,
        string name,
        uint? parent = null) => new()
    {
        ConnectionProfileId = "environment:PMS_CONNECTION_STRING",
        ConfigurationIdentity = Hash("Host=example"),
        ServerFingerprint = "server",
        DatabaseOid = 1,
        ObjectOid = oid,
        ObjectClass = objectClass,
        ParentOid = parent,
        SchemaOid = objectClass == PostgresObjectClass.Schema ? oid : parent,
        NameSnapshot = name,
    };

    private static string Hash(string value) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));

    private sealed class RecordingProvider : IPostgresObjectMetadataProvider
    {
        private readonly TaskCompletionSource<ObjectMetadataBatch> _children =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int RootLoads { get; private set; }
        public int ChildLoads { get; private set; }

        public Task<ObjectMetadataRoot> LoadRootAsync(ObjectMetadataContext context, CancellationToken cancellationToken = default)
        {
            RootLoads++;
            var database = Identity(1, PostgresObjectClass.Database, "regression");
            var schema = new ObjectMetadataDescriptor(Identity(2, PostgresObjectClass.Schema, "Sales Data", 1),
                "Sales Data", "Sales Data", "Sales Data", "\"Sales Data\"", MetadataSystemClassification.User, true);
            return Task.FromResult(new ObjectMetadataRoot(database, "regression", "180000", [schema], DateTimeOffset.UtcNow));
        }

        public Task<ObjectMetadataBatch> LoadChildrenAsync(ObjectMetadataContext context, PostgresObjectIdentity parent, CancellationToken cancellationToken = default)
        {
            ChildLoads++;
            return _children.Task.WaitAsync(cancellationToken);
        }

        public void CompleteChildren()
        {
            var schema = Identity(2, PostgresObjectClass.Schema, "Sales Data", 1);
            _children.TrySetResult(new(schema,
            [
                new(Identity(10, PostgresObjectClass.Table, "Order", 2), "Order", "Sales Data", "Order",
                    "\"Sales Data\".\"Order\"", MetadataSystemClassification.User, true),
                new(Identity(11, PostgresObjectClass.View, "Résumé", 2), "Résumé", "Sales Data", "Résumé",
                    "\"Sales Data\".\"Résumé\"", MetadataSystemClassification.User, true),
            ], DateTimeOffset.UtcNow));
        }
    }

    private sealed class MutableProvider : IPostgresObjectMetadataProvider
    {
        public int Version { get; set; } = 1;
        public Task<ObjectMetadataRoot> LoadRootAsync(ObjectMetadataContext context, CancellationToken cancellationToken = default)
        {
            var database = Identity(1, PostgresObjectClass.Database, "regression");
            var schema = new ObjectMetadataDescriptor(Identity(2, PostgresObjectClass.Schema, "public", 1),
                "public", "public", "public", "public", MetadataSystemClassification.User, true);
            return Task.FromResult(new ObjectMetadataRoot(database, "regression", "180000", [schema], DateTimeOffset.UtcNow));
        }

        public Task<ObjectMetadataBatch> LoadChildrenAsync(ObjectMetadataContext context, PostgresObjectIdentity parent, CancellationToken cancellationToken = default)
        {
            var values = Version == 1
                ? new[]
                {
                    Descriptor(10, "Before"),
                    Descriptor(11, "Dropped"),
                }
                : new[] { Descriptor(10, "After") };
            return Task.FromResult(new ObjectMetadataBatch(parent, values, DateTimeOffset.UtcNow));
        }

        private static ObjectMetadataDescriptor Descriptor(uint oid, string name) =>
            new(Identity(oid, PostgresObjectClass.Table, name, 2), name, "public", name,
                $"public.{name}", MetadataSystemClassification.User, true);
    }

    private sealed class CancellingProvider : IPostgresObjectMetadataProvider
    {
        public Task<ObjectMetadataRoot> LoadRootAsync(ObjectMetadataContext context, CancellationToken cancellationToken = default) =>
            Task.FromCanceled<ObjectMetadataRoot>(cancellationToken);
        public Task<ObjectMetadataBatch> LoadChildrenAsync(ObjectMetadataContext context, PostgresObjectIdentity parent, CancellationToken cancellationToken = default) =>
            Task.FromCanceled<ObjectMetadataBatch>(cancellationToken);
    }

    private sealed class RecursiveProvider : IPostgresObjectMetadataProvider
    {
        public int Version { get; set; } = 1;
        public int SchemaLoads { get; private set; }
        public int TableLoads { get; private set; }

        public Task<ObjectMetadataRoot> LoadRootAsync(ObjectMetadataContext context, CancellationToken cancellationToken = default)
        {
            var database = Identity(1, PostgresObjectClass.Database, "regression");
            var schema = new ObjectMetadataDescriptor(Identity(2, PostgresObjectClass.Schema, "public", 1),
                "public", "public", "public", "public", MetadataSystemClassification.User, true);
            return Task.FromResult(new ObjectMetadataRoot(database, "regression", "180000", [schema], DateTimeOffset.UtcNow));
        }

        public Task<ObjectMetadataBatch> LoadChildrenAsync(ObjectMetadataContext context, PostgresObjectIdentity parent, CancellationToken cancellationToken = default)
        {
            if (parent.ObjectClass == PostgresObjectClass.Schema)
            {
                SchemaLoads++;
                return Task.FromResult(new ObjectMetadataBatch(parent,
                [
                    new(Identity(10, PostgresObjectClass.Table, "table", 2), "table", "public", "table",
                        "public.table", MetadataSystemClassification.User, true),
                ], DateTimeOffset.UtcNow));
            }
            TableLoads++;
            var name = Version == 1 ? "old_column" : "new_column";
            var columnIdentity = Identity(10, PostgresObjectClass.Column, name, 2).WithSubObject(1);
            return Task.FromResult(new ObjectMetadataBatch(parent,
            [
                new(columnIdentity, name, "table", name, $"table.{name}",
                    MetadataSystemClassification.User, false, Ordinal: 1),
            ], DateTimeOffset.UtcNow));
        }
    }

    private sealed class FailingChildProvider : IPostgresObjectMetadataProvider
    {
        public bool Fail { get; set; } = true;
        public Task<ObjectMetadataRoot> LoadRootAsync(ObjectMetadataContext context, CancellationToken cancellationToken = default)
        {
            var database = Identity(1, PostgresObjectClass.Database, "regression");
            var schema = new ObjectMetadataDescriptor(Identity(2, PostgresObjectClass.Schema, "public", 1),
                "public", "public", "public", "public", MetadataSystemClassification.User, true);
            return Task.FromResult(new ObjectMetadataRoot(database, "regression", "180000", [schema], DateTimeOffset.UtcNow));
        }
        public Task<ObjectMetadataBatch> LoadChildrenAsync(ObjectMetadataContext context, PostgresObjectIdentity parent, CancellationToken cancellationToken = default)
        {
            if (Fail) throw new FakeMetadataDbException();
            return Task.FromResult(new ObjectMetadataBatch(parent, [], DateTimeOffset.UtcNow));
        }
    }

    private sealed class FakeMetadataDbException() : System.Data.Common.DbException("private provider detail")
    {
        public override string SqlState => "42501";
    }
}

file static class ObjectIdentityTestExtensions
{
    public static PostgresObjectIdentity WithSubObject(this PostgresObjectIdentity identity, int subObject) => new()
    {
        ConnectionProfileId = identity.ConnectionProfileId,
        ConfigurationIdentity = identity.ConfigurationIdentity,
        ServerFingerprint = identity.ServerFingerprint,
        DatabaseOid = identity.DatabaseOid,
        ObjectOid = identity.ObjectOid,
        ObjectClass = identity.ObjectClass,
        ParentOid = identity.ParentOid,
        SchemaOid = identity.SchemaOid,
        SubObjectNumber = subObject,
        NameSnapshot = identity.NameSnapshot,
    };
}
