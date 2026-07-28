using Npgsql;
using PostgreManagementStudio.Core;

namespace PostgreManagementStudio.Postgres;

public sealed class NpgsqlPostgresVersionQuery : IPostgresVersionQuery
{
    public async Task<string> ExecuteAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT version();", connection);
        return (string?)await command.ExecuteScalarAsync(cancellationToken) ?? "No version returned.";
    }
}
