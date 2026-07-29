using PostgreManagementStudio.Application;

namespace PostgreManagementStudio.Core.Tests;

public sealed class PerformanceHardeningTests
{
    [Fact]
    public async Task LatestRequestDebouncesAndSupersededResultCannotApply()
    {
        await using var coordinator = new LatestRequestCoordinator<int>();
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = coordinator.RunAsync(1, TimeSpan.Zero, async token =>
        {
            firstStarted.SetResult();
            await releaseFirst.Task.WaitAsync(token);
            return 1;
        });
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var second = coordinator.RunAsync(2, TimeSpan.FromMilliseconds(10),
            _ => Task.FromResult(2));
        releaseFirst.TrySetResult();

        Assert.Equal(LatestRequestState.Superseded, (await first).State);
        var latest = await second;
        Assert.True(latest.Applied);
        Assert.Equal(2, latest.Value);
        Assert.Equal(0, coordinator.ActiveCount);
    }

    [Fact]
    public async Task CoordinatorDisposalCancelsOwnedWorkAndRejectsNewRequests()
    {
        var coordinator = new LatestRequestCoordinator<int>();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = coordinator.RunAsync(1, TimeSpan.Zero, async token =>
        {
            started.SetResult();
            await Task.Delay(TimeSpan.FromMinutes(1), token);
            return 1;
        });
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await coordinator.DisposeAsync();

        Assert.Equal(LatestRequestState.Cancelled, (await running).State);
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            coordinator.RunAsync(2, TimeSpan.Zero, _ => Task.FromResult(2)));
    }

    [Fact]
    public void PerformanceDiagnosticsAreObserverSafeAndBudgetsAreBounded()
    {
        using (var operation = new PerformanceOperation(
            "test-operation",
            new ThrowingPerformanceDiagnostics()))
        {
            operation.RowsRead = 10;
            operation.RowsDisplayed = 5;
        }

        Assert.All(PerformanceBudgets.InteractiveP95.Values,
            budget => Assert.InRange(budget, TimeSpan.FromMilliseconds(1), TimeSpan.FromSeconds(5)));
    }

    private sealed class ThrowingPerformanceDiagnostics : IPerformanceDiagnostics
    {
        public void Record(PerformanceDiagnostic diagnostic) =>
            throw new InvalidOperationException("diagnostics failed");
    }
}
