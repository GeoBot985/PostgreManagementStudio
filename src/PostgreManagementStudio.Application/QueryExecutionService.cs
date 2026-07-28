using PostgreManagementStudio.Core;

namespace PostgreManagementStudio.Application;

public sealed class QueryExecutionService(IQueryExecutor executor)
{
    public IAsyncEnumerable<QueryExecutionEvent> ExecuteAsync(QueryRequest request, CancellationToken cancellationToken = default) => executor.ExecuteAsync(request, cancellationToken);
}
