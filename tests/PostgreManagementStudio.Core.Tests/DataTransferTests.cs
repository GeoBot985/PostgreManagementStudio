using PostgreManagementStudio.Application;
using System.Text;

namespace PostgreManagementStudio.Core.Tests;

public sealed class DataTransferTests
{
    [Fact]
    public void DetectsDelimitersAndParsesQuotedNewlines()
    {
        var path = Path.GetTempFileName(); File.WriteAllText(path, "name|value\n\"A|B\"|\"line1\nline2\"\n", Encoding.UTF8); try { var settings = DelimitedFileDetector.Detect(path) with { HasHeader = true }; var row = new DelimitedFileReader().Read(path, settings).Single(); Assert.Equal(new[] { "A|B", "line1\nline2" }, row); } finally { File.Delete(path); }
    }

    [Fact]
    public void MapsNamesAndProtectsGeneratedColumns()
    {
        var source = new[] { new SourceColumn(0, "ID", "1"), new SourceColumn(1, "name", "A") }; var destination = new[] { new DestinationColumn("id", "integer", false, Generated: true), new DestinationColumn("name", "text", true) }; var mappings = ImportMappingService.Map(source, destination); Assert.Equal("id", mappings[0].DestinationName); Assert.Throws<ArgumentException>(() => ImportMappingService.Validate(mappings, destination));
    }

    [Fact]
    public void ConvertsCommonPostgresValuesAndRejectsInvalidBooleans()
    {
        Assert.Equal(true, DataValueConverter.Convert("yes", "boolean", new())); Assert.Equal(12.5m, DataValueConverter.Convert("12.5", "numeric", new())); Assert.Throws<FormatException>(() => DataValueConverter.Convert("maybe", "boolean", new())); Assert.Null(DataValueConverter.Convert("\\N", "text", new()));
    }

    [Fact]
    public async Task WritesCsvWithSafeQuoting()
    {
        var writer = new DelimitedFileWriter(); async IAsyncEnumerable<IReadOnlyList<string>> Rows() { yield return new[] { "a,b", "line\nvalue" }; } var path = Path.GetTempFileName(); try { var count = await writer.WriteAsync(Rows(), path, x => x); Assert.Equal(1, count); Assert.Contains("\"a,b\"", File.ReadAllText(path)); } finally { File.Delete(path); }
    }

    [Fact]
    public void DetectsJsonLinesAndPreservesLeadingZeroes()
    { var path = Path.GetTempFileName(); File.WriteAllText(path, "{\"code\":\"00123\"}\n{\"code\":\"00456\"}\n"); try { var detected = DataFormatDetector.Detect(path); Assert.Equal(TransferFormat.JsonLines, detected.Format); var inferred = DataTypeInferenceService.Infer(new[] { new SourceColumn(0, "code", "00123") }); Assert.Equal("text", inferred[0].PostgreSqlType); Assert.NotEmpty(inferred[0].Conflicts); } finally { File.Delete(path); } }

    [Fact]
    public void RejectsUnsafeImportPlans()
    { var path = Path.GetTempFileName(); try { var plan = new ImportPlan(path, "public", "items", new[] { new ColumnMapping(0, "generated") }, ImportMode.Replace, ImportExecutionMethod.Copy, TransactionMode.AllRows, ImportErrorStrategy.ContinueAndCollectRejected); var result = ImportPlanValidator.Validate(plan, new[] { new DestinationColumn("generated", "integer", false, Generated: true) }); Assert.False(result.IsValid); Assert.Contains(result.Errors, x => x.Contains("Generated")); Assert.Contains(result.Errors, x => x.Contains("confirmation")); Assert.Contains(result.Errors, x => x.Contains("incompatible")); } finally { File.Delete(path); } }

    [Fact]
    public async Task StreamsJsonArrayRows()
    { var path = Path.GetTempFileName(); File.WriteAllText(path, "[{\"id\":1},{\"id\":2}]"); try { var rows = new List<string[]>(); await foreach (var row in JsonImportReader.ReadAsync(path)) rows.Add(row); Assert.Equal(2, rows.Count); Assert.Equal("1", rows[0][0]); } finally { File.Delete(path); } }
}
