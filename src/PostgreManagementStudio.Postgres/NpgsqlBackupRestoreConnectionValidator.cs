using Npgsql;
using PostgreManagementStudio.Application;

namespace PostgreManagementStudio.Postgres;

public sealed class NpgsqlBackupRestoreConnectionValidator(
    INpgsqlConnectionFactory? connectionFactory = null) : IBackupRestoreConnectionValidator
{
    private readonly INpgsqlConnectionFactory _connections =
        connectionFactory ?? NpgsqlConnectionFactory.Shared;
    public async Task<BackupRestoreValidationResult> ValidateAsync(
        DatabaseConnection connection,
        bool databaseMustExist,
        CancellationToken cancellationToken = default)
    {
        BackupPlanValidator.ValidateConnection(connection);
        try
        {
            var builder = CreateBuilder(connection);
            if (!databaseMustExist)
            {
                var target = connection.Database;
                builder.Database = "postgres";
                await using var maintenance = _connections.Create(
                    builder.ConnectionString, "PostgreManagementStudio.BackupRestoreValidation");
                await maintenance.OpenAsync(cancellationToken).ConfigureAwait(false);
                await using var exists = new NpgsqlCommand(
                    "SELECT EXISTS (SELECT 1 FROM pg_catalog.pg_database WHERE datname = @database)",
                    maintenance);
                exists.Parameters.AddWithValue("database", target);
                var alreadyExists = (bool)(await exists.ExecuteScalarAsync(cancellationToken)
                    .ConfigureAwait(false) ?? false);
                var major = await ReadMajorAsync(maintenance, cancellationToken).ConfigureAwait(false);
                return alreadyExists
                    ? new(false, major, "The create-database restore target already exists.")
                    : new(true, major, "The server is reachable and the restore target does not exist.");
            }

            await using var targetConnection = _connections.Create(
                builder.ConnectionString, "PostgreManagementStudio.BackupRestoreValidation");
            await targetConnection.OpenAsync(cancellationToken).ConfigureAwait(false);
            var serverMajor = await ReadMajorAsync(targetConnection, cancellationToken).ConfigureAwait(false);
            return new(true, serverMajor, "The target database connection was validated.");
        }
        catch (OperationCanceledException) { throw; }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.InvalidCatalogName)
        {
            return new(false, null, "The target database does not exist.");
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.InvalidPassword)
        {
            return new(false, null, "PostgreSQL rejected the supplied credentials.");
        }
        catch (NpgsqlException ex)
        {
            return new(false, null, BackupSecretRedactor.Redact(ex.Message));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return new(false, null, BackupSecretRedactor.Redact(ex.Message));
        }
    }

    private static NpgsqlConnectionStringBuilder CreateBuilder(DatabaseConnection connection) => new()
    {
        Host = connection.Host,
        Port = connection.Port,
        Database = connection.Database,
        Username = connection.Username,
        Password = connection.Password,
        Pooling = false,
        Timeout = 10,
        CommandTimeout = 15,
        ApplicationName = "PostgreManagementStudio.BackupRestoreValidation",
    };

    private static async Task<int> ReadMajorAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SHOW server_version_num", connection);
        var value = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)
            .ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
        return value / 10_000;
    }
}
