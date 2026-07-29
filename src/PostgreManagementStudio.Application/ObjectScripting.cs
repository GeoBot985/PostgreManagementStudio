using PostgreManagementStudio.Core;

namespace PostgreManagementStudio.Application;

public enum ObjectScriptKind { Create, Drop, DropAndCreate, Select, Insert, Update, Delete }

public sealed record ScriptColumn(
    string Name, string DataType, bool IsNullable, string? DefaultExpression,
    string IdentityKind, string? GeneratedExpression, int Ordinal, bool IsPrimaryKey,
    string? Collation = null, string? Comment = null,
    IReadOnlyList<string>? ForeignOptions = null, bool IsLocal = true);

public sealed record ObjectScriptMetadata(
    PostgresObjectIdentity Identity,
    string Schema,
    string Name,
    string QualifiedName,
    string ObjectKeyword,
    string? CanonicalCreate,
    IReadOnlyList<ScriptColumn> Columns,
    IReadOnlyList<string> Constraints,
    IReadOnlyList<string> Indexes,
    IReadOnlyList<string> Triggers,
    string? Comment = null,
    string? CanonicalDrop = null,
    string? Owner = null,
    long? EstimatedRows = null,
    long? SizeBytes = null,
    string? Privileges = null);

public interface IObjectScriptMetadataProvider
{
    Task<ObjectScriptMetadata> LoadAsync(
        string connectionString, string database, PostgresObjectIdentity identity,
        CancellationToken cancellationToken = default);
}

public interface IObjectActionService
{
    bool CanRename(PostgresObjectClass objectClass);
    bool CanDelete(PostgresObjectClass objectClass);
    Task RenameAsync(string connectionString, string database, PostgresObjectIdentity identity,
        string newName, bool readOnly = false, CancellationToken cancellationToken = default);
    Task DeleteAsync(string connectionString, string database, PostgresObjectIdentity identity,
        bool readOnly = false, CancellationToken cancellationToken = default);
}

public sealed class ObjectScriptService(IObjectScriptMetadataProvider metadata)
{
    public static bool SupportsMetadata(PostgresObjectClass objectClass) =>
        objectClass is not PostgresObjectClass.Unknown
            and not PostgresObjectClass.Aggregate
            and not PostgresObjectClass.WindowFunction;

    public static bool Supports(PostgresObjectClass objectClass, ObjectScriptKind kind) => kind switch
    {
        ObjectScriptKind.Select => objectClass is PostgresObjectClass.Table or PostgresObjectClass.PartitionedTable
            or PostgresObjectClass.Partition or PostgresObjectClass.ForeignTable or PostgresObjectClass.View
            or PostgresObjectClass.MaterializedView or PostgresObjectClass.Column,
        ObjectScriptKind.Insert or ObjectScriptKind.Update or ObjectScriptKind.Delete =>
            objectClass is PostgresObjectClass.Table or PostgresObjectClass.PartitionedTable
                or PostgresObjectClass.Partition or PostgresObjectClass.ForeignTable or PostgresObjectClass.View,
        ObjectScriptKind.Create or ObjectScriptKind.DropAndCreate => objectClass is
            PostgresObjectClass.Database or PostgresObjectClass.Schema or PostgresObjectClass.Extension
            or PostgresObjectClass.Table or PostgresObjectClass.PartitionedTable or PostgresObjectClass.Partition
            or PostgresObjectClass.ForeignTable or PostgresObjectClass.View
            or PostgresObjectClass.MaterializedView or PostgresObjectClass.Sequence
            or PostgresObjectClass.Index or PostgresObjectClass.Constraint or PostgresObjectClass.Trigger
            or PostgresObjectClass.Function or PostgresObjectClass.Procedure or PostgresObjectClass.EnumType
            or PostgresObjectClass.Domain or PostgresObjectClass.CompositeType,
        ObjectScriptKind.Drop => objectClass is not PostgresObjectClass.Unknown and not PostgresObjectClass.Column
            and not PostgresObjectClass.Aggregate and not PostgresObjectClass.WindowFunction,
        _ => false,
    };

    public async Task<string> GenerateAsync(
        string connectionString, string database, PostgresObjectIdentity identity,
        ObjectScriptKind kind, int rowLimit = 1000, CancellationToken cancellationToken = default)
    {
        if (!Supports(identity.ObjectClass, kind))
            throw new NotSupportedException($"{kind} scripting is not supported for {identity.ObjectClass}.");
        var value = await metadata.LoadAsync(connectionString, database, identity, cancellationToken)
            .ConfigureAwait(false);
        var drop = value.CanonicalDrop ?? $"DROP {value.ObjectKeyword} {value.QualifiedName};";
        return kind switch
        {
            ObjectScriptKind.Create => value.CanonicalCreate
                ?? throw new NotSupportedException($"CREATE scripting is not available for {identity.ObjectClass}."),
            ObjectScriptKind.Drop => drop,
            ObjectScriptKind.DropAndCreate => $"-- Drop object\n\n{drop}\n\n-- Create object\n\n{value.CanonicalCreate
                ?? throw new NotSupportedException($"CREATE scripting is not available for {identity.ObjectClass}.")}",
            ObjectScriptKind.Select => Select(value, identity, rowLimit),
            ObjectScriptKind.Insert => Insert(value),
            ObjectScriptKind.Update => Update(value),
            ObjectScriptKind.Delete => Delete(value),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    public Task<ObjectScriptMetadata> LoadMetadataAsync(
        string connectionString, string database, PostgresObjectIdentity identity,
        CancellationToken cancellationToken = default) =>
        metadata.LoadAsync(connectionString, database, identity, cancellationToken);

    private static string Select(ObjectScriptMetadata value, PostgresObjectIdentity identity, int limit)
    {
        var columns = identity.ObjectClass == PostgresObjectClass.Column
            ? value.Columns.Where(x => x.Ordinal == identity.SubObjectNumber).ToArray()
            : value.Columns.OrderBy(x => x.Ordinal).ToArray();
        if (columns.Length == 0) throw new InvalidOperationException("No selectable columns were returned for the object.");
        return $"SELECT\n    {string.Join(",\n    ", columns.Select(x => PostgreSqlIdentifierQuoter.Quote(x.Name)))}\nFROM {value.QualifiedName}\nLIMIT {Math.Clamp(limit, 1, 100000)};";
    }

    private static string Insert(ObjectScriptMetadata value)
    {
        var columns = value.Columns.Where(x => x.GeneratedExpression is null
            && x.IdentityKind != "a" && (x.DefaultExpression is null || !x.IsNullable))
            .OrderBy(x => x.Ordinal).ToArray();
        if (columns.Length == 0)
            columns = value.Columns.Where(x => x.GeneratedExpression is null && x.IdentityKind != "a")
                .OrderBy(x => x.Ordinal).ToArray();
        if (columns.Length == 0) return $"INSERT INTO {value.QualifiedName} DEFAULT VALUES;";
        return $"INSERT INTO {value.QualifiedName}\n(\n    {string.Join(",\n    ", columns.Select(x => PostgreSqlIdentifierQuoter.Quote(x.Name)))}\n)\nVALUES\n(\n    {string.Join(",\n    ", columns.Select(x => $"<{x.Name}>"))}\n);";
    }

    private static string Update(ObjectScriptMetadata value)
    {
        var keys = value.Columns.Where(x => x.IsPrimaryKey).OrderBy(x => x.Ordinal).ToArray();
        var writable = value.Columns.Where(x => !x.IsPrimaryKey && x.GeneratedExpression is null && x.IdentityKind != "a")
            .OrderBy(x => x.Ordinal).ToArray();
        if (writable.Length == 0) throw new NotSupportedException("The object has no writable columns.");
        var where = keys.Length == 0 ? "<search_condition>"
            : string.Join("\n    AND ", keys.Select(x => $"{PostgreSqlIdentifierQuoter.Quote(x.Name)} = <{x.Name}>"));
        return $"UPDATE {value.QualifiedName}\nSET\n    {string.Join(",\n    ", writable.Select(x => $"{PostgreSqlIdentifierQuoter.Quote(x.Name)} = <{x.Name}>"))}\nWHERE\n    {where};";
    }

    private static string Delete(ObjectScriptMetadata value)
    {
        var keys = value.Columns.Where(x => x.IsPrimaryKey).OrderBy(x => x.Ordinal).ToArray();
        var where = keys.Length == 0 ? "<search_condition>"
            : string.Join("\n    AND ", keys.Select(x => $"{PostgreSqlIdentifierQuoter.Quote(x.Name)} = <{x.Name}>"));
        return $"DELETE FROM {value.QualifiedName}\nWHERE\n    {where};";
    }
}
