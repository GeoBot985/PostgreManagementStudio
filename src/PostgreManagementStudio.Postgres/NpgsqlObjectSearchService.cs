using System.Diagnostics;
using Npgsql;
using PostgreManagementStudio.Application;

namespace PostgreManagementStudio.Postgres;

public sealed class NpgsqlObjectSearchService
{
    public async Task<ObjectSearchBatch> SearchAsync(string connectionString, ObjectSearchOptions options, CancellationToken cancellationToken = default)
    {
        var watch = Stopwatch.StartNew(); var results = new List<ObjectSearchResult>(); var warnings = new List<string>(); var query = ObjectSearchQueryBuilder.Build(options); var builder = new NpgsqlConnectionStringBuilder(connectionString) { ApplicationName = "PostgreManagementStudio - Object Search" }; await using var connection = new NpgsqlConnection(builder.ConnectionString);
        try { await connection.OpenAsync(cancellationToken); await using var command = new NpgsqlCommand(query.Sql, connection); foreach (var parameter in query.Parameters) command.Parameters.AddWithValue(parameter.Key, parameter.Value ?? DBNull.Value); await using var reader = await command.ExecuteReaderAsync(cancellationToken); while (await reader.ReadAsync(cancellationToken)) { var type = Enum.TryParse<SearchObjectType>(reader.GetString(0), out var parsed) ? parsed : SearchObjectType.Table; results.Add(new(type, reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6))); } } catch (OperationCanceledException) { throw; } catch (PostgresException ex) { warnings.Add($"Database search failed: {ex.MessageText}"); } catch (NpgsqlException ex) { warnings.Add($"Database connection failed during search: {ex.Message}"); } return new(ObjectSearchResultUtilities.Deduplicate(results), warnings, results.Count >= options.MaximumResults, watch.Elapsed);
    }
}
