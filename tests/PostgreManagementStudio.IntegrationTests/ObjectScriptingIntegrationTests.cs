using Npgsql;
using PostgreManagementStudio.Application;
using PostgreManagementStudio.Core;
using PostgreManagementStudio.Postgres;

namespace PostgreManagementStudio.IntegrationTests;

[Collection(ResourceStabilityCollection.Name)]
public sealed class ObjectScriptingIntegrationTests
{
    [SeededPostgreSqlFact]
    public async Task SeededTableProducesExecutableCreateAndSafeTemplates()
    {
        var connectionString = Environment.GetEnvironmentVariable("PMS_CONNECTION_STRING")!;
        var database = Environment.GetEnvironmentVariable("PMS_TEST_DATABASE")!;
        var context = new ObjectMetadataContext
        {
            ConnectionProfileId = "integration", ConfigurationIdentity = "integration",
            ConnectionString = connectionString, Database = database,
        };
        var metadataProvider = new NpgsqlMetadataProvider(NpgsqlConnectionFactory.Shared);
        var root = await metadataProvider.LoadRootAsync(context);
        var schema = root.Schemas.Single(x => x.Name == "PMS Regression");
        var schemaObjects = await metadataProvider.LoadChildrenAsync(context, schema.Identity);
        var identity = schemaObjects.Objects.Single(x => x.Name == "Type Matrix").Identity;
        var service = new ObjectScriptService(new NpgsqlObjectScriptMetadataProvider(NpgsqlConnectionFactory.Shared));
        var create = await service.GenerateAsync(connectionString, database, identity, ObjectScriptKind.Create);
        var select = await service.GenerateAsync(connectionString, database, identity, ObjectScriptKind.Select);
        var insert = await service.GenerateAsync(connectionString, database, identity, ObjectScriptKind.Insert);
        var update = await service.GenerateAsync(connectionString, database, identity, ObjectScriptKind.Update);
        var delete = await service.GenerateAsync(connectionString, database, identity, ObjectScriptKind.Delete);

        Assert.Contains("CREATE TABLE \"PMS Regression\".\"Type Matrix\"", create);
        Assert.Contains("GENERATED ALWAYS AS", create);
        Assert.Contains("numeric_value", select);
        Assert.DoesNotContain("SELECT *", select);
        Assert.DoesNotContain("generated_value", insert);
        Assert.Contains("WHERE", update);
        Assert.Contains("WHERE", delete);
    }

    [SeededPostgreSqlFact]
    public async Task SupportedCatalogueObjectsProduceFidelityScriptsAndSequenceRoundTrips()
    {
        var connectionString = Environment.GetEnvironmentVariable("PMS_CONNECTION_STRING")!;
        var database = Environment.GetEnvironmentVariable("PMS_TEST_DATABASE")!;
        await CreateForeignTableFixtureAsync(connectionString, database);
        try
        {
        var context = new ObjectMetadataContext
        {
            ConnectionProfileId = "integration", ConfigurationIdentity = "integration",
            ConnectionString = connectionString, Database = database,
        };
        var provider = new NpgsqlMetadataProvider(NpgsqlConnectionFactory.Shared);
        var root = await provider.LoadRootAsync(context);
        var schema = root.Schemas.Single(x => x.Name == "PMS Regression");
        var batch = await provider.LoadChildrenAsync(context, schema.Identity);
        var scripts = new ObjectScriptService(new NpgsqlObjectScriptMetadataProvider(NpgsqlConnectionFactory.Shared));

        async Task<string> Create(string name) => await scripts.GenerateAsync(
            connectionString, database, batch.Objects.Single(x => x.Name == name).Identity, ObjectScriptKind.Create);

        Assert.Contains("CREATE OR REPLACE VIEW", await Create("Order"));
        Assert.Contains("CREATE MATERIALIZED VIEW", await Create("Materialized Résumé"));
        Assert.Contains("WITH NO DATA", await Create("Materialized Empty"));
        Assert.Contains("FUNCTION \"PMS Regression\".\"Function With Space\"", await Create("Function With Space"));
        Assert.Contains("PROCEDURE \"PMS Regression\".\"Procedure With Space\"", await Create("Procedure With Space"));
        Assert.Contains("AS ENUM", await Create("Status Type"));
        Assert.Contains("PARTITION BY RANGE", await Create("Partitioned Table"));
        Assert.Contains("PARTITION OF", await Create("Partition 2024"));
        var inherited = await Create("Inherited Child");
        Assert.Contains("INHERITS (\"PMS Regression\".\"Inherited Parent\")", inherited);
        Assert.DoesNotContain("\"inherited_id\" integer", inherited);
        var foreign = await Create("Foreign Table");
        Assert.Contains("SERVER \"pms_test_server\"", foreign);
        Assert.Contains("\"column_name\" 'remote_id'", foreign);
        Assert.Contains("\"table_name\" 'items'", foreign);

        var sequence = batch.Objects.Single(x => x.Name == "Mixed Case Sequence").Identity;
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        async Task RoundTrip(PostgresObjectIdentity identity)
        {
            var sql = await scripts.GenerateAsync(
                connectionString, database, identity, ObjectScriptKind.DropAndCreate);
            await using var transaction = await connection.BeginTransactionAsync();
            await using (var command = new NpgsqlCommand(sql, connection, transaction))
                await command.ExecuteNonQueryAsync();
            await transaction.RollbackAsync();
        }
        await RoundTrip(sequence);
        await RoundTrip(batch.Objects.Single(x => x.Name == "Partition 2024").Identity);
        await RoundTrip(batch.Objects.Single(x => x.Name == "Inherited Child").Identity);
        await RoundTrip(batch.Objects.Single(x => x.Name == "Foreign Table").Identity);

        var table = batch.Objects.Single(x => x.Name == "Child Table").Identity;
        var children = await provider.LoadChildrenAsync(context, table);
        Assert.Contains(children.Objects, x => x.Identity.ObjectClass == PostgresObjectClass.Constraint);
        Assert.Contains(children.Objects, x => x.Identity.ObjectClass == PostgresObjectClass.Index);
        }
        finally
        {
            await DropForeignTableFixtureAsync(database);
        }
    }

    [SeededPostgreSqlFact]
    public async Task StaleDatabaseIdentityIsRejectedBeforeCatalogueLookup()
    {
        var connectionString = Environment.GetEnvironmentVariable("PMS_CONNECTION_STRING")!;
        var database = Environment.GetEnvironmentVariable("PMS_TEST_DATABASE")!;
        var identity = new PostgresObjectIdentity
        {
            ConnectionProfileId = "stale", ConfigurationIdentity = "stale",
            ServerFingerprint = "stale", DatabaseOid = uint.MaxValue, ObjectOid = 1,
            ObjectClass = PostgresObjectClass.Table, NameSnapshot = "stale",
        };
        var provider = new NpgsqlObjectScriptMetadataProvider(NpgsqlConnectionFactory.Shared);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.LoadAsync(connectionString, database, identity));
        Assert.Contains("another server or database", error.Message);
    }

    private static async Task CreateForeignTableFixtureAsync(string connectionString, string database)
    {
        var admin = new NpgsqlConnectionStringBuilder(
            Environment.GetEnvironmentVariable("PMS_ADMIN_CONNECTION_STRING")!)
        {
            Database = database,
        };
        var role = new NpgsqlConnectionStringBuilder(connectionString).Username
            ?? throw new InvalidOperationException("The integration role is unavailable.");
        await using var connection = new NpgsqlConnection(admin.ConnectionString);
        await connection.OpenAsync();
        var sql = $"""
            CREATE FOREIGN DATA WRAPPER pms_test_dummy NO HANDLER;
            CREATE SERVER pms_test_server FOREIGN DATA WRAPPER pms_test_dummy;
            GRANT USAGE ON FOREIGN SERVER pms_test_server TO {PostgreSqlIdentifierQuoter.Quote(role)};
            CREATE FOREIGN TABLE "PMS Regression"."Foreign Table" (
                id integer OPTIONS (column_name 'remote_id'),
                note text
            ) SERVER pms_test_server OPTIONS (schema_name 'remote', table_name 'items');
            ALTER FOREIGN TABLE "PMS Regression"."Foreign Table"
                OWNER TO {PostgreSqlIdentifierQuoter.Quote(role)};
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task DropForeignTableFixtureAsync(string database)
    {
        var admin = new NpgsqlConnectionStringBuilder(
            Environment.GetEnvironmentVariable("PMS_ADMIN_CONNECTION_STRING")!)
        {
            Database = database,
        };
        await using var connection = new NpgsqlConnection(admin.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            DROP FOREIGN TABLE IF EXISTS "PMS Regression"."Foreign Table";
            DROP SERVER IF EXISTS pms_test_server CASCADE;
            DROP FOREIGN DATA WRAPPER IF EXISTS pms_test_dummy CASCADE;
            """, connection);
        await command.ExecuteNonQueryAsync();
    }
}
