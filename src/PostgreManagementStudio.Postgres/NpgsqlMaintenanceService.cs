using System.Diagnostics;
using Npgsql;
using PostgreManagementStudio.Application;

namespace PostgreManagementStudio.Postgres;

public sealed class NpgsqlMaintenanceService(INpgsqlConnectionFactory? connectionFactory = null)
{
    private readonly INpgsqlConnectionFactory _connections = connectionFactory ?? NpgsqlConnectionFactory.Shared;

    public async Task<MaintenanceExecutionResult> ExecuteAsync(string connectionString, MaintenancePlan plan, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var statements = plan.Statements; var results = new List<MaintenanceTargetResult>(); var messages = new List<string>(); var started = Stopwatch.StartNew(); await using var connection = _connections.Create(connectionString, "PostgreManagementStudio - Maintenance"); connection.Notice += (_, e) => { messages.Add(e.Notice.MessageText); progress?.Report(e.Notice.MessageText); }; await connection.OpenAsync(cancellationToken);
        try { if (plan.Options.StatementTimeout is { } statement) await SetTimeout(connection, "statement_timeout", statement, cancellationToken); if (plan.Options.LockTimeout is { } lockTimeout) await SetTimeout(connection, "lock_timeout", lockTimeout, cancellationToken); for (var i = 0; i < statements.Count; i++) { cancellationToken.ThrowIfCancellationRequested(); progress?.Report($"Running {i + 1}/{statements.Count}: {statements[i]}"); var targetStarted = Stopwatch.StartNew(); try { await using var command = new NpgsqlCommand(statements[i], connection); command.CommandTimeout = plan.Options.StatementTimeout is { } t ? Math.Max(1, (int)t.TotalSeconds) : 0; await command.ExecuteNonQueryAsync(cancellationToken); results.Add(new(plan.Targets[i], true, null, targetStarted.Elapsed)); } catch (Exception ex) when (ex is PostgresException or NpgsqlException or TimeoutException) { results.Add(new(plan.Targets[i], false, Sanitize(ex.Message), targetStarted.Elapsed)); } } } catch (OperationCanceledException) { return new("Cancelled", results, messages, started.Elapsed, true); } return new(results.All(x => x.Succeeded) ? "Completed" : "Partially completed", results, messages, started.Elapsed, false);
    }
    private static async Task SetTimeout(NpgsqlConnection connection, string name, TimeSpan value, CancellationToken token) { if (value <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(value)); await using var command = new NpgsqlCommand($"SET {name} = @value", connection); command.Parameters.AddWithValue("value", $"{Math.Ceiling(value.TotalMilliseconds)}ms"); await command.ExecuteNonQueryAsync(token); }
    private static string Sanitize(string message) => PostgreManagementStudio.Core.SensitiveDataRedactor.Redact(message);
}
