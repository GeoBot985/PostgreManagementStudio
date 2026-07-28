using Npgsql;
using PostgreManagementStudio.Postgres;

namespace PostgreManagementStudio.IntegrationTests;

public sealed class ConnectionManagementIntegrationTests
{
    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("PMS_CONNECTION_STRING") ??
        throw new InvalidOperationException("PMS_CONNECTION_STRING is required.");

    private static EffectiveConnectionConfiguration Configuration(
        string id,
        Action<NpgsqlConnectionStringBuilder>? configure = null)
    {
        var builder = new NpgsqlConnectionStringBuilder(ConnectionString);
        configure?.Invoke(builder);
        return EffectiveConnectionConfigurationBuilder.FromConnectionString(id, builder.ConnectionString, $"PostgreManagementStudio - {id}");
    }

    [PostgreSqlFact]
    public async Task ConnectionProbeAndLiveConnectionUseSameEffectiveIdentity()
    {
        var configuration = Configuration("Connection Probe");
        var result = await new NpgsqlConnectionProbe().TestAsync(configuration);
        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(configuration.Profile.Database, result.Database);
        Assert.Equal(configuration.Profile.Username, result.Username);
        Assert.Contains("PostgreSQL", result.ServerVersion);
        Assert.NotNull(result.IsEncrypted);
        Assert.Equal(configuration.Profile.SslMode is SslMode.VerifyCA or SslMode.VerifyFull && result.IsEncrypted == true, result.IsVerified);

        await using var connection = NpgsqlConnectionFactory.Shared.Create(configuration);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT current_database(), current_user, current_setting('application_name')", connection);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(result.Database, reader.GetString(0));
        Assert.Equal(result.Username, reader.GetString(1));
        Assert.Equal(configuration.Profile.ApplicationName, reader.GetString(2));
    }

    [PostgreSqlFact]
    public async Task AuthenticationRoleDatabaseAndSslFailuresAreDistinctAndSecretSafe()
    {
        const string attemptedPassword = "s37-password-must-not-leak";
        var wrongPassword = Configuration("Bad Password", builder =>
        {
            builder.Password = attemptedPassword;
            builder.Pooling = false;
            builder.Timeout = 2;
        });
        var authentication = await new NpgsqlConnectionProbe().TestAsync(wrongPassword);
        Assert.False(authentication.Succeeded);
        Assert.Equal(ConnectionFailureCategory.Authentication, authentication.FailureCategory);
        Assert.DoesNotContain(attemptedPassword, authentication.Message);

        var missingRole = Configuration("Missing Role", builder =>
        {
            builder.Username = "pms_missing_role_" + Guid.NewGuid().ToString("N");
            builder.Password = attemptedPassword;
            builder.Pooling = false;
            builder.Timeout = 2;
        });
        var role = await new NpgsqlConnectionProbe().TestAsync(missingRole);
        Assert.False(role.Succeeded);
        Assert.Equal(ConnectionFailureCategory.Authentication, role.FailureCategory);

        var missingDatabase = Configuration("Missing Database", builder =>
        {
            builder.Database = "pms_missing_database_" + Guid.NewGuid().ToString("N");
            builder.Pooling = false;
            builder.Timeout = 2;
        });
        var database = await new NpgsqlConnectionProbe().TestAsync(missingDatabase);
        Assert.False(database.Succeeded);
        Assert.Equal(ConnectionFailureCategory.DatabaseUnavailable, database.FailureCategory);

        var sslMismatch = Configuration("SSL Mismatch", builder =>
        {
            builder.Host = "127.0.0.1";
            builder.SslMode = SslMode.VerifyFull;
            builder.Pooling = false;
            builder.Timeout = 2;
        });
        var ssl = await new NpgsqlConnectionProbe().TestAsync(sslMismatch);
        Assert.False(ssl.Succeeded);
        Assert.Equal(ConnectionFailureCategory.Ssl, ssl.FailureCategory);
        Assert.Contains("SSL", ssl.Message, StringComparison.OrdinalIgnoreCase);
    }

    [PostgreSqlFact]
    public async Task ProviderPoolResetsSessionStateAbortedTransactionsTempObjectsAndPreparedStatements()
    {
        var configuration = Configuration("Session Reset", builder =>
        {
            builder.MaxPoolSize = 1;
            builder.MinPoolSize = 0;
            builder.NoResetOnClose = false;
        });

        int firstPid;
        string baselineSearchPath;
        string baselineTimezone;
        await using (var first = NpgsqlConnectionFactory.Shared.Create(configuration))
        {
            await first.OpenAsync();
            firstPid = first.ProcessID;
            baselineSearchPath = (string)(await new NpgsqlCommand("SHOW search_path", first).ExecuteScalarAsync())!;
            baselineTimezone = (string)(await new NpgsqlCommand("SHOW TimeZone", first).ExecuteScalarAsync())!;
            await new NpgsqlCommand("""
                SET search_path = pg_catalog;
                SET TIME ZONE 'Pacific/Auckland';
                SET ROLE NONE;
                CREATE TEMP TABLE pms_session_leak(id integer);
                PREPARE pms_prepared_leak AS SELECT 1;
                BEGIN;
                """, first).ExecuteNonQueryAsync();
            var failure = await Assert.ThrowsAsync<PostgresException>(() =>
                new NpgsqlCommand("SELECT 1 / 0", first).ExecuteScalarAsync());
            Assert.Equal("22012", failure.SqlState);
        }

        await using (var second = NpgsqlConnectionFactory.Shared.Create(configuration))
        {
            await second.OpenAsync();
            Assert.Equal(firstPid, second.ProcessID);
            Assert.Equal(baselineSearchPath, await new NpgsqlCommand("SHOW search_path", second).ExecuteScalarAsync());
            Assert.Equal(baselineTimezone, await new NpgsqlCommand("SHOW TimeZone", second).ExecuteScalarAsync());
            Assert.Equal(configuration.Profile.Username, await new NpgsqlCommand("SELECT current_role", second).ExecuteScalarAsync());
            Assert.True((bool)(await new NpgsqlCommand("SELECT to_regclass('pg_temp.pms_session_leak') IS NULL", second).ExecuteScalarAsync())!);
            Assert.Equal(0L, await new NpgsqlCommand("SELECT count(*) FROM pg_prepared_statements WHERE name='pms_prepared_leak'", second).ExecuteScalarAsync());
            Assert.Equal(42, await new NpgsqlCommand("SELECT 42", second).ExecuteScalarAsync());
        }
        NpgsqlConnectionFactory.Shared.ClearPool(configuration);
    }

    [PostgreSqlFact]
    public async Task PoolExhaustionWaitIsCancellableAndPoolRecovers()
    {
        var configuration = Configuration("Pool Exhaustion", builder =>
        {
            builder.MaxPoolSize = 2;
            builder.MinPoolSize = 0;
            builder.Timeout = 5;
        });
        await using var first = NpgsqlConnectionFactory.Shared.Create(configuration);
        await using var second = NpgsqlConnectionFactory.Shared.Create(configuration);
        await first.OpenAsync();
        await second.OpenAsync();
        await using var waiting = NpgsqlConnectionFactory.Shared.Create(configuration);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
        var exception = await Assert.ThrowsAnyAsync<Exception>(() => waiting.OpenAsync(cancellation.Token));
        Assert.Contains(ConnectionFailureClassifier.Classify(exception), new[] { ConnectionFailureCategory.Cancelled, ConnectionFailureCategory.PoolExhausted, ConnectionFailureCategory.Timeout });

        await first.CloseAsync();
        await waiting.OpenAsync();
        Assert.Equal(System.Data.ConnectionState.Open, waiting.State);
        NpgsqlConnectionFactory.Shared.ClearPool(configuration);
    }

    [PostgreSqlFact]
    public async Task BrokenBackendIsNotReusedAndConcurrentGrowthRemainsBounded()
    {
        var configuration = Configuration("Broken and Concurrent", builder =>
        {
            builder.MaxPoolSize = 10;
            builder.MinPoolSize = 0;
        });
        int brokenPid;
        await using (var victim = NpgsqlConnectionFactory.Shared.Create(configuration))
        {
            await victim.OpenAsync();
            brokenPid = victim.ProcessID;
            await using var terminator = new NpgsqlConnection(ConnectionString);
            await terminator.OpenAsync();
            await new NpgsqlCommand("SELECT pg_terminate_backend(@pid)", terminator)
            { Parameters = { new("pid", brokenPid) } }.ExecuteScalarAsync();
            await Assert.ThrowsAnyAsync<NpgsqlException>(() => new NpgsqlCommand("SELECT 1", victim).ExecuteScalarAsync());
        }

        await using (var recovered = NpgsqlConnectionFactory.Shared.Create(configuration))
        {
            await recovered.OpenAsync();
            Assert.NotEqual(brokenPid, recovered.ProcessID);
            Assert.Equal(42, await new NpgsqlCommand("SELECT 42", recovered).ExecuteScalarAsync());
        }

        var active = 0;
        var peak = 0;
        var tasks = Enumerable.Range(0, 20).Select(async _ =>
        {
            await using var connection = NpgsqlConnectionFactory.Shared.Create(configuration);
            await connection.OpenAsync();
            var now = Interlocked.Increment(ref active);
            InterlockedExtensions.Max(ref peak, now);
            try { await new NpgsqlCommand("SELECT pg_sleep(0.05)", connection).ExecuteNonQueryAsync(); }
            finally { Interlocked.Decrement(ref active); }
        });
        await Task.WhenAll(tasks);
        Assert.InRange(peak, 1, 10);
        NpgsqlConnectionFactory.Shared.ClearPool(configuration);
    }

    private static class InterlockedExtensions
    {
        public static void Max(ref int target, int value)
        {
            var current = Volatile.Read(ref target);
            while (value > current)
            {
                var observed = Interlocked.CompareExchange(ref target, value, current);
                if (observed == current) return;
                current = observed;
            }
        }
    }
}
