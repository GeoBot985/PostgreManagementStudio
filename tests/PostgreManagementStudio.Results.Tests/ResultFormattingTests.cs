using System.Globalization;
using PostgreManagementStudio.Core;
using PostgreManagementStudio.Results;

namespace PostgreManagementStudio.Results.Tests;

public sealed class ResultFormattingTests
{
    private static readonly ResultColumn Column = new(0, "value", "text", null, typeof(string), true);

    [Fact]
    public void DisplayAndSerializationHaveSeparateFidelityRules()
    {
        var formatter = new DefaultResultValueFormatter(); var cell = new ResultCell("one\ntwo", false);
        Assert.Equal("one↵two", formatter.FormatForDisplay(cell, Column, new(20)));
        Assert.Equal("one\ntwo", formatter.FormatForSerialization(cell, Column, new()));
    }

    [Fact]
    public void NullAndEmptyRemainDistinct()
    {
        var formatter = new DefaultResultValueFormatter();
        Assert.Equal("NULL", formatter.FormatForSerialization(new(null, true), Column, new()));
        Assert.Equal(string.Empty, formatter.FormatForSerialization(new(string.Empty, false), Column, new()));
    }

    [Fact]
    public void ValuesUseInvariantCultureAndExplicitBinaryFormat()
    {
        var old = CultureInfo.CurrentCulture; CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
        try
        {
            var formatter = new DefaultResultValueFormatter();
            Assert.Equal("1234.50", formatter.FormatForSerialization(new(1234.50m, false), Column, new()));
            Assert.Equal("0x4D5A", formatter.FormatForSerialization(new(new byte[] { 0x4D, 0x5A }, false), Column, new()));
        }
        finally { CultureInfo.CurrentCulture = old; }
    }

    [Fact]
    public void SelectionRejectsInvalidRanges()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ResultSelection(-1, 1, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ResultSelection(2, 1, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ResultSelection(0, 1, 2, 1));
    }
}
