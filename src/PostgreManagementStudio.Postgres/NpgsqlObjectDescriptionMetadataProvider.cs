using System.Security.Cryptography;
using System.Text;
using Npgsql;
using NpgsqlTypes;
using PostgreManagementStudio.Application;
using PostgreManagementStudio.Core;

namespace PostgreManagementStudio.Postgres;

public sealed class NpgsqlObjectDescriptionMetadataProvider(INpgsqlConnectionFactory connections)
    : IObjectDescriptionMetadataProvider
{
    public async Task<IReadOnlyList<ObjectDescriptionCandidate>> ResolveAsync(
        string connectionString,
        string database,
        EditorObjectReference reference,
        CancellationToken cancellationToken = default)
    {
        if (reference.IsEditorLocal) return [];
        var builder = new NpgsqlConnectionStringBuilder(connectionString) { Database = database };
        await using var connection = connections.Create(
            builder.ConnectionString, "PostgreManagementStudio - Describe resolution");
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var (databaseOid, fingerprint) = await IdentityAsync(connection, cancellationToken).ConfigureAwait(false);

        var parts = reference.NameParts;
        var memberForm = reference.MemberName is not null || parts.Count >= 3;
        var schema = memberForm && parts.Count >= 3 ? parts[^3]
            : !memberForm && parts.Count >= 2 ? parts[^2] : null;
        var name = memberForm && reference.RelationAlias is null && parts.Count >= 2
            ? parts[^2] : parts[^1];
        if (reference.RelationAlias is not null)
        {
            schema = parts.Count >= 2 ? parts[^2] : null;
            name = parts[^1];
        }

        const string sql = """
            WITH candidates AS (
              SELECT c.oid::bigint AS oid, NULL::bigint AS parent_oid, n.oid::bigint AS schema_oid,
                     n.nspname, c.relname AS name,
                     CASE c.relkind WHEN 'r' THEN 'Table' WHEN 'p' THEN 'Partitioned table'
                       WHEN 'v' THEN 'View' WHEN 'm' THEN 'Materialized view'
                       WHEN 'f' THEN 'Foreign table' WHEN 'S' THEN 'Sequence'
                       WHEN 'i' THEN 'Index' WHEN 'I' THEN 'Partitioned index' END AS object_type,
                     CASE c.relkind WHEN 'r' THEN 2 WHEN 'p' THEN 3 WHEN 'v' THEN 5
                       WHEN 'm' THEN 6 WHEN 'S' THEN 7 WHEN 'f' THEN 8 ELSE 9 END AS object_class,
                     pg_get_userbyid(c.relowner) AS owner, NULL::text AS signature,
                     c.relpersistence='t' AS temporary,
                     CASE WHEN c.relkind IN ('i','I') THEN pg_table_is_visible(i.indrelid)
                          ELSE pg_table_is_visible(c.oid) END AS visible
              FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
              LEFT JOIN pg_index i ON i.indexrelid=c.oid
              WHERE c.relkind IN ('r','p','v','m','f','S','i','I')
              UNION ALL
              SELECT p.oid::bigint,NULL,n.oid::bigint,n.nspname,p.proname,
                     CASE WHEN p.prokind='p' THEN 'Procedure' ELSE 'Function' END,
                     CASE WHEN p.prokind='p' THEN 11 ELSE 10 END,
                     pg_get_userbyid(p.proowner),pg_catalog.oidvectortypes(p.proargtypes),false,
                     pg_function_is_visible(p.oid)
              FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace
              WHERE p.prokind IN ('f','p')
              UNION ALL
              SELECT t.oid::bigint,NULL,n.oid::bigint,n.nspname,t.typname,
                     CASE WHEN t.typtype='e' THEN 'Enum' WHEN t.typtype='d' THEN 'Domain'
                          ELSE 'Composite type' END,
                     CASE WHEN t.typtype='e' THEN 17 WHEN t.typtype='d' THEN 18 ELSE 19 END,
                     pg_get_userbyid(t.typowner),NULL,false,pg_type_is_visible(t.oid)
              FROM pg_type t JOIN pg_namespace n ON n.oid=t.typnamespace
              WHERE t.typtype IN ('e','d') OR
                    (t.typtype='c' AND t.typrelid<>0 AND
                     EXISTS(SELECT 1 FROM pg_class tc WHERE tc.oid=t.typrelid AND tc.relkind='c'))
              UNION ALL
              SELECT x.oid::bigint,x.conrelid::bigint,n.oid::bigint,n.nspname,x.conname,
                     'Constraint',15,pg_get_userbyid(c.relowner),NULL,false,pg_table_is_visible(c.oid)
              FROM pg_constraint x JOIN pg_class c ON c.oid=x.conrelid
              JOIN pg_namespace n ON n.oid=c.relnamespace
              UNION ALL
              SELECT t.oid::bigint,t.tgrelid::bigint,n.oid::bigint,n.nspname,t.tgname,
                     'Trigger',16,pg_get_userbyid(c.relowner),NULL,false,pg_table_is_visible(c.oid)
              FROM pg_trigger t JOIN pg_class c ON c.oid=t.tgrelid
              JOIN pg_namespace n ON n.oid=c.relnamespace WHERE NOT t.tgisinternal
              UNION ALL
              SELECT n.oid::bigint,NULL,n.oid::bigint,n.nspname,n.nspname,
                     'Schema',1,pg_get_userbyid(n.nspowner),NULL,false,
                     n.nspname=ANY(current_schemas(true))
              FROM pg_namespace n
            )
            SELECT oid,parent_oid,schema_oid,nspname,name,object_type,object_class,owner,signature,
                   temporary,visible
            FROM candidates
            WHERE name=@name AND (@schema IS NULL OR nspname=@schema)
              AND (@signature IS NULL OR signature=@signature)
            ORDER BY temporary DESC,visible DESC,nspname,object_type,signature NULLS FIRST
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("name", name);
        command.Parameters.Add("schema", NpgsqlDbType.Text).Value = (object?)schema ?? DBNull.Value;
        command.Parameters.Add("signature", NpgsqlDbType.Text).Value =
            string.IsNullOrWhiteSpace(reference.RoutineSignature)
                ? DBNull.Value : reference.RoutineSignature;
        var candidates = new List<ObjectDescriptionCandidate>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var objectClass = (PostgresObjectClass)reader.GetInt32(6);
            var candidateName = reader.GetString(4);
            var candidateSchema = reader.GetString(3);
            var signature = reader.IsDBNull(8) ? null : reader.GetString(8);
            var identity = new PostgresObjectIdentity
            {
                ConnectionProfileId = "editor",
                ConfigurationIdentity = Convert.ToHexString(SHA256.HashData(
                    Encoding.UTF8.GetBytes(builder.Host + ":" + builder.Port))),
                ServerFingerprint = fingerprint,
                DatabaseOid = databaseOid,
                ObjectOid = checked((uint)reader.GetInt64(0)),
                ObjectClass = objectClass,
                ParentOid = reader.IsDBNull(1) ? null : checked((uint)reader.GetInt64(1)),
                SchemaOid = checked((uint)reader.GetInt64(2)),
                NameSnapshot = candidateName,
            };
            var qualified = PostgreSqlIdentifierQuoter.Qualified(candidateSchema, candidateName)
                + (signature is null ? string.Empty : $"({signature})");
            candidates.Add(new(identity, qualified, reader.GetString(5), reader.GetString(7),
                signature, reader.GetBoolean(9), reader.GetBoolean(10)));
        }
        return candidates;
    }

    public async Task<ObjectDescription> LoadAsync(
        string connectionString,
        string database,
        ObjectDescriptionCandidate candidate,
        string? targetColumn,
        CancellationToken cancellationToken = default)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString) { Database = database };
        await using var connection = connections.Create(
            builder.ConnectionString, "PostgreManagementStudio - Object description");
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var currentIdentity = await IdentityAsync(connection, cancellationToken).ConfigureAwait(false);
        if (currentIdentity.DatabaseOid != candidate.Identity.DatabaseOid
            || currentIdentity.Fingerprint != candidate.Identity.ServerFingerprint)
            throw new InvalidOperationException(
                "The description target belongs to another server or database. Retry Alt+F1.");

        return candidate.Identity.ObjectClass switch
        {
            PostgresObjectClass.Table or PostgresObjectClass.PartitionedTable
                or PostgresObjectClass.Partition or PostgresObjectClass.View
                or PostgresObjectClass.MaterializedView or PostgresObjectClass.ForeignTable =>
                await LoadRelationAsync(connection, candidate, targetColumn, cancellationToken).ConfigureAwait(false),
            _ => await LoadOtherAsync(connectionString, database, candidate, cancellationToken).ConfigureAwait(false),
        };
    }

    public async Task<ObjectDescriptionSecondaryDetails> LoadSecondaryAsync(
        string connectionString,
        string database,
        ObjectDescriptionCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        if (candidate.Identity.ObjectClass is not (
            PostgresObjectClass.Table or PostgresObjectClass.PartitionedTable
            or PostgresObjectClass.Partition or PostgresObjectClass.View
            or PostgresObjectClass.MaterializedView or PostgresObjectClass.ForeignTable))
            return new(null, string.Empty);
        var builder = new NpgsqlConnectionStringBuilder(connectionString) { Database = database };
        await using var connection = connections.Create(
            builder.ConnectionString, "PostgreManagementStudio - Description details");
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var currentIdentity = await IdentityAsync(connection, cancellationToken).ConfigureAwait(false);
        if (currentIdentity.DatabaseOid != candidate.Identity.DatabaseOid
            || currentIdentity.Fingerprint != candidate.Identity.ServerFingerprint)
            throw new InvalidOperationException("The description target changed while details were loading.");
        var details = await RelationDetailsAsync(
            connection, candidate.Identity.ObjectOid, cancellationToken).ConfigureAwait(false);
        await using var sizeCommand = new NpgsqlCommand(
            "SELECT CASE WHEN relkind IN ('r','p','m','f') THEN pg_total_relation_size(oid) END FROM pg_class WHERE oid=@oid",
            connection);
        sizeCommand.Parameters.AddWithValue("oid", (long)candidate.Identity.ObjectOid);
        var value = await sizeCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return new(value is null or DBNull ? null : Convert.ToInt64(value), details);
    }

    private static async Task<ObjectDescription> LoadRelationAsync(
        NpgsqlConnection connection,
        ObjectDescriptionCandidate candidate,
        string? targetColumn,
        CancellationToken cancellationToken)
    {
        const string headerSql = """
            SELECT c.relpersistence::text,obj_description(c.oid,'pg_class'),ts.spcname,
                   CASE WHEN c.relispartition THEN 'Partition'
                        WHEN c.relkind='p' THEN 'Partitioned'
                        WHEN EXISTS(SELECT 1 FROM pg_inherits i WHERE i.inhrelid=c.oid) THEN 'Inherited'
                        ELSE NULL END,
                   c.reltuples::bigint,
                   NULL::bigint,
                   CASE WHEN c.relkind IN ('v','m') THEN pg_get_viewdef(c.oid,true) END,
                   c.relispopulated
            FROM pg_class c LEFT JOIN pg_tablespace ts ON ts.oid=c.reltablespace
            WHERE c.oid=@oid
            """;
        await using var header = new NpgsqlCommand(headerSql, connection);
        header.Parameters.AddWithValue("oid", (long)candidate.Identity.ObjectOid);
        await using var headerReader = await header.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await headerReader.ReadAsync(cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("The object was dropped or renamed. Retry Alt+F1.");
        var persistence = headerReader.GetString(0) switch
        {
            "t" => "Temporary", "u" => "Unlogged", _ => "Permanent",
        };
        var comment = headerReader.IsDBNull(1) ? null : headerReader.GetString(1);
        var tablespace = headerReader.IsDBNull(2) ? null : headerReader.GetString(2);
        var status = headerReader.IsDBNull(3) ? null : headerReader.GetString(3);
        var estimatedRows = headerReader.GetInt64(4);
        long? size = headerReader.IsDBNull(5) ? null : headerReader.GetInt64(5);
        var definition = headerReader.IsDBNull(6) ? null : headerReader.GetString(6);
        var populated = headerReader.GetBoolean(7);
        await headerReader.DisposeAsync().ConfigureAwait(false);
        if (candidate.Identity.ObjectClass == PostgresObjectClass.MaterializedView)
            status = $"{status ?? "Materialized"}; {(populated ? "populated" : "not populated")}";

        var columns = new List<ObjectDescriptionColumn>();
        const string columnSql = """
            SELECT a.attnum,a.attname,format_type(a.atttypid,a.atttypmod),NOT a.attnotnull,
                   pg_get_expr(d.adbin,d.adrelid),
                   CASE a.attidentity WHEN 'a' THEN 'ALWAYS' WHEN 'd' THEN 'BY DEFAULT' ELSE '' END,
                   CASE WHEN a.attgenerated<>'' THEN pg_get_expr(d.adbin,d.adrelid) END,
                   CASE WHEN a.attcollation<>ty.typcollation
                        THEN quote_ident(cn.nspname)||'.'||quote_ident(co.collname) END,
                   EXISTS(SELECT 1 FROM pg_constraint x WHERE x.conrelid=a.attrelid
                          AND x.contype='p' AND a.attnum=ANY(x.conkey)),
                   EXISTS(SELECT 1 FROM pg_constraint x WHERE x.conrelid=a.attrelid
                          AND x.contype='u' AND a.attnum=ANY(x.conkey)),
                   EXISTS(SELECT 1 FROM pg_constraint x WHERE x.conrelid=a.attrelid
                          AND x.contype='f' AND a.attnum=ANY(x.conkey)),
                   (SELECT quote_ident(rn.nspname)||'.'||quote_ident(rc.relname)||'.'||
                           quote_ident(ra.attname)
                    FROM pg_constraint x JOIN pg_class rc ON rc.oid=x.confrelid
                    JOIN pg_namespace rn ON rn.oid=rc.relnamespace
                    JOIN LATERAL generate_subscripts(x.conkey,1) s(i) ON true
                    JOIN pg_attribute ra ON ra.attrelid=x.confrelid AND ra.attnum=x.confkey[s.i]
                    WHERE x.conrelid=a.attrelid AND x.contype='f' AND x.conkey[s.i]=a.attnum LIMIT 1),
                   col_description(a.attrelid,a.attnum),
                   ARRAY(SELECT ic.relname FROM pg_index ix JOIN pg_class ic ON ic.oid=ix.indexrelid
                         WHERE ix.indrelid=a.attrelid AND a.attnum=ANY(ix.indkey) ORDER BY ic.relname),
                   array_to_string(a.attacl,E'\n')
            FROM pg_attribute a JOIN pg_type ty ON ty.oid=a.atttypid
            LEFT JOIN pg_attrdef d ON d.adrelid=a.attrelid AND d.adnum=a.attnum
            LEFT JOIN pg_collation co ON co.oid=a.attcollation
            LEFT JOIN pg_namespace cn ON cn.oid=co.collnamespace
            WHERE a.attrelid=@oid AND a.attnum>0 AND NOT a.attisdropped
            ORDER BY a.attnum
            """;
        await using var command = new NpgsqlCommand(columnSql, connection);
        command.Parameters.AddWithValue("oid", (long)candidate.Identity.ObjectOid);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            columns.Add(new(
                reader.GetInt16(0), reader.GetString(1), reader.GetString(2), reader.GetBoolean(3),
                reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.GetBoolean(8), reader.GetBoolean(9), reader.GetBoolean(10),
                reader.IsDBNull(11) ? null : reader.GetString(11),
                reader.IsDBNull(12) ? null : reader.GetString(12),
                reader.GetFieldValue<string[]>(13),
                reader.IsDBNull(14) ? null : reader.GetString(14)));
        await reader.DisposeAsync().ConfigureAwait(false);

        if (targetColumn is not null
            && !columns.Any(column => column.Name.Equals(targetColumn, StringComparison.Ordinal)))
            throw new InvalidOperationException(
                $"Column {PostgreSqlIdentifierQuoter.Quote(targetColumn)} was not found on {candidate.QualifiedName}.");

        return new(candidate, persistence, comment, tablespace, status, estimatedRows, size,
            columns, string.Empty, definition, targetColumn);
    }

    private async Task<ObjectDescription> LoadOtherAsync(
        string connectionString,
        string database,
        ObjectDescriptionCandidate candidate,
        CancellationToken cancellationToken)
    {
        var scripts = new NpgsqlObjectScriptMetadataProvider(connections);
        var value = await scripts.LoadAsync(connectionString, database, candidate.Identity, cancellationToken)
            .ConfigureAwait(false);
        var details = new StringBuilder();
        details.AppendLine($"Object: {value.QualifiedName}");
        details.AppendLine($"Type: {candidate.ObjectType}");
        details.AppendLine($"Owner: {candidate.Owner}");
        if (!string.IsNullOrWhiteSpace(value.Comment)) details.AppendLine($"Comment: {value.Comment}");
        if (!string.IsNullOrWhiteSpace(value.Privileges))
            details.AppendLine().AppendLine("Privileges").AppendLine(value.Privileges);
        if (value.Constraints.Count > 0)
            details.AppendLine().AppendLine("Constraints").AppendLine(string.Join(Environment.NewLine, value.Constraints));
        if (value.Indexes.Count > 0)
            details.AppendLine().AppendLine("Indexes").AppendLine(string.Join(Environment.NewLine, value.Indexes));
        if (value.Triggers.Count > 0)
            details.AppendLine().AppendLine("Triggers").AppendLine(string.Join(Environment.NewLine, value.Triggers));
        return new(candidate, candidate.IsTemporary ? "Temporary" : "Permanent", value.Comment,
            null, null, value.EstimatedRows, value.SizeBytes, [], details.ToString(),
            value.CanonicalCreate);
    }

    private static async Task<string> RelationDetailsAsync(
        NpgsqlConnection connection, uint oid, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT 'Constraint: '||quote_ident(x.conname)||' - '||pg_get_constraintdef(x.oid,true)
            FROM pg_constraint x WHERE x.conrelid=@oid
            UNION ALL
            SELECT 'Index: '||pg_get_indexdef(i.indexrelid)
            FROM pg_index i WHERE i.indrelid=@oid
            UNION ALL
            SELECT 'Trigger: '||pg_get_triggerdef(t.oid,true)
            FROM pg_trigger t WHERE t.tgrelid=@oid AND NOT t.tgisinternal
            ORDER BY 1
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("oid", (long)oid);
        var values = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            values.Add(reader.GetString(0));
        return string.Join(Environment.NewLine, values);
    }

    private static async Task<(uint DatabaseOid, string Fingerprint)> IdentityAsync(
        NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT d.oid::bigint,
                   COALESCE(inet_server_addr()::text,'local')||':'||
                   COALESCE(inet_server_port()::text,current_setting('port'))||':'||
                   current_setting('server_version_num')
            FROM pg_database d WHERE d.datname=current_database()
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("The active database identity could not be read.");
        return (checked((uint)reader.GetInt64(0)),
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(reader.GetString(1)))));
    }
}
