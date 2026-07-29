using Npgsql;
using PostgreManagementStudio.Application;
using PostgreManagementStudio.Postgres;

namespace PostgreManagementStudio.IntegrationTests;

public sealed class SecurityHardeningIntegrationTests
{
    private static string ConnectionString => Environment.GetEnvironmentVariable("PMS_CONNECTION_STRING")!;

    [PostgreSqlFact]
    public async Task ProductionReadOnlyProfile_IsEnforcedByPostgreSqlNotOnlyTheUi()
    {
        var configuration = EffectiveConnectionConfigurationBuilder.FromConnectionString(
            "security-read-only", ConnectionString, "PMS security tests");
        configuration = EffectiveConnectionConfigurationBuilder.Build(configuration.Profile with
        {
            Environment = EnvironmentClassification.Production,
            IsReadOnly = false,
        });
        await using var connection = NpgsqlConnectionFactory.Shared.Create(configuration);
        await connection.OpenAsync();
        await using var status = new NpgsqlCommand("SHOW default_transaction_read_only", connection);
        Assert.Equal("on", await status.ExecuteScalarAsync());
        await using var write = new NpgsqlCommand("CREATE TEMP TABLE pms_should_be_blocked(id integer)", connection);
        var error = await Assert.ThrowsAsync<PostgresException>(() => write.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.ReadOnlySqlTransaction, error.SqlState);
    }

    [PostgreSqlFact]
    public async Task HostileIdentifier_RemainsOneQuotedObjectAndMetadataIsInert()
    {
        const string name = "quote\";--\n\u202Etable";
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        var quoted = PostgreSqlIdentifierQuoter.Qualified("public", name);
        try
        {
            await using (var create = new NpgsqlCommand($"CREATE TABLE {quoted}(id integer)", connection))
                await create.ExecuteNonQueryAsync();
            await using var find = new NpgsqlCommand("SELECT relname FROM pg_class WHERE relname=@name", connection);
            find.Parameters.AddWithValue("name", name);
            Assert.Equal(name, await find.ExecuteScalarAsync());
            var display = PostgreManagementStudio.Core.UntrustedText.ForDisplay(name);
            Assert.DoesNotContain('\n', display);
            Assert.DoesNotContain('\u202E', display);
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP TABLE IF EXISTS {quoted}", connection);
            await drop.ExecuteNonQueryAsync();
        }
    }

    [PostgreSqlFact]
    public async Task AuthenticationFailure_IsClassifiedWithoutCredentialDisclosure()
    {
        var builder = new NpgsqlConnectionStringBuilder(ConnectionString) { Password = "security-seed-never-log" };
        var configuration = EffectiveConnectionConfigurationBuilder.FromConnectionString(
            "bad-auth", builder.ConnectionString, "PMS security tests");
        var result = await new NpgsqlConnectionProbe(NpgsqlConnectionFactory.Shared).TestAsync(configuration);
        Assert.False(result.Succeeded);
        Assert.Equal(ConnectionFailureCategory.Authentication, result.FailureCategory);
        Assert.DoesNotContain("security-seed-never-log", result.Message);
    }
}
