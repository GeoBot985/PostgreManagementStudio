using Npgsql;
using PostgreManagementStudio.Application;
using PostgreManagementStudio.Postgres;

namespace PostgreManagementStudio.IntegrationTests;

public sealed class BackupRestoreHardeningIntegrationTests
{
    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("PMS_CONNECTION_STRING")
        ?? throw new InvalidOperationException("PMS_CONNECTION_STRING is required.");

    private static string AdminConnectionString =>
        Environment.GetEnvironmentVariable("PMS_ADMIN_CONNECTION_STRING")
        ?? throw new InvalidOperationException("PMS_ADMIN_CONNECTION_STRING is required.");

    [SeededPostgreSqlFact]
    [Trait("Category", "Integration")]
    [Trait("Priority", "P0")]
    public async Task PlainAndCustomBackupsInspectAndCustomRestoreRevalidatesTarget()
    {
        var root = Path.Combine(Path.GetTempPath(), "pms-s39-live-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var targetDatabase = "pms_s39_restore_" + Guid.NewGuid().ToString("N")[..12];
        var plainTargetDatabase = "pms_s39_plain_" + Guid.NewGuid().ToString("N")[..12];
        try
        {
            var runner = new ExternalProcessRunner();
            var locator = new PostgreSqlToolLocator();
            var discovery = new PostgreSqlToolDiscoveryService(locator, runner);
            var tools = await discovery.DiscoverAsync(
                Environment.GetEnvironmentVariable("PMS_TEST_PG_BIN"));
            var validator = new NpgsqlBackupRestoreConnectionValidator();
            var inspection = new BackupInspectionService(runner);
            var service = new BackupRestoreOperationService(runner, validator, inspection,
                new BackupOperationLockManager());
            var source = DatabaseConnection.FromConnectionString(ConnectionString);

            var customPath = Path.Combine(root, "unicode Résumé custom.backup");
            var customPlan = BackupOperationPlanFactory.CreateBackup("integration", source.Host,
                new(source, customPath, BackupFormat.Custom, Verbose: true),
                tools, tools.PgDump.Major);
            await using (var controller = new BackupRestoreOperationController())
            {
                var result = await service.ExecuteBackupAsync(customPlan, controller);
                Assert.Contains(result!.State, new[]
                {
                    BackupRestoreOperationState.Completed,
                    BackupRestoreOperationState.CompletedWithWarnings,
                });
                Assert.True(result.ValidationSucceeded);
                Assert.True(result.VerificationSucceeded);
                Assert.True(result.AtomicCommit);
                Assert.True(new FileInfo(customPath).Length > 0);
            }

            var customInspection = await inspection.InspectAsync(customPath, BackupFormat.Custom, tools.Paths);
            Assert.True(customInspection.IsValid, customInspection.Warning);
            Assert.True(customInspection.ObjectCount > 0);
            Assert.Equal(source.Database, customInspection.SourceDatabase);
            Assert.NotNull(customInspection.ServerVersion);

            var plainPath = Path.Combine(root, "plain dump.sql");
            var plainPlan = BackupOperationPlanFactory.CreateBackup("integration", source.Host,
                new(source, plainPath, BackupFormat.PlainSql, SchemaOnly: true),
                tools, tools.PgDump.Major);
            await using (var controller = new BackupRestoreOperationController())
            {
                var result = await service.ExecuteBackupAsync(plainPlan, controller);
                Assert.Contains(result!.State, new[]
                {
                    BackupRestoreOperationState.Completed,
                    BackupRestoreOperationState.CompletedWithWarnings,
                });
                Assert.Equal(BackupFormat.PlainSql,
                    BackupInspectionService.DetectFormat(plainPath));
            }

            await CreateDatabaseAsync(targetDatabase,
                new NpgsqlConnectionStringBuilder(ConnectionString).Username!);
            var targetBuilder = new NpgsqlConnectionStringBuilder(ConnectionString)
            {
                Database = targetDatabase,
            };
            var target = DatabaseConnection.FromConnectionString(targetBuilder.ConnectionString);
            var restorePlan = BackupOperationPlanFactory.CreateRestore("integration", source.Host,
                new(target, customPath, BackupFormat.Custom, NoOwner: true,
                    NoPrivileges: true, SingleTransaction: true),
                customInspection, tools, tools.PgRestore.Major);
            await using (var controller = new BackupRestoreOperationController())
            {
                var result = await service.ExecuteRestoreAsync(restorePlan,
                    RestoreConfirmation.Create(restorePlan), controller);
                Assert.Contains(result!.State, new[]
                {
                    BackupRestoreOperationState.Completed,
                    BackupRestoreOperationState.CompletedWithWarnings,
                });
                Assert.True(result.ValidationSucceeded);
                Assert.True(result.VerificationSucceeded);
                Assert.False(result.TargetMayBePartiallyModified);
            }

            await using var restored = NpgsqlConnectionFactory.Shared.Create(
                targetBuilder.ConnectionString, "PostgreManagementStudio Sprint 39 Verification");
            await restored.OpenAsync();
            await using var command = new NpgsqlCommand(
                """SELECT count(*) FROM "PMS Regression"."Type Matrix" """, restored);
            Assert.Equal(1L, await command.ExecuteScalarAsync());

            await CreateDatabaseAsync(plainTargetDatabase,
                new NpgsqlConnectionStringBuilder(ConnectionString).Username!);
            var plainTargetBuilder = new NpgsqlConnectionStringBuilder(ConnectionString)
            {
                Database = plainTargetDatabase,
            };
            var plainInspection = await inspection.InspectAsync(
                plainPath, BackupFormat.PlainSql, tools.Paths);
            var plainRestorePlan = BackupOperationPlanFactory.CreateRestore("integration", source.Host,
                new(DatabaseConnection.FromConnectionString(plainTargetBuilder.ConnectionString),
                    plainPath, BackupFormat.PlainSql, SingleTransaction: true),
                plainInspection, tools, tools.Psql.Major);
            await using (var controller = new BackupRestoreOperationController())
            {
                var result = await service.ExecuteRestoreAsync(plainRestorePlan,
                    RestoreConfirmation.Create(plainRestorePlan), controller);
                Assert.Contains(result!.State, new[]
                {
                    BackupRestoreOperationState.Completed,
                    BackupRestoreOperationState.CompletedWithWarnings,
                });
                Assert.True(result.VerificationSucceeded);
            }
            await using var plainRestored = NpgsqlConnectionFactory.Shared.Create(
                plainTargetBuilder.ConnectionString, "PostgreManagementStudio Sprint 39 Plain Verification");
            await plainRestored.OpenAsync();
            await using var tableCheck = new NpgsqlCommand(
                """SELECT to_regclass('"PMS Regression"."Type Matrix"') IS NOT NULL""", plainRestored);
            Assert.True((bool)(await tableCheck.ExecuteScalarAsync() ?? false));
        }
        finally
        {
            await DropDatabaseAsync(plainTargetDatabase);
            await DropDatabaseAsync(targetDatabase);
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [SeededPostgreSqlFact]
    [Trait("Category", "Integration")]
    [Trait("Priority", "P1")]
    public async Task InvalidCredentialsMissingDatabaseAndCorruptInputFailBeforeProcessStart()
    {
        var validator = new NpgsqlBackupRestoreConnectionValidator();
        var valid = DatabaseConnection.FromConnectionString(ConnectionString);
        var authentication = await validator.ValidateAsync(valid with { Password = "incorrect" }, true);
        Assert.False(authentication.Succeeded);
        Assert.DoesNotContain("incorrect", authentication.Message);

        var missing = await validator.ValidateAsync(
            valid with { Database = "pms_missing_" + Guid.NewGuid().ToString("N") }, true);
        Assert.False(missing.Succeeded);

        var corrupt = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(corrupt, [1, 2, 3, 4]);
            Assert.Null(BackupInspectionService.DetectFormat(corrupt));
        }
        finally { File.Delete(corrupt); }
    }

    private static async Task CreateDatabaseAsync(string database, string owner)
    {
        var builder = new NpgsqlConnectionStringBuilder(AdminConnectionString) { Database = "postgres" };
        await using var connection = NpgsqlConnectionFactory.Shared.Create(
            builder.ConnectionString, "PostgreManagementStudio Sprint 39 Setup");
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"CREATE DATABASE \"{database}\" OWNER \"{owner.Replace("\"", "\"\"", StringComparison.Ordinal)}\"",
            connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task DropDatabaseAsync(string database)
    {
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(AdminConnectionString) { Database = "postgres" };
            await using var connection = NpgsqlConnectionFactory.Shared.Create(
                builder.ConnectionString, "PostgreManagementStudio Sprint 39 Cleanup");
            await connection.OpenAsync();
            await using var terminate = new NpgsqlCommand(
                "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = @database AND pid <> pg_backend_pid()",
                connection);
            terminate.Parameters.AddWithValue("database", database);
            await terminate.ExecuteNonQueryAsync();
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{database}\"", connection);
            await drop.ExecuteNonQueryAsync();
        }
        catch { }
    }
}
