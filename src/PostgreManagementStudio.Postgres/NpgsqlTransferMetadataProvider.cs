using Npgsql;
using NpgsqlTypes;
using PostgreManagementStudio.Application;
using PostgreManagementStudio.Core;

namespace PostgreManagementStudio.Postgres;

public sealed class NpgsqlTransferMetadataProvider(
    INpgsqlConnectionFactory? connectionFactory = null) : ITransferMetadataProvider
{
    private readonly INpgsqlConnectionFactory _connections =
        connectionFactory ?? NpgsqlConnectionFactory.Shared;

    public async Task<TransferDestinationMetadata> LoadAsync(
        string connectionString,
        string database,
        string? schema = null,
        string? relation = null,
        CancellationToken cancellationToken = default)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString) { Database = database };
        await using var connection = _connections.Create(
            builder.ConnectionString, "PostgreManagementStudio - Transfer metadata");
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var schemas = new List<string>();
        await using (var command = new NpgsqlCommand(
            """
            SELECT nspname
            FROM pg_namespace
            WHERE nspname NOT LIKE 'pg\_%' ESCAPE '\' AND nspname <> 'information_schema'
              AND has_schema_privilege(oid, 'USAGE')
            ORDER BY nspname
            """, connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                schemas.Add(reader.GetString(0));

        var relations = new List<TransferRelationSource>();
        await using (var command = new NpgsqlCommand(
            """
            SELECT n.nspname,c.relname,
                   CASE c.relkind WHEN 'r' THEN 'Table' WHEN 'p' THEN 'Partitioned table'
                     WHEN 'f' THEN 'Foreign table' WHEN 'v' THEN 'View'
                     WHEN 'm' THEN 'Materialized view' END,
                   c.relkind IN ('r','p','f') AND has_table_privilege(c.oid,'INSERT'),
                   has_table_privilege(c.oid,'SELECT')
            FROM pg_class c
            JOIN pg_namespace n ON n.oid=c.relnamespace
            WHERE c.relkind IN ('r','p','f','v','m')
              AND n.nspname NOT LIKE 'pg\_%' ESCAPE '\'
              AND (@schema IS NULL OR n.nspname=@schema)
            ORDER BY n.nspname,c.relname
            """, connection))
        {
            command.Parameters.Add("schema", NpgsqlDbType.Text).Value =
                (object?)schema ?? DBNull.Value;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var relationSchema = reader.GetString(0);
                var name = reader.GetString(1);
                relations.Add(new(relationSchema, name, reader.GetString(2),
                    reader.GetBoolean(3), reader.GetBoolean(4),
                    PostgreSqlIdentifierQuoter.Qualified(relationSchema, name)));
            }
        }

        var columns = new List<DestinationColumn>();
        if (!string.IsNullOrWhiteSpace(schema) && !string.IsNullOrWhiteSpace(relation))
        {
            await using var command = new NpgsqlCommand(
                """
                SELECT a.attname,format_type(a.atttypid,a.atttypmod),NOT a.attnotnull,
                       a.attgenerated<>'',d.adbin IS NOT NULL,
                       a.attidentity='a',
                       EXISTS(SELECT 1 FROM pg_constraint x
                              WHERE x.conrelid=a.attrelid AND x.contype='p'
                                AND a.attnum=ANY(x.conkey)),
                       CASE WHEN d.adbin IS NULL THEN NULL ELSE pg_get_expr(d.adbin,d.adrelid) END,
                       col_description(a.attrelid,a.attnum)
                FROM pg_attribute a
                JOIN pg_class c ON c.oid=a.attrelid
                JOIN pg_namespace n ON n.oid=c.relnamespace
                LEFT JOIN pg_attrdef d ON d.adrelid=a.attrelid AND d.adnum=a.attnum
                WHERE n.nspname=@schema AND c.relname=@relation
                  AND a.attnum>0 AND NOT a.attisdropped
                ORDER BY a.attnum
                """, connection);
            command.Parameters.AddWithValue("schema", schema);
            command.Parameters.AddWithValue("relation", relation);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                columns.Add(new(reader.GetString(0), reader.GetString(1), reader.GetBoolean(2),
                    reader.GetBoolean(3), reader.GetBoolean(4), reader.GetBoolean(5),
                    reader.GetBoolean(6), reader.IsDBNull(7) ? null : reader.GetString(7),
                    reader.IsDBNull(8) ? null : reader.GetString(8)));
        }

        var hasCreate = false;
        var hasInsert = false;
        if (!string.IsNullOrWhiteSpace(schema))
        {
            await using var permission = new NpgsqlCommand(
                """
                SELECT has_schema_privilege(@schema,'CREATE'),
                       CASE WHEN @relation IS NULL THEN false
                            ELSE has_table_privilege(format('%I.%I',@schema,@relation),'INSERT') END
                """, connection);
            permission.Parameters.AddWithValue("schema", schema);
            permission.Parameters.Add("relation", NpgsqlDbType.Text).Value =
                (object?)relation ?? DBNull.Value;
            await using var reader = await permission.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                hasCreate = reader.GetBoolean(0);
                hasInsert = reader.GetBoolean(1);
            }
        }
        return new(schemas, relations, columns, hasCreate, hasInsert);
    }
}
