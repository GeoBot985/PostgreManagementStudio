using PostgreManagementStudio.Application;
using PostgreManagementStudio.Core;

namespace PostgreManagementStudio.Core.Tests;

public sealed class ObjectExplorerTests
{
    [Fact]
    [Trait("Category", "Component")]
    [Trait("Priority", "P0")]
    public async Task ObjectExplorer_LoadsStableQualifiedHierarchyWithoutDuplicates()
    {
        var snapshot = new DatabaseMetadataSnapshot(
            "key",
            "regression",
            ["zeta", "Sales Data"],
            [
                new("Sales Data", "Order", CompletionKind.Table, []),
                new("Sales Data", "Résumé", CompletionKind.View, []),
                new("Sales Data", "Order", CompletionKind.Table, []),
            ],
            [new("Sales Data", "Calculate Total", "integer", "", CompletionKind.Function)],
            [],
            [],
            DateTimeOffset.UtcNow);

        var root = await new ObjectExplorerService(new SnapshotProvider(snapshot))
            .LoadDatabaseAsync("Host=example", "regression");

        Assert.Equal("regression", root.Name);
        Assert.Equal(["Sales Data", "zeta"], root.Children.Select(x => x.Name));
        var sales = root.Children[0];
        var tables = sales.Children.Single(x => x.Kind == ObjectExplorerNodeKind.Tables);
        Assert.Single(tables.Children);
        Assert.Equal("\"Sales Data\".\"Order\"", tables.Children[0].QualifiedName);
        Assert.Contains(sales.Children.SelectMany(x => x.Children), x => x.Name == "Résumé");
    }

    [Fact]
    [Trait("Category", "MutationSample")]
    [Trait("Priority", "P1")]
    public async Task ObjectExplorer_PropagatesCancellationToMetadataBoundary()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var service = new ObjectExplorerService(new CancellingProvider());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.LoadDatabaseAsync("Host=example", "regression", cancellation.Token));
    }

    private sealed class SnapshotProvider(DatabaseMetadataSnapshot snapshot) : IPostgresMetadataProvider
    {
        public Task<DatabaseMetadataSnapshot> LoadAsync(string connectionString, string database, CancellationToken cancellationToken = default) =>
            Task.FromResult(snapshot);
    }

    private sealed class CancellingProvider : IPostgresMetadataProvider
    {
        public Task<DatabaseMetadataSnapshot> LoadAsync(string connectionString, string database, CancellationToken cancellationToken = default) =>
            Task.FromCanceled<DatabaseMetadataSnapshot>(cancellationToken);
    }
}
