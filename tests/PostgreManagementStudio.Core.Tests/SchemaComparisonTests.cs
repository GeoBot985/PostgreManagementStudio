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
    [Fact]
    public void PreviewExcludesDestructiveChangesAndWrapsIncludedSql()
    { var source = new SchemaModel("source", "db", 18, new[] { Table("new", "CREATE TABLE new(id int)") }, Array.Empty<string>()); var target = new SchemaModel("target", "db", 18, new[] { Table("old", "CREATE TABLE old(id bigint)") }, Array.Empty<string>()); var preview = SchemaSynchronisationPreviewBuilder.Build(SchemaComparisonService.Compare(source, target), Array.Empty<SchemaDependency>()); Assert.Contains(preview.Items, x => x.Difference.Kind == SchemaDifferenceKind.Added && x.Included); Assert.Contains(preview.Items, x => x.Difference.Kind == SchemaDifferenceKind.Removed && !x.Included); Assert.Contains("BEGIN;", preview.Script); Assert.Contains("COMMIT;", preview.Script); Assert.DoesNotContain("DROP TABLE", preview.Script); }

    [Fact]
    public void PreviewHonoursExclusionsAndDependencyOrder()
    { var source = new SchemaModel("s", "db", 18, new[] { Table("a", "CREATE TABLE a(id int)"), Table("b", "CREATE TABLE b(id int)") }, Array.Empty<string>()); var comparison = SchemaComparisonService.Compare(source, new SchemaModel("t", "db", 18, Array.Empty<SchemaObject>(), Array.Empty<string>())); var preview = SchemaSynchronisationPreviewBuilder.Build(comparison, new[] { new SchemaDependency("public.b:Table", "public.a:Table") }, new HashSet<string> { "public.b:Table" }); Assert.Single(preview.Filter(SchemaPreviewFilter.Selected)); Assert.Equal("a", preview.IncludedSteps[0].Difference.Source!.Name); Assert.DoesNotContain("CREATE TABLE b", preview.Script); }

    [Fact]
    public void GeneratorQuotesIdentifiers()
    { var obj = new SchemaObject("public.Order Items:Table", SchemaObjectKind.Table, "public", "Order Items", null, "CREATE TABLE \"public\".\"Order Items\" (id int)", new Dictionary<string, string>()); var comparison = SchemaComparisonService.Compare(new SchemaModel("s", "db", 18, new[] { obj }, Array.Empty<string>()), new SchemaModel("t", "db", 18, Array.Empty<SchemaObject>(), Array.Empty<string>())); var script = SchemaScriptGenerator.Generate(comparison, SchemaSynchronisationPlanner.Plan(comparison, Array.Empty<SchemaDependency>())); Assert.Contains("\"Order Items\"", script); Assert.DoesNotContain("password", script, StringComparison.OrdinalIgnoreCase); }
}
