using PostgreManagementStudio.Core;
using PostgreManagementStudio.Results;

namespace PostgreManagementStudio.Results.Tests;

public sealed class ResultExportTests
{
    private static async Task<IResultSession> SessionAsync()
    {
        var schema = QueryEventFactory.Schema(new[] { "id", "text" }); var events = QueryEventFactory.Build(new ExecutionStarted(DateTimeOffset.UtcNow), new ResultSetStarted(0, schema), new RowBatchReceived(0, QueryEventFactory.Batch(0, QueryEventFactory.Row(1, "a,b"), QueryEventFactory.Row(2, null))), new ResultSetCompleted(0, 2), new ExecutionCompleted(TimeSpan.Zero, 1)); return await new ResultSessionBuilder(new FakeQueryExecutor(events)).ExecuteAndBuildAsync(new QueryRequest("select", "local"), CancellationToken.None);
    }
    [Fact] public async Task ExportsCsvJsonAndSqlWithEscaping() { await using var session = await SessionAsync(); var root = Path.Combine(Path.GetTempPath(), "pms-export-" + Guid.NewGuid()); Directory.CreateDirectory(root); foreach (var format in new[] { ResultExportFormat.Csv, ResultExportFormat.Json, ResultExportFormat.SqlInsert }) { var path = Path.Combine(root, format + ".out"); var outcome = await new ResultExportService().ExportAsync(new ResultExportRequest(session.ResultSets[0], null, format, ResultExportScope.EntireResult, path, new())); Assert.True(outcome.Completed); var text = await File.ReadAllTextAsync(path); Assert.NotEmpty(text); if (format == ResultExportFormat.Csv) Assert.Contains("\"a,b\"", text); if (format == ResultExportFormat.Json) Assert.Contains("\"text\"", text); if (format == ResultExportFormat.SqlInsert) Assert.Contains("INSERT INTO", text); } Directory.Delete(root, true); }
    [Fact] public async Task CancellationLeavesExistingDestinationIntact() { await using var session = await SessionAsync(); var root = Path.Combine(Path.GetTempPath(), "pms-export-" + Guid.NewGuid()); Directory.CreateDirectory(root); var path = Path.Combine(root, "result.csv"); await File.WriteAllTextAsync(path, "original"); using var cts = new CancellationTokenSource(); cts.Cancel(); var outcome = await new ResultExportService().ExportAsync(new ResultExportRequest(session.ResultSets[0], null, ResultExportFormat.Csv, ResultExportScope.EntireResult, path, new()), cancellationToken: cts.Token); Assert.True(outcome.Cancelled); Assert.Equal("original", await File.ReadAllTextAsync(path)); Directory.Delete(root, true); }
}
