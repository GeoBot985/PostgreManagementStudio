using System.Collections.Concurrent;
using PostgreManagementStudio.Core;

namespace PostgreManagementStudio.Application;

public sealed class MetadataCache(IPostgresMetadataProvider provider)
{
    private readonly ConcurrentDictionary<string, Lazy<Task<DatabaseMetadataSnapshot>>> _entries = new();
    public Task<DatabaseMetadataSnapshot> GetAsync(string connectionString, string database, CancellationToken cancellationToken = default) { var key = Key(connectionString, database); return _entries.GetOrAdd(key, _ => new Lazy<Task<DatabaseMetadataSnapshot>>(() => provider.LoadAsync(connectionString, database, cancellationToken))).Value; }
    public void Invalidate(string connectionString, string database) => _entries.TryRemove(Key(connectionString, database), out _);
    public Task<DatabaseMetadataSnapshot> RefreshAsync(string connectionString, string database, CancellationToken cancellationToken = default) { Invalidate(connectionString, database); return GetAsync(connectionString, database, cancellationToken); }
    private static string Key(string connection, string database) => SqlDocument.Hash(connection.Replace("Password", "", StringComparison.OrdinalIgnoreCase)) + "|" + database;
}
