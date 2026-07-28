using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using PostgreManagementStudio.Core;

namespace PostgreManagementStudio.Application;

public sealed class MetadataCache(IPostgresMetadataProvider provider, int capacity = 32, TimeSpan? lifetime = null)
{
    private sealed record Entry(
        Lazy<Task<DatabaseMetadataSnapshot>> Value,
        DateTimeOffset ExpiresAt,
        long Sequence);

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly TimeSpan _lifetime = lifetime ?? TimeSpan.FromMinutes(2);
    private long _sequence;

    public async Task<DatabaseMetadataSnapshot> GetAsync(
        string connectionString,
        string database,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(database);
        if (capacity is < 1 or > 512) throw new ArgumentOutOfRangeException(nameof(capacity));
        if (_lifetime <= TimeSpan.Zero || _lifetime > TimeSpan.FromHours(1)) throw new ArgumentOutOfRangeException(nameof(lifetime));
        var key = Key(connectionString, database);
        if (_entries.TryGetValue(key, out var expired) && expired.ExpiresAt <= DateTimeOffset.UtcNow)
            _entries.TryRemove(key, out _);
        var entry = _entries.GetOrAdd(key, _ => new(
            new(() => provider.LoadAsync(connectionString, database, cancellationToken),
                LazyThreadSafetyMode.ExecutionAndPublication),
            DateTimeOffset.UtcNow + _lifetime,
            Interlocked.Increment(ref _sequence)));
        Trim();
        try
        {
            return Freeze(await entry.Value.Value.WaitAsync(cancellationToken).ConfigureAwait(false));
        }
        catch
        {
            _entries.TryRemove(new KeyValuePair<string, Entry>(key, entry));
            throw;
        }
    }

    public void Invalidate(string connectionString, string database) =>
        _entries.TryRemove(Key(connectionString, database), out _);

    public Task<DatabaseMetadataSnapshot> RefreshAsync(
        string connectionString,
        string database,
        CancellationToken cancellationToken = default)
    {
        Invalidate(connectionString, database);
        return GetAsync(connectionString, database, cancellationToken);
    }

    public void InvalidateAll() => _entries.Clear();

    private void Trim()
    {
        while (_entries.Count > capacity && _entries.MinBy(x => x.Value.Sequence) is { } oldest)
            _entries.TryRemove(oldest.Key, out _);
    }

    private static string Key(string connection, string database)
    {
        var connectionIdentity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(connection)));
        return connectionIdentity + "|" + database.Trim();
    }

    private static DatabaseMetadataSnapshot Freeze(DatabaseMetadataSnapshot value) => value with
    {
        Schemas = Array.AsReadOnly(value.Schemas.ToArray()),
        Relations = Array.AsReadOnly(value.Relations.Select(x => x with
        {
            Columns = Array.AsReadOnly(x.Columns.ToArray()),
        }).ToArray()),
        Routines = Array.AsReadOnly(value.Routines.ToArray()),
        Types = Array.AsReadOnly(value.Types.ToArray()),
        Sequences = Array.AsReadOnly(value.Sequences.ToArray()),
    };
}
