using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Npgsql;
using PostgreManagementStudio.Application;
using PostgreManagementStudio.Core;

namespace PostgreManagementStudio.Postgres;

public sealed class NpgsqlObjectSearchService(INpgsqlConnectionFactory? connectionFactory = null)
{
    private readonly INpgsqlConnectionFactory _connections = connectionFactory ?? NpgsqlConnectionFactory.Shared;

    public async Task<ObjectSearchBatch> SearchAsync(
        string connectionString,
        ObjectSearchOptions options,
        CancellationToken cancellationToken = default)
    {
        var watch = Stopwatch.StartNew();
        var results = new List<ObjectSearchResult>();
        var warnings = new List<string>();
        var query = ObjectSearchQueryBuilder.Build(options);
        await using var connection = _connections.Create(connectionString, "PostgreManagementStudio - Object Search");
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            var configurationIdentity = Hash(connectionString);
            await using var command = new NpgsqlCommand(query.Sql, connection);
            foreach (var parameter in query.Parameters)
                command.Parameters.AddWithValue(parameter.Key, parameter.Value ?? DBNull.Value);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var type = Enum.TryParse<SearchObjectType>(reader.GetString(0), out var parsed)
                    ? parsed : SearchObjectType.Table;
                var relationKind = reader.GetChar(11);
                var objectClass = relationKind switch
                {
                    'p' => PostgresObjectClass.PartitionedTable,
                    'r' when reader.GetBoolean(12) => PostgresObjectClass.Partition,
                    'v' => PostgresObjectClass.View,
                    'm' => PostgresObjectClass.MaterializedView,
                    'S' => PostgresObjectClass.Sequence,
                    'f' => PostgresObjectClass.ForeignTable,
                    'i' => PostgresObjectClass.Index,
                    _ => PostgresObjectClass.Table,
                };
                var objectOid = checked((uint)reader.GetInt64(7));
                var schemaOid = checked((uint)reader.GetInt64(8));
                var databaseOid = checked((uint)reader.GetInt64(9));
                var identity = new PostgresObjectIdentity
                {
                    ConnectionProfileId = "environment:PMS_CONNECTION_STRING",
                    ConfigurationIdentity = configurationIdentity,
                    ServerFingerprint = Hash(reader.GetString(10)),
                    DatabaseOid = databaseOid,
                    ObjectOid = objectOid,
                    ObjectClass = objectClass,
                    ParentOid = schemaOid,
                    SchemaOid = schemaOid,
                    NameSnapshot = reader.GetString(3),
                };
                results.Add(new(type, reader.GetString(1), reader.GetString(2), reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6), identity));
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (PostgresException ex)
        {
            warnings.Add($"Database search failed: {SecretRedactor.Redact(ex.MessageText)}");
        }
        catch (NpgsqlException ex)
        {
            warnings.Add($"Database connection failed during search: {SecretRedactor.Redact(ex.Message)}");
        }
        return new(ObjectSearchResultUtilities.Deduplicate(results), warnings,
            results.Count >= options.MaximumResults, watch.Elapsed);
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
