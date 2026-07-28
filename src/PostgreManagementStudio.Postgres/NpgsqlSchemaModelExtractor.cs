using Npgsql;
using PostgreManagementStudio.Application;

namespace PostgreManagementStudio.Postgres;

public sealed class NpgsqlSchemaModelExtractor(INpgsqlConnectionFactory? connectionFactory = null)
{
    private readonly INpgsqlConnectionFactory _connections = connectionFactory ?? NpgsqlConnectionFactory.Shared;

    public async Task<SchemaModel> ExtractAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        await using var connection = _connections.Create(connectionString, "PostgreManagementStudio - Schema Extractor"); await connection.OpenAsync(cancellationToken); var version = await ReadVersion(connection, cancellationToken); var objects = new List<SchemaObject>(); const string sql = "SELECT n.nspname,c.relname,c.relkind,pg_get_expr(c.relpartbound,c.oid),c.oid::text FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname NOT LIKE 'pg_%' AND n.nspname <> 'information_schema' AND c.relkind IN ('r','p','v','m','S') ORDER BY n.nspname,c.relname"; await using var command = new NpgsqlCommand(sql, connection); await using var reader = await command.ExecuteReaderAsync(cancellationToken); while (await reader.ReadAsync(cancellationToken)) { var schema = reader.GetString(0); var name = reader.GetString(1); var kind = reader.GetChar(2) switch { 'v' => SchemaObjectKind.View, 'm' => SchemaObjectKind.MaterializedView, 'S' => SchemaObjectKind.Sequence, _ => SchemaObjectKind.Table }; objects.Add(new($"{schema}.{name}:{kind}", kind, schema, name, null, $"CREATE {kind.ToString().ToUpperInvariant()} {PostgreSqlIdentifierQuoter.Qualified(schema, name)}", new Dictionary<string, string>())); } return new(connectionString.GetHashCode().ToString(), connection.Database, version, objects, Array.Empty<string>());
    }
    private static async Task<int> ReadVersion(NpgsqlConnection connection, CancellationToken token) { await using var command = new NpgsqlCommand("SHOW server_version_num", connection); var value = Convert.ToInt32(await command.ExecuteScalarAsync(token)); return value / 10000; }
}
