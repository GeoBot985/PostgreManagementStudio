using System.Data.Common;
using PostgreManagementStudio.Application;
using PostgreManagementStudio.Core;

namespace PostgreManagementStudio.Core.Tests;

public sealed class MetadataHardeningTests
{
    [Fact]
    public void StableIdentityIgnoresRenameButDistinguishesRecreatedObjectsSchemasAndColumns()
    {
        var before = Identity(10, "before", schema: 2);
        var renamed = Identity(10, "after", schema: 2);
        var recreated = Identity(11, "before", schema: 2);
        var otherSchema = Identity(10, "before", schema: 3);
        var column1 = Identity(10, "id", schema: 2, subObject: 1, objectClass: PostgresObjectClass.Column);
        var column2 = Identity(10, "id", schema: 2, subObject: 2, objectClass: PostgresObjectClass.Column);

        Assert.Equal(before, renamed);
        Assert.NotEqual(before, recreated);
        Assert.NotEqual(before, otherSchema);
        Assert.NotEqual(column1, column2);
        Assert.DoesNotContain("before", before.ToString());
    }

    [Fact]
    public async Task RequestControllerRejectsLateCompletionAndDisposeIsTerminal()
    {
        var controller = new MetadataRequestController();
        var firstCompletion = new TaskCompletionSource<(string Value, bool CacheHit)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var secondCompletion = new TaskCompletionSource<(string Value, bool CacheHit)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var first = controller.RunAsync(false, _ => firstCompletion.Task);
        var firstId = controller.RequestId;
        var second = controller.RunAsync(true, _ => secondCompletion.Task);
        Assert.NotEqual(firstId, controller.RequestId);
        secondCompletion.SetResult(("new", false));
        Assert.Equal("new", (await second).Value);
        firstCompletion.SetResult(("old", false));
        Assert.Equal(MetadataRequestState.Stale, (await first).State);
        Assert.Equal(MetadataRequestState.Completed, controller.State);

        await controller.DisposeAsync();
        Assert.Equal(MetadataRequestState.Disposed, controller.State);
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            controller.RunAsync(false, _ => Task.FromResult(("invalid", false))));
    }

    [Fact]
    public void CacheIsBoundedImmutableByKeyAndSupportsScopedInvalidation()
    {
        var cache = new BoundedMetadataCache(2, TimeSpan.FromMinutes(1));
        var a = Context("profile-a", "config-a", "db-a");
        var b = Context("profile-b", "config-b", "db-a");
        var keyA = Key(a);
        var keyB = Key(b);
        cache.Store(keyA, "a");
        cache.Store(keyB, "b");
        Assert.True(cache.TryGet<string>(keyA, out var value));
        Assert.Equal("a", value);
        cache.InvalidateProfile("profile-a");
        Assert.False(cache.TryGet<string>(keyA, out _));
        Assert.True(cache.TryGet<string>(keyB, out _));

        cache.Store(Key(Context("profile-c", "config-c", "db-c")), "c");
        cache.Store(Key(Context("profile-d", "config-d", "db-d")), "d");
        Assert.InRange(cache.Count, 1, 2);
    }

    [Fact]
    public async Task CancelledAndFailedLoadsDoNotPopulateCache()
    {
        var provider = new CountingProvider { Failure = new FakePostgresException("42501") };
        var cache = new BoundedMetadataCache();
        var service = new HardenedMetadataService(provider, cache);
        var context = Context("profile", "config", "database");
        await using var controller = new MetadataRequestController();
        var failed = await service.LoadRootAsync(context, controller);
        Assert.Equal(MetadataRequestState.Failed, failed.State);
        Assert.Equal(MetadataFailureCategory.PermissionDenied, failed.Error!.Category);
        Assert.Equal(0, cache.Count);

        provider.Failure = null;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelled = await service.LoadRootAsync(context, controller, cancellationToken: cancellation.Token);
        Assert.Equal(MetadataRequestState.Cancelled, cancelled.State);
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public async Task CancelledRequestDoesNotApplyAnExistingCacheHit()
    {
        var provider = new CountingProvider();
        var cache = new BoundedMetadataCache();
        var service = new HardenedMetadataService(provider, cache);
        var context = Context("profile", "config", "database");
        await using var controller = new MetadataRequestController();
        Assert.Equal(MetadataRequestState.Completed, (await service.LoadRootAsync(context, controller)).State);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelled = await service.LoadRootAsync(context, controller, cancellationToken: cancellation.Token);
        Assert.Equal(MetadataRequestState.Cancelled, cancelled.State);
        Assert.Null(cancelled.Value);
    }

    [Fact]
    public void FilteringClassificationAndSortingAreDeterministic()
    {
        Assert.Equal(MetadataSystemClassification.Catalog, ObjectMetadataRules.ClassifySchema("pg_catalog"));
        Assert.Equal(MetadataSystemClassification.InformationSchema, ObjectMetadataRules.ClassifySchema("information_schema"));
        Assert.Equal(MetadataSystemClassification.Temporary, ObjectMetadataRules.ClassifySchema("pg_temp_3"));
        Assert.Equal(MetadataSystemClassification.TemporaryToast, ObjectMetadataRules.ClassifySchema("pg_toast_temp_3"));
        Assert.Equal(MetadataSystemClassification.User, ObjectMetadataRules.ClassifySchema("pg_business"));

        var values = new[]
        {
            Descriptor(2, "alpha", MetadataSystemClassification.User),
            Descriptor(3, "Alpha", MetadataSystemClassification.User),
            Descriptor(1, "hidden", MetadataSystemClassification.Catalog),
        };
        var visible = ObjectMetadataRules.Filter(values, false);
        Assert.Equal(["Alpha", "alpha"], visible.Select(x => x.Name));
        Assert.Equal(3, ObjectMetadataRules.Filter(values, true).Count);
    }

    [Theory]
    [InlineData("42501", MetadataFailureCategory.PermissionDenied)]
    [InlineData("3D000", MetadataFailureCategory.DatabaseUnavailable)]
    [InlineData("42P01", MetadataFailureCategory.ObjectNotFound)]
    [InlineData("57P01", MetadataFailureCategory.ConnectionLost)]
    public void ProviderErrorsAreStructuredAndSecretFree(string sqlState, MetadataFailureCategory expected)
    {
        var result = MetadataFailureClassifier.Classify(new FakePostgresException(sqlState));
        Assert.Equal(expected, result.Category);
        Assert.DoesNotContain("super-secret", result.Message);
        Assert.Equal(sqlState, result.SqlState);
    }

    private static ObjectMetadataContext Context(string profile, string configuration, string database) => new()
    {
        ConnectionProfileId = profile,
        ConfigurationIdentity = configuration,
        ConnectionString = "Host=example;Password=super-secret",
        Database = database,
    };

    private static MetadataCacheKey Key(ObjectMetadataContext context) =>
        new(context.ConnectionProfileId, context.ConfigurationIdentity, context.Database, null,
            MetadataOperation.LoadRoot, context.ShowSystemObjects);

    private static PostgresObjectIdentity Identity(
        uint oid,
        string name,
        uint schema,
        int? subObject = null,
        PostgresObjectClass objectClass = PostgresObjectClass.Table) => new()
    {
        ConnectionProfileId = "profile",
        ConfigurationIdentity = "config",
        ServerFingerprint = "server",
        DatabaseOid = 1,
        ObjectOid = oid,
        ObjectClass = objectClass,
        SchemaOid = schema,
        ParentOid = schema,
        SubObjectNumber = subObject,
        NameSnapshot = name,
    };

    private static ObjectMetadataDescriptor Descriptor(
        uint oid,
        string name,
        MetadataSystemClassification classification) =>
        new(Identity(oid, name, 2), name, "public", name, $"public.{name}", classification, false);

    private sealed class CountingProvider : IPostgresObjectMetadataProvider
    {
        public Exception? Failure { get; set; }
        public Task<ObjectMetadataRoot> LoadRootAsync(ObjectMetadataContext context, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Failure is not null) throw Failure;
            return Task.FromResult(new ObjectMetadataRoot(
                Identity(1, "database", 0, objectClass: PostgresObjectClass.Database),
                context.Database, "180000", [], DateTimeOffset.UtcNow));
        }
        public Task<ObjectMetadataBatch> LoadChildrenAsync(ObjectMetadataContext context, PostgresObjectIdentity parent, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ObjectMetadataBatch(parent, [], DateTimeOffset.UtcNow));
    }

    private sealed class FakePostgresException(string sqlState) : DbException("provider detail super-secret")
    {
        public override string SqlState { get; } = sqlState;
    }
}
