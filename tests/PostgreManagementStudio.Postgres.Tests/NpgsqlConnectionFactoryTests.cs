using Npgsql;
using PostgreManagementStudio.Postgres;

namespace PostgreManagementStudio.Postgres.Tests;

public sealed class NpgsqlConnectionFactoryTests
{
    [Fact]
    public async Task FactoryCreatesClosedOwnedConnectionWithStableApplicationName()
    {
        await using var connection = NpgsqlConnectionFactory.Shared.Create(
            "Host=localhost;Database=postgres;Username=test;Password=secret",
            "PostgreManagementStudio - Test");

        Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
        var settings = new NpgsqlConnectionStringBuilder(connection.ConnectionString);
        Assert.Equal("PostgreManagementStudio - Test", settings.ApplicationName);
        Assert.Equal("secret", settings.Password);
    }

    [Fact]
    public void FactoryRejectsMissingInputs()
    {
        Assert.Throws<ArgumentException>(() => NpgsqlConnectionFactory.Shared.Create("", "test"));
        Assert.Throws<ArgumentException>(() => NpgsqlConnectionFactory.Shared.Create("Host=localhost", ""));
    }
}
