using Npgsql;
using PostgreManagementStudio.Application;
using PostgreManagementStudio.Core;
using PostgreManagementStudio.Postgres;

namespace PostgreManagementStudio.IntegrationTests;

public sealed class MetadataHardeningIntegrationTests
{
    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("PMS_CONNECTION_STRING") ??
        throw new InvalidOperationException("PMS_CONNECTION_STRING is required.");

    [SeededPostgreSqlFact]
    public async Task LazyMetadataClassifiesPartitionsRoutinesColumnsAndSystemSchemas()
    {
        var database = Environment.GetEnvironmentVariable("PMS_TEST_DATABASE")!;
        var provider = new NpgsqlMetadataProvider();
        var visibleContext = Context(ConnectionString, database, false);
        var visibleRoot = await provider.LoadRootAsync(visibleContext);
        Assert.DoesNotContain(visibleRoot.Schemas, x => x.Name == "pg_catalog");
        var schema = Assert.Single(visibleRoot.Schemas, x => x.Name == "PMS Regression");

        var children = await provider.LoadChildrenAsync(visibleContext, schema.Identity);
        Assert.Contains(children.Objects, x => x.Identity.ObjectClass == PostgresObjectClass.PartitionedTable);
        Assert.Contains(children.Objects, x => x.Identity.ObjectClass == PostgresObjectClass.Partition);
        Assert.Contains(children.Objects, x => x.Identity.ObjectClass == PostgresObjectClass.MaterializedView);
        Assert.Contains(children.Objects, x => x.Identity.ObjectClass == PostgresObjectClass.Function
            && x.RoutineSignature!.Contains("integer", StringComparison.Ordinal));
        Assert.Contains(children.Objects, x => x.Identity.ObjectClass == PostgresObjectClass.Procedure);

        var table = Assert.Single(children.Objects, x => x.Name == "Type Matrix");
        var search = await new NpgsqlObjectSearchService().SearchAsync(
            ConnectionString, new ObjectSearchOptions("Type Matrix"));
        var searchResult = Assert.Single(search.Results, x => x.Schema == "PMS Regression" && x.ObjectName == "Type Matrix");
        Assert.Equal(table.Identity, searchResult.Identity);
        var columns = await provider.LoadChildrenAsync(visibleContext, table.Identity);
        Assert.NotEmpty(columns.Objects);
        Assert.Equal(columns.Objects.Select(x => x.Ordinal).Order(), columns.Objects.Select(x => x.Ordinal));
        Assert.All(columns.Objects, x =>
        {
            Assert.Equal(PostgresObjectClass.Column, x.Identity.ObjectClass);
            Assert.Equal(table.Identity.ObjectOid, x.Identity.ObjectOid);
            Assert.NotNull(x.Identity.SubObjectNumber);
        });

        var allRoot = await provider.LoadRootAsync(visibleContext with { ShowSystemObjects = true });
        Assert.Contains(allRoot.Schemas, x => x.Name == "pg_catalog"
            && x.SystemClassification == MetadataSystemClassification.Catalog);
        Assert.Contains(allRoot.Schemas, x => x.Name == "information_schema"
            && x.SystemClassification == MetadataSystemClassification.InformationSchema);
    }

    [SeededPostgreSqlFact]
    public async Task RenameAndDropRecreateUseOidIdentityAndRoutineOverloadsRemainDistinct()
    {
        var database = Environment.GetEnvironmentVariable("PMS_TEST_DATABASE")!;
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var schemaName = "s38_" + suffix;
        var provider = new NpgsqlMetadataProvider();
        var context = Context(ConnectionString, database, false);
        await using var owner = NpgsqlConnectionFactory.Shared.Create(ConnectionString, "PostgreManagementStudio - Metadata Mutation");
        await owner.OpenAsync();
        try
        {
            await new NpgsqlCommand($"""
                CREATE SCHEMA "{schemaName}";
                CREATE TABLE "{schemaName}"."Before Name"(id integer);
                CREATE FUNCTION "{schemaName}".calculate(integer) RETURNS integer LANGUAGE sql AS $$ SELECT $1 $$;
                CREATE FUNCTION "{schemaName}".calculate(numeric) RETURNS numeric LANGUAGE sql AS $$ SELECT $1 $$;
                CREATE PROCEDURE "{schemaName}".calculate(text) LANGUAGE plpgsql AS $$ BEGIN NULL; END $$;
                CREATE AGGREGATE "{schemaName}".total(integer) (SFUNC = int4pl, STYPE = integer, INITCOND = '0');
                """, owner).ExecuteNonQueryAsync();

            var root = await provider.LoadRootAsync(context);
            var schema = Assert.Single(root.Schemas, x => x.Name == schemaName);
            var initial = await provider.LoadChildrenAsync(context, schema.Identity);
            var before = Assert.Single(initial.Objects, x => x.Name == "Before Name");
            var routines = initial.Objects.Where(x => x.Name == "calculate").ToArray();
            Assert.Equal(3, routines.Length);
            Assert.Equal(3, routines.Select(x => (x.Identity.ObjectClass, x.RoutineSignature)).Distinct().Count());
            Assert.Contains(initial.Objects, x => x.Name == "total"
                && x.Identity.ObjectClass == PostgresObjectClass.Aggregate);

            await new NpgsqlCommand($"""ALTER TABLE "{schemaName}"."Before Name" RENAME TO "After Name";""", owner)
                .ExecuteNonQueryAsync();
            var renamedBatch = await provider.LoadChildrenAsync(context, schema.Identity);
            var renamed = Assert.Single(renamedBatch.Objects, x => x.Name == "After Name");
            Assert.Equal(before.Identity, renamed.Identity);

            await new NpgsqlCommand($"""
                DROP TABLE "{schemaName}"."After Name";
                CREATE TABLE "{schemaName}"."After Name"(id integer);
                """, owner).ExecuteNonQueryAsync();
            var recreatedBatch = await provider.LoadChildrenAsync(context, schema.Identity);
            var recreated = Assert.Single(recreatedBatch.Objects, x => x.Name == "After Name");
            Assert.NotEqual(renamed.Identity, recreated.Identity);
        }
        finally
        {
            await new NpgsqlCommand($"""DROP SCHEMA IF EXISTS "{schemaName}" CASCADE;""", owner).ExecuteNonQueryAsync();
        }
    }

    [SeededPostgreSqlFact]
    public async Task RestrictedRoleSeesOnlyPermittedSchemasAndReadOnlyRoleCanBrowseGrantedRelations()
    {
        var database = Environment.GetEnvironmentVariable("PMS_TEST_DATABASE")!;
        var readOnlyConnection = Environment.GetEnvironmentVariable("PMS_TEST_READONLY_CONNECTION_STRING")!;
        var restrictedConnection = Environment.GetEnvironmentVariable("PMS_TEST_RESTRICTED_CONNECTION_STRING")!;
        var provider = new NpgsqlMetadataProvider();

        var readOnlyContext = Context(readOnlyConnection, database, false);
        var readOnlyRoot = await provider.LoadRootAsync(readOnlyContext);
        var schema = Assert.Single(readOnlyRoot.Schemas, x => x.Name == "PMS Regression");
        var visible = await provider.LoadChildrenAsync(readOnlyContext, schema.Identity);
        Assert.Contains(visible.Objects, x => x.Name == "Type Matrix");

        var restrictedRoot = await provider.LoadRootAsync(Context(restrictedConnection, database, false));
        Assert.DoesNotContain(restrictedRoot.Schemas, x => x.Name == "PMS Regression");
    }

    [SeededPostgreSqlFact]
    public async Task LargeSchemaLoadsAsOneBoundedDeterministicBatch()
    {
        var database = Environment.GetEnvironmentVariable("PMS_TEST_DATABASE")!;
        var schemaName = "s38_large_" + Guid.NewGuid().ToString("N")[..8];
        await using var owner = NpgsqlConnectionFactory.Shared.Create(ConnectionString, "PostgreManagementStudio - Large Metadata");
        await owner.OpenAsync();
        try
        {
            await new NpgsqlCommand($"""
                CREATE SCHEMA "{schemaName}";
                DO $block$
                BEGIN
                    FOR item IN 1..500 LOOP
                        EXECUTE format('CREATE TABLE "{schemaName}".object_%s(id integer)', lpad(item::text, 4, '0'));
                    END LOOP;
                END
                $block$;
                """, owner).ExecuteNonQueryAsync();
            var provider = new NpgsqlMetadataProvider();
            var context = Context(ConnectionString, database, false);
            var root = await provider.LoadRootAsync(context);
            var schema = Assert.Single(root.Schemas, x => x.Name == schemaName);
            var started = System.Diagnostics.Stopwatch.StartNew();
            var batch = await provider.LoadChildrenAsync(context, schema.Identity);
            started.Stop();
            Assert.Equal(500, batch.Objects.Count);
            Assert.Equal(batch.Objects.Select(x => x.Name).Order(StringComparer.Ordinal),
                batch.Objects.Select(x => x.Name));
            Assert.True(started.Elapsed < TimeSpan.FromSeconds(10),
                $"Large schema metadata took {started.Elapsed}.");
        }
        finally
        {
            await new NpgsqlCommand($"""DROP SCHEMA IF EXISTS "{schemaName}" CASCADE;""", owner).ExecuteNonQueryAsync();
        }
    }

    [PostgreSqlFact]
    public async Task MissingDatabaseAndCancellationAreControlledAndDoNotPopulateCache()
    {
        var provider = new NpgsqlMetadataProvider();
        var cache = new BoundedMetadataCache();
        var service = new HardenedMetadataService(provider, cache);
        var missing = Context(ConnectionString, "s38_missing_" + Guid.NewGuid().ToString("N"), false);
        await using var controller = new MetadataRequestController();
        var failed = await service.LoadRootAsync(missing, controller);
        Assert.Equal(MetadataRequestState.Failed, failed.State);
        Assert.Equal(MetadataFailureCategory.DatabaseUnavailable, failed.Error!.Category);
        Assert.Equal(0, cache.Count);

        var badSearchBuilder = new NpgsqlConnectionStringBuilder(ConnectionString)
        {
            Database = missing.Database,
        };
        var search = await new NpgsqlObjectSearchService().SearchAsync(
            badSearchBuilder.ConnectionString, new ObjectSearchOptions("anything"));
        Assert.Empty(search.Results);
        Assert.Single(search.Warnings);
        Assert.DoesNotContain(badSearchBuilder.Password!, search.Warnings[0]);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelled = await service.LoadRootAsync(
            Context(ConnectionString, Environment.GetEnvironmentVariable("PMS_TEST_DATABASE")!, false),
            controller, cancellationToken: cancellation.Token);
        Assert.Equal(MetadataRequestState.Cancelled, cancelled.State);
        Assert.Equal(0, cache.Count);
    }

    private static ObjectMetadataContext Context(string connectionString, string database, bool showSystem) => new()
    {
        ConnectionProfileId = "environment:PMS_CONNECTION_STRING",
        ConfigurationIdentity = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(connectionString))),
        ConnectionString = connectionString,
        Database = database,
        ShowSystemObjects = showSystem,
    };
}
