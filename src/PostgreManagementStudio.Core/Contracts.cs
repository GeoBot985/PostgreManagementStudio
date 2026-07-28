namespace PostgreManagementStudio.Core;

public interface IPostgresVersionQuery
{
    Task<string> ExecuteAsync(string connectionString, CancellationToken cancellationToken = default);
}
