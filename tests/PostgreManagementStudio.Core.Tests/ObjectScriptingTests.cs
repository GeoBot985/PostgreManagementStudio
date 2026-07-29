using PostgreManagementStudio.Application;
using PostgreManagementStudio.Core;

namespace PostgreManagementStudio.Core.Tests;

public sealed class ObjectScriptingTests
{
    [Fact]
    public async Task SelectUsesOrderedExplicitColumnsAndLimit()
    {
        var service = new ObjectScriptService(new StubProvider(Metadata()));
        var sql = await service.GenerateAsync("Host=localhost", "db", Identity(), ObjectScriptKind.Select);
        Assert.Contains("SELECT\n    \"id\",\n    \"Order\"", sql);
        Assert.DoesNotContain("*", sql);
        Assert.EndsWith("LIMIT 1000;", sql);
    }

    [Fact]
    public async Task InsertOmitsDefaultsGeneratedAndIdentityAlways()
    {
        var service = new ObjectScriptService(new StubProvider(Metadata()));
        var sql = await service.GenerateAsync("Host=localhost", "db", Identity(), ObjectScriptKind.Insert);
        Assert.Contains("required", sql);
        Assert.DoesNotContain("generated", sql);
        Assert.DoesNotContain("identity", sql);
        Assert.DoesNotContain("optional", sql);
    }

    [Fact]
    public async Task UpdateAndDeleteAlwaysHaveSafePrimaryKeyWhere()
    {
        var service = new ObjectScriptService(new StubProvider(Metadata()));
        var update = await service.GenerateAsync("Host=localhost", "db", Identity(), ObjectScriptKind.Update);
        var delete = await service.GenerateAsync("Host=localhost", "db", Identity(), ObjectScriptKind.Delete);
        Assert.Contains("WHERE\n    \"id\" = <id>;", update);
        Assert.Contains("WHERE\n    \"id\" = <id>;", delete);
    }

    [Fact]
    public async Task DropIsQualifiedTerminatedAndNeverCascades()
    {
        var service = new ObjectScriptService(new StubProvider(Metadata()));
        var sql = await service.GenerateAsync("Host=localhost", "db", Identity(), ObjectScriptKind.Drop);
        Assert.Equal("DROP TABLE public.\"Order Items\";", sql);
        Assert.DoesNotContain("CASCADE", sql);
    }

    [Fact]
    public void CommandAvailabilityMatchesObjectType()
    {
        Assert.True(ObjectScriptService.Supports(PostgresObjectClass.Table, ObjectScriptKind.Select));
        Assert.True(ObjectScriptService.Supports(PostgresObjectClass.Function, ObjectScriptKind.Create));
        Assert.False(ObjectScriptService.Supports(PostgresObjectClass.Function, ObjectScriptKind.Update));
        Assert.False(ObjectScriptService.Supports(PostgresObjectClass.Column, ObjectScriptKind.Drop));
        Assert.True(ObjectScriptService.Supports(PostgresObjectClass.ForeignTable, ObjectScriptKind.Create));
        Assert.True(ObjectScriptService.Supports(PostgresObjectClass.ForeignTable, ObjectScriptKind.Drop));
        Assert.False(ObjectScriptService.Supports(PostgresObjectClass.Aggregate, ObjectScriptKind.Create));
    }

    private static ObjectScriptMetadata Metadata() => new(Identity(), "public", "Order Items",
        "public.\"Order Items\"", "TABLE", "CREATE TABLE public.\"Order Items\" ();",
        [
            new("id", "integer", false, null, "", null, 1, true),
            new("Order", "text", false, null, "", null, 2, false),
            new("required", "text", false, null, "", null, 3, false),
            new("optional", "text", true, "'x'", "", null, 4, false),
            new("identity", "bigint", false, null, "a", null, 5, false),
            new("generated", "integer", true, null, "", "id + 1", 6, false),
        ], [], [], []);

    private static PostgresObjectIdentity Identity() => new()
    {
        ConnectionProfileId = "test", ConfigurationIdentity = "config", ServerFingerprint = "server",
        DatabaseOid = 1, ObjectOid = 2, ObjectClass = PostgresObjectClass.Table, NameSnapshot = "Order Items",
    };

    private sealed class StubProvider(ObjectScriptMetadata value) : IObjectScriptMetadataProvider
    {
        public Task<ObjectScriptMetadata> LoadAsync(string connectionString, string database,
            PostgresObjectIdentity identity, CancellationToken cancellationToken = default) => Task.FromResult(value);
    }
}
