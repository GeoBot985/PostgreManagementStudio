using Npgsql;
using PostgreManagementStudio.Core;

namespace PostgreManagementStudio.Postgres;

public sealed class NpgsqlPostgresVersionQuery(INpgsqlConnectionFactory? connectionFactory = null) : IPostgresVersionQuery
{
    private readonly INpgsqlConnectionFactory _connections = connectionFactory ?? NpgsqlConnectionFactory.Shared;

    public async Task<string> ExecuteAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        await using var connection = _connections.Create(connectionString, "PostgreManagementStudio - Version Detection");
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT version();", connection);
        return (string?)await command.ExecuteScalarAsync(cancellationToken) ?? "No version returned.";
    }
}
