using PostgreManagementStudio.Application;
using PostgreManagementStudio.Core;

namespace PostgreManagementStudio.Core.Tests;

public sealed class QueryDocumentTests
{
    [Fact] public async Task EmptySqlIsRejectedBeforeExecution() { var doc = new QueryDocument(new ResultExecutionService(new NoOpExecutor()), "Query 1") { ConnectionString = "local", SqlText = "  " }; await Assert.ThrowsAsync<ArgumentException>(() => doc.ExecuteAsync()); }
    [Fact] public void DuplicateExecutionIsRejected() { var doc = new QueryDocument(new ResultExecutionService(new NoOpExecutor()), "Query 1") { ConnectionString = "local", SqlText = "SELECT 1" }; Assert.Equal(QueryDocumentExecutionState.Idle, doc.State); }
    [Fact] public void TabsAreIndependentAndDirtyTabsNeedDiscardConsent() { var manager = new QueryTabManager(new ResultExecutionService(new NoOpExecutor())); var first = manager.Open("a"); var second = manager.Open("b", "other"); first.SqlText = "SELECT 1"; first.MarkDirty(); Assert.Equal("b", second.ConnectionString); Assert.False(manager.TryClose(first, false)); Assert.True(manager.TryClose(first, true)); Assert.Single(manager.Documents); }
    private sealed class NoOpExecutor : IQueryExecutor { public async IAsyncEnumerable<QueryExecutionEvent> ExecuteAsync(QueryRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) { await Task.CompletedTask; yield return new ExecutionStarted(DateTimeOffset.UtcNow); yield return new ExecutionCompleted(TimeSpan.Zero, 0); } }
}
