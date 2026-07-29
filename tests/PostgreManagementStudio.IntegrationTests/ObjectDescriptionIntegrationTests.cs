using PostgreManagementStudio.Application;
using PostgreManagementStudio.Postgres;

namespace PostgreManagementStudio.IntegrationTests;

[Collection(ResourceStabilityCollection.Name)]
public sealed class ObjectDescriptionIntegrationTests
{
    [SeededPostgreSqlFact]
    public async Task RelationDescriptionMatchesCatalogueTruthAndOrdinalOrder()
    {
        var service = Service();
        var reference = new EditorObjectReference(
            "\"PMS Regression\".\"Type Matrix\"",
            ["PMS Regression", "Type Matrix"]);
        var candidates = await service.ResolveAsync(ConnectionString(), Database(), reference);
        var candidate = Assert.Single(candidates);

        var description = await service.LoadAsync(
            ConnectionString(), Database(), candidate, "generated_value");

        Assert.Equal("Table", candidate.ObjectType);
        Assert.Equal(Enumerable.Range(1, 16), description.Columns.Select(column => column.Ordinal));
        Assert.Equal("integer", description.Columns[0].DataType);
        Assert.Equal("ALWAYS", description.Columns[0].IdentityMode);
        Assert.True(description.Columns[0].IsPrimaryKey);
        Assert.True(description.Columns.Single(column => column.Name == "unicode_text").IsUnique);
        Assert.Contains("id * 2",
            description.Columns.Single(column => column.Name == "generated_value").GeneratedExpression);
        Assert.DoesNotContain(description.Columns, column => column.Ordinal <= 0);
        Assert.Equal("generated_value", description.TargetColumn);
    }

    [SeededPostgreSqlFact]
    public async Task ForeignKeyAndIndexParticipationAreReturnedWithoutReadingTableData()
    {
        var service = Service();
        var candidates = await service.ResolveAsync(ConnectionString(), Database(),
            new("\"PMS Regression\".\"Child Table\"", ["PMS Regression", "Child Table"]));
        var description = await service.LoadAsync(
            ConnectionString(), Database(), Assert.Single(candidates), "parent_id");
        var parentId = description.Columns.Single(column => column.Name == "parent_id");

        Assert.True(parentId.IsForeignKey);
        Assert.Contains("\"PMS Regression\".\"Parent Table\".id", parentId.ForeignKeyReference);
        Assert.Contains("Index With Space", parentId.Indexes!);
        Assert.Empty(description.DetailsText);
        var secondary = await service.LoadSecondaryAsync(
            ConnectionString(), Database(), description.Candidate);
        Assert.Contains("FOREIGN KEY", secondary.DetailsText);
    }

    [SeededPostgreSqlFact]
    public async Task ViewsTypesSequencesAndRoutinesResolveToUsefulDefinitions()
    {
        var service = Service();

        async Task<ObjectDescription> Describe(string name, params string[] parts)
        {
            var candidates = await service.ResolveAsync(
                ConnectionString(), Database(), new(name, parts));
            return await service.LoadAsync(
                ConnectionString(), Database(), Assert.Single(candidates), null);
        }

        var view = await Describe(
            "\"PMS Regression\".\"Order\"", "PMS Regression", "Order");
        Assert.Contains("SELECT", view.Definition!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(["id", "unicode_text"], view.Columns.Select(column => column.Name));

        var sequence = await Describe(
            "\"PMS Regression\".\"Mixed Case Sequence\"",
            "PMS Regression", "Mixed Case Sequence");
        Assert.Contains("CREATE SEQUENCE", sequence.Definition!);

        var type = await Describe(
            "\"PMS Regression\".\"Status Type\"", "PMS Regression", "Status Type");
        Assert.Contains("AS ENUM", type.Definition!);
        Assert.Contains("'new'", type.Definition!);

        var routineReference = new EditorObjectReference(
            "\"PMS Regression\".\"Function With Space\"(integer)",
            ["PMS Regression", "Function With Space"], RoutineSignature: "integer");
        var routines = await service.ResolveAsync(ConnectionString(), Database(), routineReference);
        var routine = await service.LoadAsync(
            ConnectionString(), Database(), Assert.Single(routines), null);
        Assert.Equal("integer", routine.Candidate.Signature);
        Assert.Contains("IMMUTABLE", routine.Definition!);
    }

    [SeededPostgreSqlFact]
    public async Task SearchPathVisibilityIsExposedInsteadOfChoosingArbitrarily()
    {
        var service = Service();
        var candidates = await service.ResolveAsync(ConnectionString(), Database(),
            new("pg_type", ["pg_type"]));

        Assert.Contains(candidates, candidate =>
            candidate.QualifiedName == "\"pg_catalog\".\"pg_type\"" && candidate.IsVisible);
    }

    private static ObjectDescriptionService Service() =>
        new(new NpgsqlObjectDescriptionMetadataProvider(NpgsqlConnectionFactory.Shared));
    private static string ConnectionString() =>
        Environment.GetEnvironmentVariable("PMS_CONNECTION_STRING")!;
    private static string Database() =>
        Environment.GetEnvironmentVariable("PMS_TEST_DATABASE")!;
}
