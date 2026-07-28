using Npgsql;
using PostgreManagementStudio.Application;

namespace PostgreManagementStudio.Postgres;

public sealed class NpgsqlSecurityService
{
    public async Task<IReadOnlyList<PostgreSqlRole>> LoadRolesAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        var roles = new List<PostgreSqlRole>(); await using var connection = new NpgsqlConnection(connectionString); await connection.OpenAsync(cancellationToken); await using var command = new NpgsqlCommand(SecurityMetadataQueries.Roles, connection); await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) roles.Add(new(reader.GetString(0), reader.GetBoolean(1), reader.GetBoolean(2), reader.GetBoolean(3), reader.GetBoolean(4), reader.GetBoolean(5), reader.GetBoolean(6), reader.GetBoolean(7), reader.GetInt32(8), reader.IsDBNull(9) ? null : reader.GetFieldValue<DateTimeOffset>(9), reader.IsDBNull(10) ? null : reader.GetString(10))); return roles;
    }
    public async Task<IReadOnlyList<RoleMembership>> LoadMembershipsAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        var memberships = new List<RoleMembership>(); await using var connection = new NpgsqlConnection(connectionString); await connection.OpenAsync(cancellationToken); await using var command = new NpgsqlCommand(SecurityMetadataQueries.Memberships, connection); await using var reader = await command.ExecuteReaderAsync(cancellationToken); while (await reader.ReadAsync(cancellationToken)) memberships.Add(new(reader.GetString(0), reader.GetString(1), reader.GetBoolean(2), false)); return memberships;
    }
    public async Task ExecuteAndVerifyAsync(string connectionString, SecuritySqlCommand change, string verificationSql, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(connectionString); await connection.OpenAsync(cancellationToken); await using var transaction = await connection.BeginTransactionAsync(cancellationToken); await using (var command = new NpgsqlCommand(change.Sql, connection, transaction)) { foreach (var parameter in change.Parameters) command.Parameters.AddWithValue(parameter.Key, parameter.Value ?? DBNull.Value); await command.ExecuteNonQueryAsync(cancellationToken); } await using (var verify = new NpgsqlCommand(verificationSql, connection, transaction)) await verify.ExecuteScalarAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
    }
}
