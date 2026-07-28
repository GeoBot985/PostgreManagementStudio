using PostgreManagementStudio.Core;

namespace PostgreManagementStudio.Application;

public sealed class PostgresVersionService(IPostgresVersionQuery query)
{
    public Task<string> GetVersionAsync(string connectionString, CancellationToken cancellationToken = default) =>
        query.ExecuteAsync(connectionString, cancellationToken);
}
