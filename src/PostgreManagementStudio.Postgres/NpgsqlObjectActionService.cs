using Npgsql;
using PostgreManagementStudio.Application;
using PostgreManagementStudio.Core;

namespace PostgreManagementStudio.Postgres;

public sealed class NpgsqlObjectActionService(
    INpgsqlConnectionFactory connections,
    IObjectScriptMetadataProvider metadata) : IObjectActionService
{
    public bool CanRename(PostgresObjectClass value) => value is
        PostgresObjectClass.Schema or PostgresObjectClass.Table or PostgresObjectClass.PartitionedTable
        or PostgresObjectClass.Partition or PostgresObjectClass.ForeignTable
        or PostgresObjectClass.View or PostgresObjectClass.MaterializedView
        or PostgresObjectClass.Sequence or PostgresObjectClass.Index or PostgresObjectClass.Function
        or PostgresObjectClass.Procedure or PostgresObjectClass.EnumType or PostgresObjectClass.Domain
        or PostgresObjectClass.CompositeType;
    public bool CanDelete(PostgresObjectClass value) => CanRename(value)
        || value is PostgresObjectClass.Constraint or PostgresObjectClass.Trigger;

    public async Task RenameAsync(string connectionString, string database, PostgresObjectIdentity identity,
        string newName, bool readOnly = false, CancellationToken cancellationToken = default)
    {
        if (readOnly) throw new InvalidOperationException("Object modification is disabled for a read-only connection.");
        if (!CanRename(identity.ObjectClass)) throw new NotSupportedException($"Rename is not supported for {identity.ObjectClass}.");
        if (string.IsNullOrWhiteSpace(newName)) throw new ArgumentException("A new object name is required.", nameof(newName));
        var value = await metadata.LoadAsync(connectionString,database,identity,cancellationToken).ConfigureAwait(false);
        var sql=$"ALTER {value.ObjectKeyword} {value.QualifiedName} RENAME TO {PostgreSqlIdentifierQuoter.Quote(newName.Trim())};";
        await ExecuteAsync(connectionString,database,sql,cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string connectionString, string database, PostgresObjectIdentity identity,
        bool readOnly = false, CancellationToken cancellationToken = default)
    {
        if (readOnly) throw new InvalidOperationException("Object modification is disabled for a read-only connection.");
        if (!CanDelete(identity.ObjectClass)) throw new NotSupportedException($"Delete is not supported for {identity.ObjectClass}.");
        var service=new ObjectScriptService(metadata);
        var sql=await service.GenerateAsync(connectionString,database,identity,ObjectScriptKind.Drop,cancellationToken:cancellationToken)
            .ConfigureAwait(false);
        await ExecuteAsync(connectionString,database,sql,cancellationToken).ConfigureAwait(false);
    }

    private async Task ExecuteAsync(string connectionString,string database,string sql,CancellationToken token)
    {
        var builder=new NpgsqlConnectionStringBuilder(connectionString){Database=database};
        await using var connection=connections.Create(builder.ConnectionString,"PostgreManagementStudio - Object action");
        await connection.OpenAsync(token).ConfigureAwait(false);
        await using var command=new NpgsqlCommand(sql,connection);
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }
}
