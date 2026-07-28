using Npgsql;
using PostgreManagementStudio.Core;
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

    [Fact]
    public async Task ExecutorConvertsConnectionConstructionFailureWithoutLeakingSecretOrHanging()
    {
        var events = new List<QueryExecutionEvent>();
        await foreach (var item in new NpgsqlQueryExecutor(new ThrowingFactory())
            .ExecuteAsync(new QueryRequest("SELECT 1", "Password=must-not-leak")))
            events.Add(item);
        var failure = Assert.Single(events.OfType<ExecutionFailed>());
        Assert.Equal(DatabaseErrorKind.Provider, failure.Error.Kind);
        Assert.DoesNotContain("must-not-leak", failure.Error.Message);
    }

    private sealed class ThrowingFactory : INpgsqlConnectionFactory
    {
        public NpgsqlConnection Create(string connectionString, string applicationName)
            => throw new ArgumentException($"bad {connectionString}");
        public NpgsqlConnection Create(EffectiveConnectionConfiguration configuration)
            => throw new ArgumentException($"bad {configuration.Profile.Id}");
        public void ClearPool(EffectiveConnectionConfiguration configuration) { }
    }
}
