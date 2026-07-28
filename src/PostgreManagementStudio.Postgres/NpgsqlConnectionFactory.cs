using Npgsql;

namespace PostgreManagementStudio.Postgres;

/// <summary>
/// The single production construction point for PostgreSQL connections.
/// Callers own and must dispose the returned, unopened connection.
/// </summary>
public interface INpgsqlConnectionFactory
{
    NpgsqlConnection Create(string connectionString, string applicationName);
}

public sealed class NpgsqlConnectionFactory : INpgsqlConnectionFactory
{
    public static NpgsqlConnectionFactory Shared { get; } = new();

    public NpgsqlConnection Create(string connectionString, string applicationName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationName);

        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            ApplicationName = applicationName,
        };

        return new NpgsqlConnection(builder.ConnectionString);
    }
}
