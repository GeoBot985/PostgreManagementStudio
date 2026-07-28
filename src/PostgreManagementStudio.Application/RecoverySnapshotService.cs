using System.Text.Json;

namespace PostgreManagementStudio.Application;
public sealed record RecoverySnapshot(Guid Id, string DisplayName, string? FilePath, string Text, DateTimeOffset Timestamp, EncodingKind Encoding, string Database, int SchemaVersion = 1);
public sealed class RecoverySnapshotService
{
    private readonly string _root; public RecoverySnapshotService(string? root = null) { _root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PostgreManagementStudio", "recovery"); }
    public async Task<string?> WriteAsync(SqlDocument document, string database, CancellationToken cancellationToken = default) { if (!document.IsDirty && document.FilePath is not null) return null; Directory.CreateDirectory(_root); var snapshot = new RecoverySnapshot(document.Id, document.DisplayName, document.FilePath, document.Text, DateTimeOffset.UtcNow, document.EncodingKind, database); var path = Path.Combine(_root, document.Id + ".json"); await File.WriteAllTextAsync(path, JsonSerializer.Serialize(snapshot), cancellationToken); return path; }
    public async Task<RecoverySnapshot?> ReadAsync(string path, CancellationToken cancellationToken = default) { try { await using var stream = File.OpenRead(path); var snapshot = await JsonSerializer.DeserializeAsync<RecoverySnapshot>(stream, cancellationToken: cancellationToken); return snapshot?.SchemaVersion == 1 ? snapshot : null; } catch { return null; } }
    public void Remove(SqlDocument document) { var path = Path.Combine(_root, document.Id + ".json"); if (File.Exists(path)) File.Delete(path); }
}
