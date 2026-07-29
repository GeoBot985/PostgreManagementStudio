using System.Text.Json;

namespace PostgreManagementStudio.Application;

public sealed record RecoverySnapshot(
    Guid Id,
    string DisplayName,
    string? FilePath,
    string Text,
    DateTimeOffset Timestamp,
    EncodingKind Encoding,
    string Database,
    int CaretOffset = 0,
    int SchemaVersion = 1);

public sealed class RecoverySnapshotService
{
    private const long MaximumSnapshotBytes = 512L * 1024 * 1024;
    private readonly string _root;

    public RecoverySnapshotService(string? root = null)
    {
        _root = root ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PostgreManagementStudio",
            "recovery");
    }

    public Task<string?> WriteAsync(
        SqlDocument document,
        string database,
        CancellationToken cancellationToken = default) =>
        WriteAsync(new(
            document.Id,
            document.DisplayName,
            document.FilePath,
            document.Text,
            DateTimeOffset.UtcNow,
            document.EncodingKind,
            database), cancellationToken);

    public async Task<string?> WriteAsync(
        RecoverySnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Id == Guid.Empty)
            throw new ArgumentException("A recovery snapshot must have a document identity.", nameof(snapshot));
        if (string.IsNullOrWhiteSpace(snapshot.DisplayName))
            throw new ArgumentException("A recovery snapshot must have a display name.", nameof(snapshot));

        Directory.CreateDirectory(_root);
        var path = PathFor(snapshot.Id);
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, snapshot, cancellationToken: cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, path, overwrite: true);
            return path;
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    public async Task<RecoverySnapshot?> ReadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists || file.Length <= 0 || file.Length > MaximumSnapshotBytes)
                return null;
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var snapshot = await JsonSerializer.DeserializeAsync<RecoverySnapshot>(
                stream,
                cancellationToken: cancellationToken);
            return IsValid(snapshot) ? snapshot : null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or JsonException or NotSupportedException)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<RecoverySnapshot>> ReadAllAsync(
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_root))
            return [];

        string[] paths;
        try
        {
            paths = Directory.GetFiles(_root, "*.json");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        var snapshots = new List<RecoverySnapshot>();
        foreach (var path in paths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await ReadAsync(path, cancellationToken) is { } snapshot)
                snapshots.Add(snapshot);
        }

        return snapshots
            .OrderBy(snapshot => snapshot.Timestamp)
            .ToArray();
    }

    public void Remove(SqlDocument document) => Remove(document.Id);

    public void Remove(Guid documentId)
    {
        if (documentId == Guid.Empty)
            return;
        try
        {
            var path = PathFor(documentId);
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.WriteLine(
                $"recovery_snapshot_remove_failed type={ex.GetType().FullName}");
        }
    }

    private string PathFor(Guid documentId) =>
        Path.Combine(_root, documentId.ToString("N") + ".json");

    private static bool IsValid(RecoverySnapshot? snapshot) =>
        snapshot is
        {
            SchemaVersion: 1,
            Id: var id,
            DisplayName.Length: > 0,
            Text: not null,
            Database.Length: > 0,
        } && id != Guid.Empty;
}
