using PostgreManagementStudio.Core;

namespace PostgreManagementStudio.Application;

public enum ObjectExplorerNodeKind
{
    Database,
    Schema,
    Tables,
    Views,
    MaterializedViews,
    Sequences,
    Functions,
    Procedures,
    Table,
    View,
    MaterializedView,
    Sequence,
    Function,
    Procedure,
}

public sealed record ObjectExplorerNode(
    ObjectExplorerNodeKind Kind,
    string Name,
    string? QualifiedName,
    IReadOnlyList<ObjectExplorerNode> Children);

public sealed class ObjectExplorerService(IPostgresMetadataProvider metadataProvider)
{
    public async Task<ObjectExplorerNode> LoadDatabaseAsync(
        string connectionString,
        string database,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(database);
        var snapshot = await metadataProvider.LoadAsync(connectionString, database, cancellationToken);
        var schemas = snapshot.Schemas
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Select(schema => BuildSchema(snapshot, schema))
            .ToArray();
        return new(ObjectExplorerNodeKind.Database, snapshot.Database, snapshot.Database, schemas);
    }

    private static ObjectExplorerNode BuildSchema(DatabaseMetadataSnapshot snapshot, string schema)
    {
        var relations = snapshot.Relations.Where(x => x.SchemaName == schema).ToArray();
        var routines = snapshot.Routines.Where(x => x.SchemaName == schema).ToArray();
        var groups = new[]
        {
            Group(ObjectExplorerNodeKind.Tables, "Tables", relations.Where(x => x.Kind == CompletionKind.Table), ObjectExplorerNodeKind.Table),
            Group(ObjectExplorerNodeKind.Views, "Views", relations.Where(x => x.Kind == CompletionKind.View), ObjectExplorerNodeKind.View),
            Group(ObjectExplorerNodeKind.MaterializedViews, "Materialized Views", relations.Where(x => x.Kind == CompletionKind.MaterializedView), ObjectExplorerNodeKind.MaterializedView),
            Group(ObjectExplorerNodeKind.Sequences, "Sequences", relations.Where(x => x.Kind == CompletionKind.Sequence), ObjectExplorerNodeKind.Sequence),
            RoutineGroup(ObjectExplorerNodeKind.Functions, "Functions", routines.Where(x => x.Kind == CompletionKind.Function), ObjectExplorerNodeKind.Function),
            RoutineGroup(ObjectExplorerNodeKind.Procedures, "Procedures", routines.Where(x => x.Kind == CompletionKind.Procedure), ObjectExplorerNodeKind.Procedure),
        };
        return new(ObjectExplorerNodeKind.Schema, schema, PostgreSqlIdentifierQuoter.Quote(schema), groups);
    }

    private static ObjectExplorerNode Group(ObjectExplorerNodeKind groupKind, string name, IEnumerable<RelationMetadata> items, ObjectExplorerNodeKind itemKind) =>
        new(groupKind, name, null, items
            .GroupBy(x => (x.SchemaName, x.Name, x.Kind))
            .Select(x => x.First())
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Select(x => new ObjectExplorerNode(itemKind, x.Name, PostgreSqlIdentifierQuoter.Qualified(x.SchemaName, x.Name), []))
            .ToArray());

    private static ObjectExplorerNode RoutineGroup(ObjectExplorerNodeKind groupKind, string name, IEnumerable<RoutineMetadata> items, ObjectExplorerNodeKind itemKind) =>
        new(groupKind, name, null, items
            .GroupBy(x => (x.SchemaName, x.Name, x.Signature, x.Kind))
            .Select(x => x.First())
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Select(x => new ObjectExplorerNode(itemKind, x.Name, PostgreSqlIdentifierQuoter.Qualified(x.SchemaName, x.Name), []))
            .ToArray());
}
