using Npgsql;
using System.Security.Cryptography;
using System.Text;
using PostgreManagementStudio.Application;
using PostgreManagementStudio.Core;

namespace PostgreManagementStudio.Postgres;

public sealed class NpgsqlObjectScriptMetadataProvider(INpgsqlConnectionFactory connections)
    : IObjectScriptMetadataProvider
{
    public async Task<ObjectScriptMetadata> LoadAsync(
        string connectionString, string database, PostgresObjectIdentity identity,
        CancellationToken cancellationToken = default)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString) { Database = database };
        await using var connection = connections.Create(builder.ConnectionString, "PostgreManagementStudio - Object scripting");
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await ValidateIdentityAsync(connection, identity, cancellationToken).ConfigureAwait(false);
        return identity.ObjectClass switch
        {
            PostgresObjectClass.Table or PostgresObjectClass.PartitionedTable or PostgresObjectClass.Partition
                or PostgresObjectClass.ForeignTable or PostgresObjectClass.View or PostgresObjectClass.MaterializedView
                or PostgresObjectClass.Sequence or PostgresObjectClass.Column =>
                await RelationAsync(connection, identity, cancellationToken).ConfigureAwait(false),
            PostgresObjectClass.Function or PostgresObjectClass.Procedure or PostgresObjectClass.Aggregate
                or PostgresObjectClass.WindowFunction =>
                await RoutineAsync(connection, identity, cancellationToken).ConfigureAwait(false),
            PostgresObjectClass.Index or PostgresObjectClass.Constraint or PostgresObjectClass.Trigger =>
                await RelationChildAsync(connection, identity, cancellationToken).ConfigureAwait(false),
            PostgresObjectClass.EnumType or PostgresObjectClass.Domain or PostgresObjectClass.CompositeType =>
                await TypeAsync(connection, identity, cancellationToken).ConfigureAwait(false),
            PostgresObjectClass.Extension => await ExtensionAsync(connection, identity, cancellationToken).ConfigureAwait(false),
            PostgresObjectClass.Schema => await SchemaAsync(connection, identity, cancellationToken).ConfigureAwait(false),
            PostgresObjectClass.Database => await DatabaseAsync(connection, identity, cancellationToken).ConfigureAwait(false),
            _ => throw new NotSupportedException($"Scripting metadata is not available for {identity.ObjectClass}."),
        };
    }

    private static async Task<ObjectScriptMetadata> RelationAsync(
        NpgsqlConnection connection, PostgresObjectIdentity identity, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT n.nspname, c.relname, c.relkind, obj_description(c.oid, 'pg_class'),
                   CASE WHEN c.relkind IN ('v','m') THEN pg_get_viewdef(c.oid, true) END,
                   pg_get_partkeydef(c.oid), pg_get_userbyid(c.relowner),
                   c.reltuples::bigint, pg_total_relation_size(c.oid),
                   array_to_string(c.relacl, E'\n'), c.relispartition,
                   pg_get_expr(c.relpartbound,c.oid,true), pn.nspname, pc.relname,
                   ts.spcname, array_to_string(c.reloptions, ', '), c.relispopulated,
                   fs.srvname, ft.ftoptions
            FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
            LEFT JOIN pg_inherits i ON i.inhrelid=c.oid
            LEFT JOIN pg_class pc ON pc.oid=i.inhparent
            LEFT JOIN pg_namespace pn ON pn.oid=pc.relnamespace
            LEFT JOIN pg_tablespace ts ON ts.oid=c.reltablespace
            LEFT JOIN pg_foreign_table ft ON ft.ftrelid=c.oid
            LEFT JOIN pg_foreign_server fs ON fs.oid=ft.ftserver
            WHERE c.oid=@oid
            """, connection);
        command.Parameters.AddWithValue("oid", (long)identity.ObjectOid);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("The selected object no longer exists.");
        var schema = reader.GetString(0);
        var name = reader.GetString(1);
        var relkind = reader.GetChar(2);
        var comment = reader.IsDBNull(3) ? null : reader.GetString(3);
        var viewDefinition = reader.IsDBNull(4) ? null : reader.GetString(4);
        var partitionKey = reader.IsDBNull(5) ? null : reader.GetString(5);
        var owner = reader.GetString(6);
        var estimatedRows = reader.GetInt64(7);
        var sizeBytes = reader.GetInt64(8);
        var privileges = reader.IsDBNull(9) ? null : reader.GetString(9);
        var isPartition = reader.GetBoolean(10);
        var partitionBound = reader.IsDBNull(11) ? null : reader.GetString(11);
        var partitionParent = reader.IsDBNull(12) ? null
            : PostgreSqlIdentifierQuoter.Qualified(reader.GetString(12), reader.GetString(13));
        var tablespace = reader.IsDBNull(14) ? null : reader.GetString(14);
        var storageParameters = reader.IsDBNull(15) ? null : reader.GetString(15);
        var isPopulated = reader.GetBoolean(16);
        var foreignServer = reader.IsDBNull(17) ? null : reader.GetString(17);
        var foreignOptions = reader.IsDBNull(18) ? null : reader.GetFieldValue<string[]>(18);
        await reader.DisposeAsync().ConfigureAwait(false);

        var columns = await ColumnsAsync(connection, identity.ObjectOid, cancellationToken).ConfigureAwait(false);
        var constraints = await StringsAsync(connection,
            "SELECT 'CONSTRAINT ' || quote_ident(conname) || ' ' || pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid=@oid AND conislocal ORDER BY conname",
            identity.ObjectOid, cancellationToken).ConfigureAwait(false);
        var indexes = await StringsAsync(connection,
            "SELECT pg_get_indexdef(i.indexrelid) || ';' FROM pg_index i WHERE i.indrelid=@oid AND NOT EXISTS (SELECT 1 FROM pg_constraint x WHERE x.conindid=i.indexrelid) AND NOT EXISTS (SELECT 1 FROM pg_inherits h WHERE h.inhrelid=i.indexrelid) ORDER BY i.indexrelid",
            identity.ObjectOid, cancellationToken).ConfigureAwait(false);
        var triggers = await StringsAsync(connection,
            "SELECT pg_get_triggerdef(oid, true) || ';' FROM pg_trigger WHERE tgrelid=@oid AND NOT tgisinternal AND tgparentid=0 ORDER BY tgname",
            identity.ObjectOid, cancellationToken).ConfigureAwait(false);
        var inheritance = await StringsAsync(connection, """
            SELECT quote_ident(n.nspname)||'.'||quote_ident(p.relname)
            FROM pg_inherits i JOIN pg_class c ON c.oid=i.inhrelid
            JOIN pg_class p ON p.oid=i.inhparent JOIN pg_namespace n ON n.oid=p.relnamespace
            WHERE c.oid=@oid AND NOT c.relispartition ORDER BY i.inhseqno
            """, identity.ObjectOid, cancellationToken).ConfigureAwait(false);
        var qualified = PostgreSqlIdentifierQuoter.Qualified(schema, name);
        var keyword = relkind switch { 'v' => "VIEW", 'm' => "MATERIALIZED VIEW", 'S' => "SEQUENCE", 'f' => "FOREIGN TABLE", _ => "TABLE" };
        var create = relkind switch
        {
            'v' => $"CREATE OR REPLACE VIEW {qualified} AS\n{viewDefinition!.TrimEnd(';')};",
            'm' => $"CREATE MATERIALIZED VIEW {qualified} AS\n{viewDefinition!.TrimEnd(';')}\nWITH {(isPopulated ? "DATA" : "NO DATA")};"
                + Section("Indexes", indexes),
            'S' => await SequenceAsync(connection, identity.ObjectOid, qualified, cancellationToken).ConfigureAwait(false),
            'f' => ForeignTableCreate(qualified, columns, constraints, foreignServer, foreignOptions),
            _ when isPartition => PartitionCreate(qualified, partitionParent, partitionBound, indexes, triggers, comment),
            _ => TableCreate(qualified, columns, constraints, partitionKey, inheritance,
                tablespace, storageParameters, indexes, triggers, comment),
        };
        return new(identity, schema, name, qualified, keyword, create, columns, constraints, indexes, triggers,
            comment, Owner: owner, EstimatedRows: estimatedRows, SizeBytes: sizeBytes, Privileges: privileges);
    }

    private static async Task<IReadOnlyList<ScriptColumn>> ColumnsAsync(
        NpgsqlConnection connection, uint oid, CancellationToken cancellationToken)
    {
        var values = new List<ScriptColumn>();
        await using var command = new NpgsqlCommand("""
            SELECT a.attname, format_type(a.atttypid,a.atttypmod), NOT a.attnotnull,
                   pg_get_expr(d.adbin,d.adrelid), a.attidentity::text,
                   CASE WHEN a.attgenerated <> '' THEN pg_get_expr(d.adbin,d.adrelid) END,
                   a.attnum,
                   EXISTS (SELECT 1 FROM pg_constraint p WHERE p.conrelid=a.attrelid
                           AND p.contype='p' AND a.attnum=ANY(p.conkey)),
                   CASE WHEN a.attcollation<>t.typcollation
                        THEN quote_ident(cn.nspname)||'.'||quote_ident(coll.collname) END,
                   col_description(a.attrelid,a.attnum),a.attfdwoptions,a.attislocal
            FROM pg_attribute a
            JOIN pg_type t ON t.oid=a.atttypid
            LEFT JOIN pg_collation coll ON coll.oid=a.attcollation
            LEFT JOIN pg_namespace cn ON cn.oid=coll.collnamespace
            LEFT JOIN pg_attrdef d ON d.adrelid=a.attrelid AND d.adnum=a.attnum
            WHERE a.attrelid=@oid AND a.attnum>0 AND NOT a.attisdropped ORDER BY a.attnum
            """, connection);
        command.Parameters.AddWithValue("oid", (long)oid);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            values.Add(new(reader.GetString(0), reader.GetString(1), reader.GetBoolean(2),
                reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5), reader.GetInt16(6), reader.GetBoolean(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetFieldValue<string[]>(10),
                reader.GetBoolean(11)));
        return values;
    }

    private static string TableCreate(string qualified, IReadOnlyList<ScriptColumn> columns,
        IReadOnlyList<string> constraints, string? partitionKey, IReadOnlyList<string> inheritance,
        string? tablespace, string? storageParameters, IReadOnlyList<string> indexes,
        IReadOnlyList<string> triggers, string? comment)
    {
        var scriptedColumns = inheritance.Count > 0 ? columns.Where(x => x.IsLocal) : columns;
        var definitions = scriptedColumns.Select(c =>
        {
            var text = $"{PostgreSqlIdentifierQuoter.Quote(c.Name)} {c.DataType}";
            if (c.Collation is not null) text += $" COLLATE {c.Collation}";
            if (c.GeneratedExpression is not null) text += $" GENERATED ALWAYS AS ({c.GeneratedExpression}) STORED";
            else if (c.IdentityKind == "a") text += " GENERATED ALWAYS AS IDENTITY";
            else if (c.IdentityKind == "d") text += " GENERATED BY DEFAULT AS IDENTITY";
            else if (c.DefaultExpression is not null) text += $" DEFAULT {c.DefaultExpression}";
            if (!c.IsNullable) text += " NOT NULL";
            return text;
        }).Concat(constraints).Select(x => "    " + x);
        var sql = $"CREATE TABLE {qualified}\n(\n{string.Join(",\n", definitions)}\n)";
        if (inheritance.Count > 0) sql += $"\nINHERITS ({string.Join(", ", inheritance)})";
        if (!string.IsNullOrWhiteSpace(partitionKey)) sql += $"\nPARTITION BY {partitionKey}";
        if (!string.IsNullOrWhiteSpace(storageParameters)) sql += $"\nWITH ({storageParameters})";
        if (!string.IsNullOrWhiteSpace(tablespace))
            sql += $"\nTABLESPACE {PostgreSqlIdentifierQuoter.Quote(tablespace)}";
        sql += ";";
        if (!string.IsNullOrWhiteSpace(comment))
            sql += $"\n\nCOMMENT ON TABLE {qualified} IS {Literal(comment)};";
        foreach (var column in columns.Where(x => !string.IsNullOrWhiteSpace(x.Comment)))
            sql += $"\n\nCOMMENT ON COLUMN {qualified}.{PostgreSqlIdentifierQuoter.Quote(column.Name)} IS {Literal(column.Comment!)};";
        return sql + Section("Indexes", indexes) + Section("Triggers", triggers);
    }

    private static string ForeignTableCreate(string qualified, IReadOnlyList<ScriptColumn> columns,
        IReadOnlyList<string> constraints, string? server, IReadOnlyList<string>? tableOptions)
    {
        if (string.IsNullOrWhiteSpace(server))
            throw new InvalidOperationException("Foreign server metadata is unavailable.");
        var definitions = columns.Select(c =>
        {
            var text = $"{PostgreSqlIdentifierQuoter.Quote(c.Name)} {c.DataType}";
            if (c.Collation is not null) text += $" COLLATE {c.Collation}";
            if (c.ForeignOptions is { Count: > 0 }) text += $" OPTIONS ({FormatOptions(c.ForeignOptions)})";
            if (c.DefaultExpression is not null) text += $" DEFAULT {c.DefaultExpression}";
            if (!c.IsNullable) text += " NOT NULL";
            return text;
        }).Concat(constraints).Select(x => "    " + x);
        var sql = $"CREATE FOREIGN TABLE {qualified}\n(\n{string.Join(",\n", definitions)}\n)"
            + $"\nSERVER {PostgreSqlIdentifierQuoter.Quote(server)}";
        if (tableOptions is { Count: > 0 }) sql += $"\nOPTIONS ({FormatOptions(tableOptions)})";
        return sql + ";";
    }

    private static string FormatOptions(IEnumerable<string> options) =>
        string.Join(", ", options.Select(option =>
        {
            var separator = option.IndexOf('=');
            if (separator <= 0)
                throw new InvalidOperationException("A foreign-data option has an invalid catalogue representation.");
            return $"{PostgreSqlIdentifierQuoter.Quote(option[..separator])} {Literal(option[(separator + 1)..])}";
        }));

    private static string PartitionCreate(string qualified, string? parent, string? bound,
        IReadOnlyList<string> indexes, IReadOnlyList<string> triggers, string? comment)
    {
        if (parent is null || bound is null)
            throw new InvalidOperationException("Partition parent or bound metadata is unavailable.");
        var sql = $"CREATE TABLE {qualified}\n    PARTITION OF {parent}\n    {bound};";
        if (!string.IsNullOrWhiteSpace(comment))
            sql += $"\n\nCOMMENT ON TABLE {qualified} IS {Literal(comment)};";
        return sql + Section("Indexes", indexes) + Section("Triggers", triggers);
    }

    private static async Task<string> SequenceAsync(NpgsqlConnection connection, uint oid, string qualified, CancellationToken token)
    {
        await using var command = new NpgsqlCommand("""
            SELECT format_type(s.seqtypid,NULL), s.seqincrement, s.seqmin, s.seqmax, s.seqstart, s.seqcache, s.seqcycle,
                   (SELECT quote_ident(n.nspname)||'.'||quote_ident(c.relname)||'.'||quote_ident(a.attname)
                    FROM pg_depend d JOIN pg_class c ON c.oid=d.refobjid
                    JOIN pg_namespace n ON n.oid=c.relnamespace
                    JOIN pg_attribute a ON a.attrelid=c.oid AND a.attnum=d.refobjsubid
                    WHERE d.classid='pg_class'::regclass AND d.objid=s.seqrelid
                      AND d.refclassid='pg_class'::regclass AND d.deptype IN ('a','i')
                    LIMIT 1)
            FROM pg_sequence s WHERE s.seqrelid=@oid
            """, connection);
        command.Parameters.AddWithValue("oid", (long)oid);
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false)) throw new InvalidOperationException("Sequence metadata is unavailable.");
        return $"CREATE SEQUENCE {qualified}\n    AS {reader.GetString(0)}\n    INCREMENT BY {reader.GetInt64(1)}\n    MINVALUE {reader.GetInt64(2)}\n    MAXVALUE {reader.GetInt64(3)}\n    START WITH {reader.GetInt64(4)}\n    CACHE {reader.GetInt64(5)}\n    {(reader.GetBoolean(6) ? "CYCLE" : "NO CYCLE")}"
            + (reader.IsDBNull(7) ? "" : $"\n    OWNED BY {reader.GetString(7)}") + ";";
    }

    private static async Task<ObjectScriptMetadata> RoutineAsync(NpgsqlConnection connection, PostgresObjectIdentity identity, CancellationToken token)
    {
        await using var command = new NpgsqlCommand("""
            SELECT n.nspname,p.proname,p.prokind,pg_get_function_identity_arguments(p.oid),
                   pg_get_functiondef(p.oid),obj_description(p.oid,'pg_proc'),
                   pg_get_userbyid(p.proowner),array_to_string(p.proacl,E'\n')
            FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace WHERE p.oid=@oid
            """, connection);
        command.Parameters.AddWithValue("oid", (long)identity.ObjectOid);
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false)) throw new InvalidOperationException("The selected routine no longer exists.");
        var schema=reader.GetString(0); var name=reader.GetString(1); var kind=reader.GetChar(2);
        var signature=reader.GetString(3); var create=reader.GetString(4);
        var qualified=$"{PostgreSqlIdentifierQuoter.Qualified(schema,name)}({signature})";
        return new(identity,schema,name,qualified,kind=='p'?"PROCEDURE":"FUNCTION",create,[],[],[],[],
            reader.IsDBNull(5)?null:reader.GetString(5), Owner: reader.GetString(6),
            Privileges: reader.IsDBNull(7)?null:reader.GetString(7));
    }

    private static async Task<ObjectScriptMetadata> RelationChildAsync(
        NpgsqlConnection connection, PostgresObjectIdentity identity, CancellationToken token)
    {
        var sql = identity.ObjectClass switch
        {
            PostgresObjectClass.Index => """
                SELECT n.nspname,c.relname,pg_get_indexdef(c.oid),
                       'DROP INDEX '||quote_ident(n.nspname)||'.'||quote_ident(c.relname)||';'
                FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE c.oid=@oid
                """,
            PostgresObjectClass.Constraint => """
                SELECT n.nspname,x.conname,
                       'ALTER TABLE '||quote_ident(n.nspname)||'.'||quote_ident(c.relname)||
                       ' ADD CONSTRAINT '||quote_ident(x.conname)||' '||pg_get_constraintdef(x.oid,true)||';',
                       'ALTER TABLE '||quote_ident(n.nspname)||'.'||quote_ident(c.relname)||
                       ' DROP CONSTRAINT '||quote_ident(x.conname)||';'
                FROM pg_constraint x JOIN pg_class c ON c.oid=x.conrelid
                JOIN pg_namespace n ON n.oid=c.relnamespace WHERE x.oid=@oid
                """,
            _ => """
                SELECT n.nspname,t.tgname,pg_get_triggerdef(t.oid,true)||';',
                       'DROP TRIGGER '||quote_ident(t.tgname)||' ON '||
                       quote_ident(n.nspname)||'.'||quote_ident(c.relname)||';'
                FROM pg_trigger t JOIN pg_class c ON c.oid=t.tgrelid
                JOIN pg_namespace n ON n.oid=c.relnamespace WHERE t.oid=@oid
                """,
        };
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("oid", (long)identity.ObjectOid);
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false))
            throw new InvalidOperationException("The selected object no longer exists.");
        var schema = reader.GetString(0); var name = reader.GetString(1);
        var create = reader.GetString(2); var drop = reader.GetString(3);
        var keyword = identity.ObjectClass.ToString().ToUpperInvariant();
        return new(identity, schema, name, PostgreSqlIdentifierQuoter.Qualified(schema, name),
            keyword, create, [], [], [], [], CanonicalDrop: drop);
    }

    private static async Task<ObjectScriptMetadata> TypeAsync(
        NpgsqlConnection connection, PostgresObjectIdentity identity, CancellationToken token)
    {
        await using var command = new NpgsqlCommand("""
            SELECT n.nspname,t.typname,t.typtype,format_type(t.typbasetype,t.typtypmod),
                   t.typnotnull,pg_get_expr(t.typdefaultbin,0),pg_get_userbyid(t.typowner),
                   array_to_string(t.typacl,E'\n')
            FROM pg_type t JOIN pg_namespace n ON n.oid=t.typnamespace WHERE t.oid=@oid
            """, connection);
        command.Parameters.AddWithValue("oid", (long)identity.ObjectOid);
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false))
            throw new InvalidOperationException("The selected type no longer exists.");
        var schema=reader.GetString(0); var name=reader.GetString(1); var kind=reader.GetChar(2);
        var baseType=reader.IsDBNull(3)?null:reader.GetString(3); var notNull=reader.GetBoolean(4);
        var defaultValue=reader.IsDBNull(5)?null:reader.GetString(5);
        var owner=reader.GetString(6); var privileges=reader.IsDBNull(7)?null:reader.GetString(7);
        await reader.DisposeAsync().ConfigureAwait(false);
        var qualified=PostgreSqlIdentifierQuoter.Qualified(schema,name);
        string create; string keyword;
        if(kind=='e')
        {
            var labels=await StringsAsync(connection,
                "SELECT enumlabel FROM pg_enum WHERE enumtypid=@oid ORDER BY enumsortorder",
                identity.ObjectOid,token).ConfigureAwait(false);
            keyword="TYPE"; create=$"CREATE TYPE {qualified} AS ENUM\n(\n    {string.Join(",\n    ",labels.Select(Literal))}\n);";
        }
        else if(kind=='d')
        {
            var domainConstraints = await StringsAsync(connection, """
                SELECT 'CONSTRAINT '||quote_ident(conname)||' '||pg_get_constraintdef(oid,true)
                FROM pg_constraint WHERE contypid=@oid ORDER BY conname
                """, identity.ObjectOid, token).ConfigureAwait(false);
            keyword="DOMAIN"; create=$"CREATE DOMAIN {qualified} AS {baseType}"
                +(defaultValue is null?"":$"\n    DEFAULT {defaultValue}")+(notNull?"\n    NOT NULL":"")+";";
            if (domainConstraints.Count > 0)
                create = create.TrimEnd(';') + "\n    " + string.Join("\n    ", domainConstraints) + ";";
        }
        else
        {
            keyword="TYPE";
            var attrs=await StringsAsync(connection,"""
                SELECT quote_ident(a.attname)||' '||format_type(a.atttypid,a.atttypmod)
                FROM pg_attribute a JOIN pg_type t ON t.typrelid=a.attrelid
                WHERE t.oid=@oid AND a.attnum>0 AND NOT a.attisdropped ORDER BY a.attnum
                """,identity.ObjectOid,token).ConfigureAwait(false);
            create=$"CREATE TYPE {qualified} AS\n(\n    {string.Join(",\n    ",attrs)}\n);";
        }
        return new(identity,schema,name,qualified,keyword,create,[],[],[],[],
            Owner: owner, Privileges: privileges);
    }

    private static async Task<ObjectScriptMetadata> ExtensionAsync(
        NpgsqlConnection connection, PostgresObjectIdentity identity, CancellationToken token)
    {
        await using var command=new NpgsqlCommand("""
            SELECT e.extname,n.nspname,e.extversion,obj_description(e.oid,'pg_extension')
            FROM pg_extension e JOIN pg_namespace n ON n.oid=e.extnamespace WHERE e.oid=@oid
            """,connection);
        command.Parameters.AddWithValue("oid",(long)identity.ObjectOid);
        await using var reader=await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        if(!await reader.ReadAsync(token).ConfigureAwait(false)) throw new InvalidOperationException("The selected extension no longer exists.");
        var name=reader.GetString(0); var schema=reader.GetString(1); var version=reader.GetString(2);
        var qualified=PostgreSqlIdentifierQuoter.Quote(name);
        var create=$"CREATE EXTENSION {qualified}\n    WITH SCHEMA {PostgreSqlIdentifierQuoter.Quote(schema)}\n    VERSION {Literal(version)};";
        return new(identity,schema,name,qualified,"EXTENSION",create,[],[],[],[],
            reader.IsDBNull(3)?null:reader.GetString(3));
    }

    private static Task<ObjectScriptMetadata> SchemaAsync(NpgsqlConnection connection, PostgresObjectIdentity identity, CancellationToken token) =>
        SimpleAsync(connection, identity, "SELECT nspname,pg_get_userbyid(nspowner),obj_description(oid,'pg_namespace') FROM pg_namespace WHERE oid=@oid",
            "SCHEMA", x => $"CREATE SCHEMA {PostgreSqlIdentifierQuoter.Quote(x.Name)} AUTHORIZATION {PostgreSqlIdentifierQuoter.Quote(x.Owner)};", token);

    private static Task<ObjectScriptMetadata> DatabaseAsync(NpgsqlConnection connection, PostgresObjectIdentity identity, CancellationToken token) =>
        SimpleAsync(connection, identity, "SELECT datname,pg_get_userbyid(datdba),NULL::text FROM pg_database WHERE oid=@oid",
            "DATABASE", x => $"CREATE DATABASE {PostgreSqlIdentifierQuoter.Quote(x.Name)} OWNER {PostgreSqlIdentifierQuoter.Quote(x.Owner)};", token);

    private static async Task<ObjectScriptMetadata> SimpleAsync(NpgsqlConnection connection, PostgresObjectIdentity identity,
        string sql, string keyword, Func<(string Name,string Owner),string> create, CancellationToken token)
    {
        await using var command=new NpgsqlCommand(sql,connection); command.Parameters.AddWithValue("oid",(long)identity.ObjectOid);
        await using var reader=await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        if(!await reader.ReadAsync(token).ConfigureAwait(false)) throw new InvalidOperationException("The selected object no longer exists.");
        var name=reader.GetString(0); var owner=reader.GetString(1);
        return new(identity,"",name,PostgreSqlIdentifierQuoter.Quote(name),keyword,create((name,owner)),[],[],[],[],
            reader.IsDBNull(2)?null:reader.GetString(2), Owner: owner);
    }

    private static async Task<IReadOnlyList<string>> StringsAsync(NpgsqlConnection connection,string sql,uint oid,CancellationToken token)
    {
        var list=new List<string>(); await using var command=new NpgsqlCommand(sql,connection);
        command.Parameters.AddWithValue("oid",(long)oid); await using var reader=await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while(await reader.ReadAsync(token).ConfigureAwait(false)) list.Add(reader.GetString(0)); return list;
    }
    private static string Section(string title,IReadOnlyList<string> values)=>values.Count==0?"":$"\n\n-- {title}\n\n{string.Join("\n",values)}";
    private static string Literal(string value)=>$"'{value.Replace("'","''")}'";

    private static async Task ValidateIdentityAsync(
        NpgsqlConnection connection, PostgresObjectIdentity identity, CancellationToken token)
    {
        await using var command = new NpgsqlCommand("""
            SELECT d.oid::bigint,
                   COALESCE(inet_server_addr()::text, 'local') || ':' ||
                   COALESCE(inet_server_port()::text, current_setting('port')) || ':' ||
                   current_setting('server_version_num')
            FROM pg_database d WHERE d.datname=current_database()
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false))
            throw new InvalidOperationException("The active database identity could not be verified.");
        var databaseOid = checked((uint)reader.GetInt64(0));
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(reader.GetString(1))));
        if (databaseOid != identity.DatabaseOid || fingerprint != identity.ServerFingerprint)
            throw new InvalidOperationException(
                "The selected object belongs to another server or database. Refresh Object Explorer before continuing.");
    }
}
