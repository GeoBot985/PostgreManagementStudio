using PostgreManagementStudio.Core;
using PostgreManagementStudio.Results;

namespace PostgreManagementStudio.Results.Tests;

public sealed class ResultCellHardeningTests
{
    private static readonly ResultColumn Column = new(0, "value", "unknown", null, typeof(object), true);

    [Fact]
    public void NullEmptyBinaryAndLargeValuesRemainDistinctAndBounded()
    {
        var formatter = new DefaultResultValueFormatter();
        var options = new ResultDisplayFormattingOptions(64);
        Assert.Equal("NULL", formatter.FormatForDisplay(new(null, true), Column, options));
        Assert.Equal("", formatter.FormatForDisplay(new("", false), Column, options));
        var binary = formatter.FormatForDisplay(new(new byte[10_000], false), Column, options);
        Assert.StartsWith("0x", binary);
        Assert.Contains("bytes", binary);
        Assert.True(binary.Length < 100);
        Assert.EndsWith("…", formatter.FormatForDisplay(new(new string('x', 10_000), false), Column, options));
    }

    [Fact]
    public void OneThrowingValueProducesACellErrorInsteadOfCrashing()
    {
        var formatter = new DefaultResultValueFormatter();
        var result = formatter.FormatForDisplay(new(new ThrowingValue(), false), Column, new(64));
        Assert.Equal("<formatting error: InvalidOperationException>", result);
    }

    private sealed class ThrowingValue
    {
        public override string ToString() => throw new InvalidOperationException("Sensitive value must not escape.");
    }
}
