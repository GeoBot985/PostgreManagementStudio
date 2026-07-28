using Npgsql;
using PostgreManagementStudio.Application;

namespace PostgreManagementStudio.Postgres;

public sealed class NpgsqlExecutionPlanService
{
    public async Task<ExecutionPlanDocument> ExplainAsync(string connectionString, ExplainRequest request, CancellationToken cancellationToken = default)
    {
        var command = ExplainCommandBuilder.Build(request); var builder = new NpgsqlConnectionStringBuilder(connectionString) { ApplicationName = "PostgreManagementStudio - Execution Plan" }; await using var connection = new NpgsqlConnection(builder.ConnectionString); await connection.OpenAsync(cancellationToken); await using var sql = new NpgsqlCommand(command.Sql, connection); if (request.Options.StatementTimeout is { } timeout) { await using var setting = new NpgsqlCommand("SET statement_timeout = @timeout", connection); setting.Parameters.AddWithValue("timeout", $"{timeout.TotalMilliseconds}ms"); await setting.ExecuteNonQueryAsync(cancellationToken); sql.CommandTimeout = Math.Max(1, (int)timeout.TotalSeconds); } var raw = Convert.ToString(await sql.ExecuteScalarAsync(cancellationToken)) ?? throw new InvalidOperationException("PostgreSQL returned an empty execution plan."); return ExecutionPlanParser.Parse(request.Sql, raw, request.Options.Type);
    }
}
