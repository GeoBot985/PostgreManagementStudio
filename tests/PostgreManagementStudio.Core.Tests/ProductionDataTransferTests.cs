using System.Text;
using PostgreManagementStudio.Application;

namespace PostgreManagementStudio.Core.Tests;

public sealed class ProductionDataTransferTests
{
    [Theory]
    [InlineData("utf8", "UTF-8")]
    [InlineData("utf8bom", "UTF-8 with BOM")]
    [InlineData("utf16le", "UTF-16 little-endian")]
    [InlineData("utf16be", "UTF-16 big-endian")]
    public async Task DetectsSupportedUnicodeEncodings(string kind, string expected)
    {
        Encoding encoding = kind switch
        {
            "utf8bom" => new UTF8Encoding(true),
            "utf16le" => new UnicodeEncoding(false, true),
            "utf16be" => new UnicodeEncoding(true, true),
            _ => new UTF8Encoding(false),
        };
        var path = TemporaryPath(".csv");
        try
        {
            await File.WriteAllTextAsync(path, "id,name\r\n1,Ångström\r\n", encoding);
            var inspection = await new ProductionDelimitedFileInspector().InspectAsync(path);
            Assert.Equal(expected, inspection.EncodingLabel);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task DetectsDelimiterAndStreamsBoundedMultilinePreview()
    {
        var path = TemporaryPath(".txt");
        try
        {
            await File.WriteAllTextAsync(path,
                "id|note\r\n1|\"line one\r\nline two\"\r\n2|done\r\n");
            var inspector = new ProductionDelimitedFileInspector();
            var inspection = await inspector.InspectAsync(path);
            var preview = await inspector.PreviewAsync(path, formatOverride:
                inspection.Format with { HasHeader = true }, maximumRecords: 1);

            Assert.Equal('|', inspection.Format.Delimiter);
            Assert.Single(preview.Records);
            Assert.Equal("line one\r\nline two", preview.Records[0].Fields[1].Value);
            Assert.True(preview.IsBoundedSample);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task ReportsUnclosedQuoteAndInconsistentFieldCount()
    {
        var path = TemporaryPath(".csv");
        try
        {
            await File.WriteAllTextAsync(path, "a,b\r\n1,2\r\n3,\"broken");
            var preview = await new ProductionDelimitedFileInspector().PreviewAsync(
                path, formatOverride: new(HasHeader: true));

            Assert.Contains(preview.Warnings, warning => warning.Contains("Source row 3"));
            Assert.Contains(preview.Records, record => record.IsMalformed);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void NormalisesEmptyDuplicateAndControlHeadersDeterministically()
    {
        var names = HeaderNormalizationService.Normalize(
            [" Order ID ", "Order ID", "\0", "Customer Name"], true);

        Assert.Equal(["Order_ID", "Order_ID_2", "column_3", "Customer_Name"], names);
    }

    [Fact]
    public void InferencePromotesRangesAndPreservesAmbiguousValues()
    {
        var records = new[]
        {
            Record("1", "12", "1.25", "03/04/2026", "0012"),
            Record("2", "5000000000", "2", "04/05/2026", "0013"),
        };
        var inferred = ProductionDataTypeInferenceService.Infer(
            ["small", "large", "mixed", "date", "code"], records);

        Assert.Equal("smallint", inferred[0].PostgreSqlType);
        Assert.Equal("bigint", inferred[1].PostgreSqlType);
        Assert.Equal("numeric", inferred[2].PostgreSqlType);
        Assert.Equal("text", inferred[3].PostgreSqlType);
        Assert.Contains(inferred[3].Warnings, warning => warning.Contains("Ambiguous date"));
        Assert.Equal("text", inferred[4].PostgreSqlType);
    }

    [Theory]
    [InlineData(ImportMappingMode.ExactName, null)]
    [InlineData(ImportMappingMode.CaseInsensitiveName, "order_id")]
    [InlineData(ImportMappingMode.Ordinal, "order_id")]
    public void MappingModesAreExplicitAndExcludeIdentityAlways(
        ImportMappingMode mode, string? expected)
    {
        var source = new[] { new SourceColumn(0, "ORDER_ID", "1") };
        var destination = new[]
        {
            new DestinationColumn("order_id", "integer", false),
            new DestinationColumn("generated_id", "integer", false, IdentityAlways: true),
        };
        var mapping = ProductionImportMappingService.Map(source, destination, mode).Single();
        Assert.Equal(expected, mapping.DestinationName);
    }

    [Fact]
    public void PreflightRejectsRequiredAndUnsafeMappings()
    {
        var path = Path.GetTempFileName();
        try
        {
            var result = ProductionImportPreflight.Validate(new(path, "public", "items",
                ImportDestinationMode.ExistingTable, [new(0, "generated")],
                [
                    new("required", "integer", false),
                    new("generated", "integer", false, Generated: true),
                ], ImportStrategy.Copy, TransactionMode.AllRows, CollectErrors: true));

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, error => error.Contains("identity-always")
                || error.Contains("Generated"));
            Assert.Contains(result.Errors, error => error.Contains("Collect-errors"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void BuildsQuotedPostgreSqlCreateTableAndValidatesIdentifiers()
    {
        var sql = NewTableSqlBuilder.Build("Mixed Schema", "order",
        [
            new("Order ID", "bigint", false, IsPrimaryKey: true),
            new("total", "numeric(12,2)", true, HasDefault: true,
                DefaultExpression: "0"),
        ]);

        Assert.Contains("CREATE TABLE \"Mixed Schema\".\"order\"", sql);
        Assert.Contains("\"Order ID\" bigint NOT NULL PRIMARY KEY", sql);
        Assert.Null(PostgreSqlTransferIdentifier.Validate("select"));
        Assert.NotNull(PostgreSqlTransferIdentifier.Validate(new string('é', 40)));
    }

    [Fact]
    public async Task RejectedRowsAreEscapedAndFinalisedAtomically()
    {
        var path = TemporaryPath(".csv");
        try
        {
            await new RejectedRowWriter().WriteAsync(path,
            [
                new RejectedRow(7, ["a,b", "line\nvalue"], "amount",
                    "bad \"number\"", "22P02"),
            ]);
            var text = await File.ReadAllTextAsync(path);
            Assert.Contains("\"bad \"\"number\"\"\"", text);
            Assert.DoesNotContain("Password=", text, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!,
                Path.GetFileName(path) + ".pms-rejected-*.tmp"));
        }
        finally { File.Delete(path); }
    }

    private static DelimitedRecord Record(params string[] values) => new(1,
        values.Select(value => new DelimitedField(value, false, false,
            value.Length == 0, string.IsNullOrWhiteSpace(value))).ToArray());

    private static string TemporaryPath(string extension) =>
        Path.Combine(Path.GetTempPath(), "pms-s60-" + Guid.NewGuid().ToString("N") + extension);
}
