using Microsoft.Extensions.Logging;
using PostgreManagementStudio.Core;
using PostgreManagementStudio.Results;

namespace PostgreManagementStudio.Application;

/// <summary>
/// Application-layer orchestration that runs a <see cref="QueryRequest"/> through
/// an <see cref="IQueryExecutor"/> and materialises the results into a fully
/// retained <see cref="IResultSession"/> via the Sprint 002 result-store layer.
/// </summary>
public sealed class ResultExecutionService
{
    private readonly IResultSessionBuilder _builder;
    private readonly IQueryExecutor _executor;

    public ResultExecutionService(IQueryExecutor executor, ResultStorageOptions? options = null, ILogger<ResultSessionBuilder>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(executor);
        _executor = executor;
        _builder = new ResultSessionBuilder(executor, options, logger);
    }

    /// <summary>
    /// Executes the request and returns the populated session. The caller owns
    /// disposal of the returned <see cref="IResultSession"/>.
    /// </summary>
    public Task<IResultSession> ExecuteAndBuildAsync(QueryRequest request, CancellationToken cancellationToken)
        => _builder.ExecuteAndBuildAsync(request, cancellationToken);

    public ValueTask CloseExecutionScopeAsync(Guid executionScopeId)
        => _executor is IQueryExecutionScopeManager scopes ? scopes.CloseScopeAsync(executionScopeId) : ValueTask.CompletedTask;
}
