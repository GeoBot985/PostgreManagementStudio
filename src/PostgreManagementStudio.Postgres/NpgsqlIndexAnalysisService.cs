using Npgsql;
using PostgreManagementStudio.Application;
using PostgreManagementStudio.Core;

namespace PostgreManagementStudio.Postgres;

public sealed class NpgsqlIndexAnalysisService(INpgsqlConnectionFactory? connectionFactory = null)
{
    private readonly INpgsqlConnectionFactory _connections = connectionFactory ?? NpgsqlConnectionFactory.Shared;

    public async Task<IndexAnalysisSnapshot> LoadAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        await using var connection = _connections.Create(connectionString, "PostgreManagementStudio - Index Analysis");
        await connection.OpenAsync(cancellationToken);
        var indexes = new List<IndexMetadata>();
        await using (var command = new NpgsqlCommand("""
            SELECT i.indexrelid::bigint, i.indrelid::bigint, ns.nspname, tbl.relname,
                   idx.relname, am.amname, pg_get_indexdef(i.indexrelid),
                   i.indisunique, i.indisprimary, i.indisvalid, i.indisready, i.indislive,
                   pg_relation_size(i.indexrelid), COALESCE(s.idx_scan, 0),
                   EXISTS (SELECT 1 FROM pg_constraint c WHERE c.conindid = i.indexrelid),
                   EXISTS (SELECT 1 FROM pg_index ri WHERE ri.indrelid = i.indrelid AND ri.indisreplident AND ri.indexrelid = i.indexrelid)
            FROM pg_index i
            JOIN pg_class idx ON idx.oid = i.indexrelid
            JOIN pg_class tbl ON tbl.oid = i.indrelid
            JOIN pg_namespace ns ON ns.oid = tbl.relnamespace
            JOIN pg_am am ON am.oid = idx.relam
            LEFT JOIN pg_stat_all_indexes s ON s.indexrelid = i.indexrelid
            WHERE ns.nspname NOT LIKE 'pg_%' AND ns.nspname <> 'information_schema'
              AND (has_table_privilege(tbl.oid, 'SELECT') OR pg_has_role(tbl.relowner, 'USAGE'))
            ORDER BY ns.nspname COLLATE "C", tbl.relname COLLATE "C", idx.relname COLLATE "C"
            """, connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var definition = reader.GetString(6);
                indexes.Add(new(
                    checked((uint)reader.GetInt64(0)), checked((uint)reader.GetInt64(1)), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5),
                    new[] { new IndexKeyDefinition(definition) }, Array.Empty<string>(), null, reader.GetBoolean(7), reader.GetBoolean(8), reader.GetBoolean(9), reader.GetBoolean(10), reader.GetBoolean(11),
                    reader.GetInt64(12), reader.GetInt64(13), reader.GetBoolean(14), reader.GetBoolean(15)));
            }
        }
        return new(DateTimeOffset.UtcNow, null, indexes, Array.Empty<ForeignKeyMetadata>());
    }
}
