using System.Text;
using PostgreManagementStudio.Application;

namespace PostgreManagementStudio.Core.Tests;

public sealed class FileManagementTests
{
    [Fact]
    public async Task LoadsAndSavesUtf8BomAndPreservesDirtyState()
    {
        var root = Path.Combine(Path.GetTempPath(), "pms-" + Guid.NewGuid()); Directory.CreateDirectory(root); var path = Path.Combine(root, "test.sql"); await File.WriteAllTextAsync(path, "SELECT café;", new UTF8Encoding(true));
        var service = new DocumentFileService(); var loaded = await service.LoadAsync(path); var document = SqlDocument.FromLoaded(loaded); Assert.Equal(EncodingKind.Utf8Bom, loaded.Encoding); Assert.False(document.IsDirty); await service.SaveAsync(document, path); Assert.Equal(new UTF8Encoding(true).GetPreamble(), (await File.ReadAllBytesAsync(path))[..3]); Directory.Delete(root, true);
    }

    [Fact]
    public void RecentFilesAreDeduplicatedAndCapped()
    { var root = Path.Combine(Path.GetTempPath(), "pms-recent-" + Guid.NewGuid()); var recent = new RecentFilesService(root); for (var i = 0; i < 12; i++) recent.Add($"C:\\file{i}.sql"); recent.Add("C:\\file5.sql"); Assert.Equal(10, recent.Files.Count); Assert.Equal(Path.GetFullPath("C:\\file5.sql"), recent.Files[0]); }

    [Fact]
    public void FindAndReplaceSupportsWholeWordAndCountsChanges()
    { var service = new FindReplaceService(); var options = new SearchOptions(WholeWord: true, Wrap: false); Assert.Equal(0, service.FindNext("cat category cat", "cat", 0, options)); var result = service.ReplaceAll("cat category cat", "cat", "dog", options, out var count); Assert.Equal("dog category dog", result); Assert.Equal(2, count); }

    [Fact]
    public async Task RecoverySnapshotRoundTripsUnsavedTextWithoutSecrets()
    { var root = Path.Combine(Path.GetTempPath(), "pms-recovery-" + Guid.NewGuid()); var document = new SqlDocument { DisplayName = "Query 1" }; document.SetText("SELECT 42;"); var service = new RecoverySnapshotService(root); var path = await service.WriteAsync(document, "postgres"); var snapshot = await service.ReadAsync(path!); Assert.Equal(document.Text, snapshot!.Text); Assert.Null(snapshot.FilePath); Assert.DoesNotContain("Password", await File.ReadAllTextAsync(path!)); }

    [Fact]
    [Trait("Category", "Component")]
    [Trait("Priority", "P0")]
    public async Task RecoverySnapshots_AreAtomicOrderedAndIgnoreCorruption()
    {
        var root = Path.Combine(Path.GetTempPath(), "pms-recovery-" + Guid.NewGuid());
        var service = new RecoverySnapshotService(root);
        var later = new RecoverySnapshot(
            Guid.NewGuid(), "Later", null, "SELECT 2;", DateTimeOffset.UtcNow.AddMinutes(1),
            EncodingKind.Utf8, "postgres", 4);
        var earlier = new RecoverySnapshot(
            Guid.NewGuid(), "Earlier", null, "SELECT 1;", DateTimeOffset.UtcNow,
            EncodingKind.Utf8, "postgres", 3);

        await service.WriteAsync(later);
        await service.WriteAsync(earlier);
        await File.WriteAllTextAsync(Path.Combine(root, "corrupt.json"), "{not-json");

        var snapshots = await service.ReadAllAsync();
        Assert.Equal(new[] { earlier.Id, later.Id }, snapshots.Select(x => x.Id));
        Assert.All(Directory.EnumerateFiles(root), path =>
            Assert.False(path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)));

        service.Remove(earlier.Id);
        Assert.Equal(later.Id, Assert.Single(await service.ReadAllAsync()).Id);
        Directory.Delete(root, recursive: true);
    }
}
