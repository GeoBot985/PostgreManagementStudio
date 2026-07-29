using Npgsql;
using System.Diagnostics.Metrics;

namespace PostgreManagementStudio.Postgres;

/// <summary>
/// The single production construction point for PostgreSQL connections.
/// Callers own and must dispose the returned, unopened connection.
/// </summary>
public interface INpgsqlConnectionFactory
{
    NpgsqlConnection Create(string connectionString, string applicationName);
    NpgsqlConnection Create(EffectiveConnectionConfiguration configuration);
    void ClearPool(EffectiveConnectionConfiguration configuration);
}

public sealed class NpgsqlConnectionFactory : INpgsqlConnectionFactory
{
    private static readonly Meter ResourceMeter = new(
        "PostgreManagementStudio.Postgres.Resources",
        typeof(NpgsqlConnectionFactory).Assembly.GetName().Version?.ToString());
    private static readonly Counter<long> ConnectionsCreated =
        ResourceMeter.CreateCounter<long>("pms.postgresql.connections.created");
    private static readonly Counter<long> PoolsCleared =
        ResourceMeter.CreateCounter<long>("pms.postgresql.pools.cleared");

    public static NpgsqlConnectionFactory Shared { get; } = new();

    public NpgsqlConnection Create(string connectionString, string applicationName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationName);
        var configuration = EffectiveConnectionConfigurationBuilder.FromConnectionString(
            $"raw:{applicationName}",
            connectionString,
            applicationName);
        return Create(configuration);
    }

    public NpgsqlConnection Create(EffectiveConnectionConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ConnectionsCreated.Add(1);
        return new NpgsqlConnection(configuration.ProviderConnectionString);
    }

    public void ClearPool(EffectiveConnectionConfiguration configuration)
    {
        using var connection = Create(configuration);
        NpgsqlConnection.ClearPool(connection);
        PoolsCleared.Add(1);
    }
}
