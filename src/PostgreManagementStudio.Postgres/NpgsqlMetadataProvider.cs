using System.Security.Cryptography;
using System.Text;
using Npgsql;
using PostgreManagementStudio.Application;
using PostgreManagementStudio.Core;

namespace PostgreManagementStudio.Postgres;

public sealed class NpgsqlMetadataProvider(INpgsqlConnectionFactory? connectionFactory = null)
    : IPostgresMetadataProvider, IPostgresObjectMetadataProvider
{
    private readonly INpgsqlConnectionFactory _connections = connectionFactory ?? NpgsqlConnectionFactory.Shared;

    public async Task<ObjectMetadataRoot> LoadRootAsync(
        ObjectMetadataContext context,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(context, cancellationToken).ConfigureAwait(false);
        uint databaseOid;
        string serverVersion;
        string serverFingerprint;
        await using (var identity = new NpgsqlCommand("""
            SELECT d.oid::bigint, current_setting('server_version_num'),
                   COALESCE(inet_server_addr()::text, 'local') || ':' ||
                   COALESCE(inet_server_port()::text, current_setting('port')) || ':' ||
                   current_setting('server_version_num')
            FROM pg_database d
            WHERE d.datname = current_database()
            """, connection))
        await using (var reader = await identity.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                throw new MetadataObjectNotFoundException("The selected database no longer exists.");
            databaseOid = checked((uint)reader.GetInt64(0));
            serverVersion = reader.GetString(1);
            serverFingerprint = Hash(reader.GetString(2));
        }

        var databaseIdentity = Identity(context, serverFingerprint, databaseOid, databaseOid,
            PostgresObjectClass.Database, context.Database);
        var schemas = new List<ObjectMetadataDescriptor>();
        await using (var command = new NpgsqlCommand("""
            SELECT n.oid::bigint, n.nspname,
                   EXISTS (
                       SELECT 1 FROM pg_depend d
                       WHERE d.classid = 'pg_namespace'::regclass
                         AND d.objid = n.oid
                         AND d.deptype = 'e'),
                   pg_has_role(n.nspowner, 'USAGE')
            FROM pg_namespace n
            WHERE has_schema_privilege(n.oid, 'USAGE')
               OR pg_has_role(n.nspowner, 'USAGE')
            ORDER BY n.nspname COLLATE "C", n.oid
            """, connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var oid = checked((uint)reader.GetInt64(0));
                var name = reader.GetString(1);
                var classification = reader.GetBoolean(2)
                    ? MetadataSystemClassification.ExtensionOwned
                    : ObjectMetadataRules.ClassifySchema(name);
                schemas.Add(new(
                    Identity(context, serverFingerprint, databaseOid, oid, PostgresObjectClass.Schema, name,
                        schemaOid: oid, parentOid: databaseOid),
                    name, name, name, PostgreSqlIdentifierQuoter.Quote(name), classification, true,
                    CanModify: reader.GetBoolean(3)));
            }
        }

        await using (var command = new NpgsqlCommand("""
            SELECT e.oid::bigint,e.extname,e.extnamespace::bigint,
                   pg_has_role(e.extowner,'USAGE')
            FROM pg_extension e ORDER BY e.extname COLLATE "C",e.oid
            """, connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var oid=checked((uint)reader.GetInt64(0)); var name=reader.GetString(1);
                schemas.Add(new(
                    Identity(context,serverFingerprint,databaseOid,oid,PostgresObjectClass.Extension,name,
                        parentOid:databaseOid,schemaOid:checked((uint)reader.GetInt64(2))),
                    name,null,$"Extension: {name}",PostgreSqlIdentifierQuoter.Quote(name),
                    MetadataSystemClassification.ExtensionOwned,false, CanModify: reader.GetBoolean(3)));
            }
        }

        return new(databaseIdentity, connection.Database, serverVersion,
            ObjectMetadataRules.Filter(schemas, context.ShowSystemObjects), DateTimeOffset.UtcNow);
    }

    public async Task<ObjectMetadataBatch> LoadChildrenAsync(
        ObjectMetadataContext context,
        PostgresObjectIdentity parent,
        CancellationToken cancellationToken = default)
    {
        ValidateContext(context, parent);
        await using var connection = await OpenAsync(context, cancellationToken).ConfigureAwait(false);
        return parent.ObjectClass switch
        {
            PostgresObjectClass.Schema => await LoadSchemaChildrenAsync(connection, context, parent, cancellationToken).ConfigureAwait(false),
            PostgresObjectClass.Table or PostgresObjectClass.PartitionedTable or PostgresObjectClass.Partition
                or PostgresObjectClass.View or PostgresObjectClass.MaterializedView or PostgresObjectClass.ForeignTable =>
                await LoadRelationChildrenAsync(connection, context, parent, cancellationToken).ConfigureAwait(false),
            _ => new(parent, Array.Empty<ObjectMetadataDescriptor>(), DateTimeOffset.UtcNow),
        };
    }

    public async Task<DatabaseMetadataSnapshot> LoadAsync(
        string connectionString,
        string database,
        CancellationToken cancellationToken = default)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString) { Database = database };
        await using var connection = _connections.Create(builder.ConnectionString, "PostgreManagementStudio - Completion Metadata");
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var schemas = await ReadStringsAsync(connection, """
            SELECT n.nspname
            FROM pg_namespace n
            WHERE n.nspname NOT LIKE 'pg_%'
              AND n.nspname <> 'information_schema'
              AND (has_schema_privilege(n.oid, 'USAGE') OR pg_has_role(n.nspowner, 'USAGE'))
            ORDER BY n.nspname COLLATE "C"
            """, cancellationToken).ConfigureAwait(false);

        var relations = new List<RelationMetadata>();
        await using (var command = new NpgsqlCommand("""
            SELECT n.nspname, c.relname, c.relkind
            FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE c.relkind IN ('r','p','v','m','S','f')
              AND n.nspname NOT LIKE 'pg_%'
              AND n.nspname <> 'information_schema'
              AND (has_table_privilege(c.oid, 'SELECT')
                   OR has_any_column_privilege(c.oid, 'SELECT')
                   OR pg_has_role(c.relowner, 'USAGE'))
            ORDER BY n.nspname COLLATE "C", c.relname COLLATE "C", c.oid
            """, connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var kind = reader.GetChar(2) switch
                {
                    'v' => CompletionKind.View,
                    'm' => CompletionKind.MaterializedView,
                    'S' => CompletionKind.Sequence,
                    _ => CompletionKind.Table,
                };
                relations.Add(new(reader.GetString(0), reader.GetString(1), kind, Array.Empty<ColumnMetadata>()));
            }
        }

        var routines = new List<RoutineMetadata>();
        await using (var command = new NpgsqlCommand("""
            SELECT n.nspname, p.proname, pg_get_function_result(p.oid),
                   pg_get_function_identity_arguments(p.oid), p.prokind
            FROM pg_proc p
            JOIN pg_namespace n ON n.oid = p.pronamespace
            WHERE n.nspname NOT LIKE 'pg_%'
              AND n.nspname <> 'information_schema'
              AND (has_schema_privilege(n.oid, 'USAGE') OR pg_has_role(n.nspowner, 'USAGE'))
            ORDER BY n.nspname COLLATE "C", p.proname COLLATE "C",
                     pg_get_function_identity_arguments(p.oid) COLLATE "C", p.oid
            """, connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var kind = reader.GetChar(4) == 'p' ? CompletionKind.Procedure : CompletionKind.Function;
                routines.Add(new(reader.GetString(0), reader.GetString(1),
                    reader.IsDBNull(2) ? "" : reader.GetString(2), reader.GetString(3), kind));
            }
        }

        var columns = new List<(string Schema, string Relation, ColumnMetadata Column)>();
        await using (var command = new NpgsqlCommand("""
            SELECT n.nspname, c.relname, a.attname,
                   format_type(a.atttypid, a.atttypmod), a.attnum, NOT a.attnotnull
            FROM pg_attribute a
            JOIN pg_class c ON c.oid = a.attrelid
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE a.attnum > 0 AND NOT a.attisdropped
              AND c.relkind IN ('r','p','v','m','f')
              AND n.nspname NOT LIKE 'pg_%'
              AND n.nspname <> 'information_schema'
              AND (has_table_privilege(c.oid, 'SELECT')
                   OR has_any_column_privilege(c.oid, 'SELECT')
                   OR pg_has_role(c.relowner, 'USAGE'))
            ORDER BY n.nspname COLLATE "C", c.relname COLLATE "C", a.attnum
            """, connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                columns.Add((reader.GetString(0), reader.GetString(1),
                    new(reader.GetString(2), reader.GetString(3), reader.GetInt16(4), reader.GetBoolean(5))));
        relations = relations.Select(relation => relation with
        {
            Columns = Array.AsReadOnly(columns
                .Where(x => x.Schema == relation.SchemaName && x.Relation == relation.Name)
                .Select(x => x.Column).ToArray()),
        }).ToList();
        var types = await ReadStringsAsync(connection, """
            SELECT t.typname
            FROM pg_type t
            JOIN pg_namespace n ON n.oid=t.typnamespace
            WHERE n.nspname NOT LIKE 'pg_%'
              AND n.nspname <> 'information_schema'
              AND t.typtype IN ('b','c','d','e','r','m')
              AND (has_schema_privilege(n.oid, 'USAGE') OR pg_has_role(n.nspowner, 'USAGE'))
            ORDER BY t.typname COLLATE "C"
            """, cancellationToken).ConfigureAwait(false);
        return new(Hash(builder.ConnectionString), database, schemas, relations, routines, types,
            relations.Where(x => x.Kind == CompletionKind.Sequence).Select(x => x.Name).ToArray(), DateTimeOffset.UtcNow);
    }

    private async Task<ObjectMetadataBatch> LoadSchemaChildrenAsync(
        NpgsqlConnection connection,
        ObjectMetadataContext context,
        PostgresObjectIdentity parent,
        CancellationToken cancellationToken)
    {
        var objects = new List<ObjectMetadataDescriptor>();
        await using (var command = new NpgsqlCommand("""
            SELECT c.oid::bigint, c.relname, c.relkind, c.relispartition,
                   e.oid::bigint, pg_has_role(c.relowner,'USAGE')
            FROM pg_class c
            LEFT JOIN pg_depend d
              ON d.classid = 'pg_class'::regclass
             AND d.objid = c.oid AND d.deptype = 'e'
            LEFT JOIN pg_extension e ON e.oid = d.refobjid
            WHERE c.relnamespace = @schema_oid
              AND c.relkind IN ('r','p','v','m','S','f')
              AND (has_table_privilege(c.oid, 'SELECT')
                   OR has_any_column_privilege(c.oid, 'SELECT')
                   OR pg_has_role(c.relowner, 'USAGE'))
            ORDER BY c.relname COLLATE "C", c.oid
            """, connection))
        {
            command.Parameters.AddWithValue("schema_oid", (long)parent.ObjectOid);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var oid = checked((uint)reader.GetInt64(0));
                var name = reader.GetString(1);
                var relationKind = reader.GetChar(2);
                var isPartition = reader.GetBoolean(3);
                var objectClass = relationKind switch
                {
                    'p' => PostgresObjectClass.PartitionedTable,
                    'v' => PostgresObjectClass.View,
                    'm' => PostgresObjectClass.MaterializedView,
                    'S' => PostgresObjectClass.Sequence,
                    'f' => PostgresObjectClass.ForeignTable,
                    _ when isPartition => PostgresObjectClass.Partition,
                    _ => PostgresObjectClass.Table,
                };
                var extensionOid = reader.IsDBNull(4) ? null : checked((uint?)reader.GetInt64(4));
                var classification = extensionOid.HasValue
                    ? MetadataSystemClassification.ExtensionOwned
                    : ObjectMetadataRules.ClassifySchema(parent.NameSnapshot);
                objects.Add(new(
                    Identity(context, parent.ServerFingerprint, parent.DatabaseOid, oid, objectClass, name,
                        parent.ObjectOid, parent.ObjectOid),
                    name, parent.NameSnapshot, name,
                    PostgreSqlIdentifierQuoter.Qualified(parent.NameSnapshot, name),
                    classification, objectClass is not PostgresObjectClass.Sequence,
                    ExtensionOid: extensionOid, CanModify: reader.GetBoolean(5)));
            }
        }

        await using (var command = new NpgsqlCommand("""
            SELECT p.oid::bigint, p.proname, p.prokind,
                   pg_get_function_identity_arguments(p.oid),
                   e.oid::bigint, pg_has_role(p.proowner,'USAGE')
            FROM pg_proc p
            LEFT JOIN pg_depend d
              ON d.classid = 'pg_proc'::regclass
             AND d.objid = p.oid AND d.deptype = 'e'
            LEFT JOIN pg_extension e ON e.oid = d.refobjid
            WHERE p.pronamespace = @schema_oid
            ORDER BY p.proname COLLATE "C",
                     pg_get_function_identity_arguments(p.oid) COLLATE "C", p.oid
            """, connection))
        {
            command.Parameters.AddWithValue("schema_oid", (long)parent.ObjectOid);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var oid = checked((uint)reader.GetInt64(0));
                var name = reader.GetString(1);
                var objectClass = reader.GetChar(2) switch
                {
                    'p' => PostgresObjectClass.Procedure,
                    'a' => PostgresObjectClass.Aggregate,
                    'w' => PostgresObjectClass.WindowFunction,
                    _ => PostgresObjectClass.Function,
                };
                var signature = reader.GetString(3);
                var extensionOid = reader.IsDBNull(4) ? null : checked((uint?)reader.GetInt64(4));
                var classification = extensionOid.HasValue
                    ? MetadataSystemClassification.ExtensionOwned
                    : ObjectMetadataRules.ClassifySchema(parent.NameSnapshot);
                var quoted = PostgreSqlIdentifierQuoter.Qualified(parent.NameSnapshot, name);
                objects.Add(new(
                    Identity(context, parent.ServerFingerprint, parent.DatabaseOid, oid, objectClass, name,
                        parent.ObjectOid, parent.ObjectOid),
                    name, parent.NameSnapshot, $"{name}({signature})", $"{quoted}({signature})",
                    classification, false, signature, extensionOid, CanModify: reader.GetBoolean(5)));
            }
        }

        await using (var command = new NpgsqlCommand("""
            SELECT t.oid::bigint,t.typname,t.typtype,pg_has_role(t.typowner,'USAGE')
            FROM pg_type t
            WHERE t.typnamespace=@schema_oid
              AND t.typtype IN ('e','d','c')
              AND (t.typtype <> 'c' OR NOT EXISTS
                   (SELECT 1 FROM pg_class c WHERE c.reltype=t.oid AND c.relkind IN ('r','p','v','m','f')))
            ORDER BY t.typname COLLATE "C",t.oid
            """, connection))
        {
            command.Parameters.AddWithValue("schema_oid", (long)parent.ObjectOid);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var oid = checked((uint)reader.GetInt64(0));
                var name = reader.GetString(1);
                var objectClass = reader.GetChar(2) switch
                {
                    'e' => PostgresObjectClass.EnumType,
                    'd' => PostgresObjectClass.Domain,
                    _ => PostgresObjectClass.CompositeType,
                };
                objects.Add(new(
                    Identity(context, parent.ServerFingerprint, parent.DatabaseOid, oid, objectClass, name,
                        parent.ObjectOid, parent.ObjectOid),
                    name, parent.NameSnapshot, name,
                    PostgreSqlIdentifierQuoter.Qualified(parent.NameSnapshot, name),
                    ObjectMetadataRules.ClassifySchema(parent.NameSnapshot), false,
                    CanModify: reader.GetBoolean(3)));
            }
        }

        if (objects.Count == 0 && !await ExistsAsync(connection, "pg_namespace", parent.ObjectOid, cancellationToken).ConfigureAwait(false))
            throw new MetadataObjectNotFoundException("The schema changed while metadata was loading.");
        return new(parent, ObjectMetadataRules.Filter(objects, context.ShowSystemObjects), DateTimeOffset.UtcNow);
    }

    private static async Task<ObjectMetadataBatch> LoadRelationChildrenAsync(
        NpgsqlConnection connection,
        ObjectMetadataContext context,
        PostgresObjectIdentity parent,
        CancellationToken cancellationToken)
    {
        var columns = new List<ObjectMetadataDescriptor>();
        string relationQualifiedName;
        await using (var relationCommand = new NpgsqlCommand("""
            SELECT quote_ident(n.nspname)||'.'||quote_ident(c.relname)
            FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
            WHERE c.oid=@relation_oid
            """, connection))
        {
            relationCommand.Parameters.AddWithValue("relation_oid", (long)parent.ObjectOid);
            relationQualifiedName = (string?)await relationCommand.ExecuteScalarAsync(cancellationToken)
                .ConfigureAwait(false)
                ?? throw new MetadataObjectNotFoundException("The relation changed while metadata was loading.");
        }
        await using var command = new NpgsqlCommand("""
            SELECT a.attname, format_type(a.atttypid, a.atttypmod),
                   a.attnum, NOT a.attnotnull
            FROM pg_attribute a
            WHERE a.attrelid = @relation_oid
              AND a.attnum > 0
              AND NOT a.attisdropped
            ORDER BY a.attnum
            """, connection);
        command.Parameters.AddWithValue("relation_oid", (long)parent.ObjectOid);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var name = reader.GetString(0);
            var dataType = reader.GetString(1);
            var ordinal = reader.GetInt16(2);
            var nullable = reader.GetBoolean(3);
            columns.Add(new(
                Identity(context, parent.ServerFingerprint, parent.DatabaseOid, parent.ObjectOid,
                    PostgresObjectClass.Column, name, parent.ObjectOid, parent.SchemaOid, ordinal),
                name, parent.NameSnapshot, name,
                relationQualifiedName + "." + PostgreSqlIdentifierQuoter.Quote(name),
                MetadataSystemClassification.User, false, dataType, Ordinal: ordinal,
                ExtensionOid: null));
            if (nullable)
                columns[^1] = columns[^1] with { DisplayName = $"{name} — {dataType} nullable" };
            else
                columns[^1] = columns[^1] with { DisplayName = $"{name} — {dataType}" };
        }
        await reader.DisposeAsync().ConfigureAwait(false);
        await AddRelationObjectsAsync(connection, context, parent, columns,
            "SELECT x.oid::bigint,x.conname,pg_get_constraintdef(x.oid,true),pg_has_role(c.relowner,'USAGE') FROM pg_constraint x JOIN pg_class c ON c.oid=x.conrelid WHERE x.conrelid=@relation_oid ORDER BY x.conname",
            PostgresObjectClass.Constraint, relationQualifiedName, cancellationToken).ConfigureAwait(false);
        await AddRelationObjectsAsync(connection, context, parent, columns,
            "SELECT c.oid::bigint,c.relname,pg_get_indexdef(c.oid),pg_has_role(c.relowner,'USAGE') FROM pg_index i JOIN pg_class c ON c.oid=i.indexrelid WHERE i.indrelid=@relation_oid ORDER BY c.relname",
            PostgresObjectClass.Index, relationQualifiedName, cancellationToken).ConfigureAwait(false);
        await AddRelationObjectsAsync(connection, context, parent, columns,
            "SELECT t.oid::bigint,t.tgname,pg_get_triggerdef(t.oid,true),pg_has_role(c.relowner,'USAGE') FROM pg_trigger t JOIN pg_class c ON c.oid=t.tgrelid WHERE t.tgrelid=@relation_oid AND NOT t.tgisinternal ORDER BY t.tgname",
            PostgresObjectClass.Trigger, relationQualifiedName, cancellationToken).ConfigureAwait(false);
        if (columns.Count == 0 && !await ExistsAsync(connection, "pg_class", parent.ObjectOid, cancellationToken).ConfigureAwait(false))
            throw new MetadataObjectNotFoundException("The relation changed while metadata was loading.");
        return new(parent, ObjectMetadataRules.Sort(columns), DateTimeOffset.UtcNow);
    }

    private static async Task AddRelationObjectsAsync(
        NpgsqlConnection connection,
        ObjectMetadataContext context,
        PostgresObjectIdentity parent,
        List<ObjectMetadataDescriptor> values,
        string sql,
        PostgresObjectClass objectClass,
        string relationQualifiedName,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("relation_oid", (long)parent.ObjectOid);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var oid = checked((uint)reader.GetInt64(0));
            var name = reader.GetString(1);
            values.Add(new(
                Identity(context, parent.ServerFingerprint, parent.DatabaseOid, oid, objectClass, name,
                    parent.ObjectOid, parent.SchemaOid),
                name, parent.NameSnapshot, $"{name} — {objectClass}",
                relationQualifiedName + "." + PostgreSqlIdentifierQuoter.Quote(name),
                MetadataSystemClassification.User, false, reader.GetString(2),
                CanModify: reader.GetBoolean(3)));
        }
    }

    private async Task<NpgsqlConnection> OpenAsync(ObjectMetadataContext context, CancellationToken cancellationToken)
    {
        var builder = new NpgsqlConnectionStringBuilder(context.ConnectionString) { Database = context.Database };
        var connection = _connections.Create(builder.ConnectionString, "PostgreManagementStudio - Metadata");
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<bool> ExistsAsync(
        NpgsqlConnection connection,
        string catalogue,
        uint oid,
        CancellationToken cancellationToken)
    {
        var sql = catalogue == "pg_namespace"
            ? "SELECT EXISTS (SELECT 1 FROM pg_namespace WHERE oid=@oid)"
            : "SELECT EXISTS (SELECT 1 FROM pg_class WHERE oid=@oid)";
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("oid", (long)oid);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
    }

    private static async Task<IReadOnlyList<string>> ReadStringsAsync(
        NpgsqlConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        var values = new List<string>();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            values.Add(reader.GetString(0));
        return Array.AsReadOnly(values.ToArray());
    }

    private static PostgresObjectIdentity Identity(
        ObjectMetadataContext context,
        string serverFingerprint,
        uint databaseOid,
        uint objectOid,
        PostgresObjectClass objectClass,
        string name,
        uint? parentOid = null,
        uint? schemaOid = null,
        int? subObject = null) => new()
    {
        ConnectionProfileId = context.ConnectionProfileId,
        ConfigurationIdentity = context.ConfigurationIdentity,
        ServerFingerprint = serverFingerprint,
        DatabaseOid = databaseOid,
        ObjectOid = objectOid,
        ObjectClass = objectClass,
        ParentOid = parentOid,
        SchemaOid = schemaOid,
        SubObjectNumber = subObject,
        NameSnapshot = name,
    };

    private static void ValidateContext(ObjectMetadataContext context, PostgresObjectIdentity parent)
    {
        if (context.ConnectionProfileId != parent.ConnectionProfileId
            || context.ConfigurationIdentity != parent.ConfigurationIdentity)
            throw new InvalidOperationException("Metadata identity does not belong to the active connection profile.");
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
