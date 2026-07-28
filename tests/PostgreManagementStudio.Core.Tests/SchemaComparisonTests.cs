using PostgreManagementStudio.Application;

namespace PostgreManagementStudio.Core.Tests;

public sealed class SchemaComparisonTests
{
    private static SchemaObject Table(string name, string definition) => new($"public.{name}:Table", SchemaObjectKind.Table, "public", name, null, definition, new Dictionary<string, string>());

    [Fact]
    public void CanonicalizationIgnoresWhitespaceButPreservesMeaning()
    { Assert.Equal("CREATE TABLE public.orders ( id int );", SchemaCanonicalizer.Canonicalize("CREATE  TABLE public.orders (\r\n id int );")); Assert.NotEqual(SchemaCanonicalizer.Canonicalize("SELECT 'a b'"), SchemaCanonicalizer.Canonicalize("SELECT 'ab'")); }

    [Fact]
    public void ClassifiesAddedChangedRemovedAndRenameCandidates()
    { var source = new SchemaModel("s", "db", 18, new[] { Table("orders", "CREATE TABLE orders(id int)"), Table("new_table", "CREATE TABLE new_table(id int)") }, Array.Empty<string>()); var target = new SchemaModel("t", "db", 18, new[] { Table("orders", "CREATE TABLE orders(id bigint)"), Table("old_table", "CREATE TABLE old_table(id int)") }, Array.Empty<string>()); var result = SchemaComparisonService.Compare(source, target); Assert.Contains(result.Differences, x => x.Kind == SchemaDifferenceKind.Changed); Assert.Contains(result.Differences, x => x.Kind == SchemaDifferenceKind.RenameCandidate); }

    [Fact]
    public void PlannerOrdersDependenciesAndExcludesDestructiveByDefault()
    { var source = new SchemaModel("s", "db", 18, new[] { Table("a", "CREATE TABLE a(id int)"), Table("b", "CREATE TABLE b(a_id int)") }, Array.Empty<string>()); var target = new SchemaModel("t", "db", 18, Array.Empty<SchemaObject>(), Array.Empty<string>()); var comparison = SchemaComparisonService.Compare(source, target); var plan = SchemaSynchronisationPlanner.Plan(comparison, new[] { new SchemaDependency("public.b:Table", "public.a:Table") }); Assert.Equal(2, plan.Steps.Count); Assert.Equal("a", plan.Steps[0].Difference.Source!.Name); Assert.DoesNotContain("DROP", SchemaScriptGenerator.Generate(comparison, plan)); }

    [Fact]
    public void SnapshotsRoundTripWithoutCredentials()
    { var model = new SchemaModel("server", "db", 18, new[] { Table("x", "CREATE TABLE x(id int)") }, Array.Empty<string>()); var copy = SchemaSnapshotService.Deserialize(SchemaSnapshotService.Serialize(model)); Assert.Equal("db", copy.Database); Assert.DoesNotContain("password", SchemaSnapshotService.Serialize(copy), StringComparison.OrdinalIgnoreCase); }
}
