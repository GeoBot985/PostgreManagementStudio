using PostgreManagementStudio.Application;

namespace PostgreManagementStudio.Core.Tests;

public sealed class DatabaseMaintenanceTests
{
    [Fact]
    public void BuildsTypedVacuumAndAnalyzeStatements()
    {
        var plan = new MaintenancePlan(MaintenanceOperation.Vacuum, new[] { new MaintenanceTarget(MaintenanceTargetKind.Table, "Order", "Sales Data") }, new(Analyze: true, Verbose: true), new(18)); var sql = plan.Statements.Single(); Assert.Contains("VACUUM VERBOSE ANALYZE", sql); Assert.Contains("\"Sales Data\".\"Order\"", sql);
        var analyze = new MaintenancePlan(MaintenanceOperation.Analyze, new[] { new MaintenanceTarget(MaintenanceTargetKind.Table, "Order", "Sales Data", new[] { "Order Date" }) }, new(Analyze: true, Verbose: true), new(18)); Assert.Contains("(\"Order Date\")", analyze.Statements.Single());
    }

    [Fact]
    public void ValidatesHighImpactAndUnsupportedCombinations()
    {
        Assert.Throws<ArgumentException>(() => new MaintenancePlan(MaintenanceOperation.Vacuum, new[] { new MaintenanceTarget(MaintenanceTargetKind.Table, "t") }, new(Full: true, Freeze: true), new(18)).Statements);
        Assert.Throws<ArgumentException>(() => new MaintenancePlan(MaintenanceOperation.Reindex, new[] { new MaintenanceTarget(MaintenanceTargetKind.Database, "db") }, new(Concurrent: true), new(18)).Statements);
        Assert.Throws<ArgumentException>(() => new MaintenancePlan(MaintenanceOperation.Reindex, new[] { new MaintenanceTarget(MaintenanceTargetKind.System, "db"), new MaintenanceTarget(MaintenanceTargetKind.Table, "t") }, new(), new(18)).Statements);
    }

    [Fact]
    public void BuildsReindexAndClusterSafely()
    {
        var reindex = new MaintenancePlan(MaintenanceOperation.Reindex, new[] { new MaintenanceTarget(MaintenanceTargetKind.Index, "ix Weird", "Sales Data") }, new(Concurrent: true, Verbose: true), new(18)); Assert.Contains("REINDEX (VERBOSE) CONCURRENTLY INDEX", reindex.Statements.Single());
        var cluster = new MaintenancePlan(MaintenanceOperation.Cluster, new[] { new MaintenanceTarget(MaintenanceTargetKind.Table, "Order", "Sales Data") }, new(Verbose: true), new(18)); Assert.Contains("CLUSTER VERBOSE", cluster.Statements.Single());
    }

    [Fact]
    public void HistoryRemainsBoundedAndCapabilitiesAreVersionAware()
    {
        Assert.False(new MaintenanceCapabilities(11).SupportsReindexConcurrently); Assert.True(new MaintenanceCapabilities(18).SupportsVacuumParallel); var history = new MaintenanceHistoryService(2); for (var i = 0; i < 4; i++) history.Add(new(DateTimeOffset.UtcNow, "server", "db", "user", MaintenanceOperation.Vacuum, 1, "", "VACUUM;", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "Completed", 1, 0, true)); Assert.Equal(2, history.Entries.Count);
    }
}
