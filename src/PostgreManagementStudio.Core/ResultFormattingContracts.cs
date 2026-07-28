namespace PostgreManagementStudio.Core;

public sealed record ResultSelection
{
    public ResultSelection(long startRowIndex, long endRowIndex, int startColumnIndex, int endColumnIndex)
    {
        if (startRowIndex < 0 || endRowIndex < startRowIndex) throw new ArgumentOutOfRangeException(nameof(startRowIndex));
        if (startColumnIndex < 0 || endColumnIndex < startColumnIndex) throw new ArgumentOutOfRangeException(nameof(startColumnIndex));
        StartRowIndex = startRowIndex; EndRowIndex = endRowIndex; StartColumnIndex = startColumnIndex; EndColumnIndex = endColumnIndex;
    }
    public long StartRowIndex { get; }
    public long EndRowIndex { get; }
    public int StartColumnIndex { get; }
    public int EndColumnIndex { get; }
    public long RowCount => EndRowIndex - StartRowIndex + 1;
    public int ColumnCount => EndColumnIndex - StartColumnIndex + 1;
}

public enum ResultSerializationFormat { PlainText, TabSeparatedValues, Csv, HtmlTable }
public enum BinarySerializationMode { Hex, Base64 }
public enum DateTimeSerializationMode { Iso8601 }
public enum ResultSerializationStopReason { Cancelled, MaximumOutputExceeded, InvalidSelection, FormattingFailure }

public sealed record ResultDisplayFormattingOptions(int MaximumTextLength = 256, string NullText = "NULL", bool ShowControlCharacterMarkers = true, bool UseSingleLinePreview = true);
public sealed record ResultSerializationFormattingOptions(string NullText = "NULL", string LineEnding = "\r\n", bool IncludeHeaders = true, bool PreserveEmbeddedLineBreaks = true, BinarySerializationMode BinaryMode = BinarySerializationMode.Hex, DateTimeSerializationMode DateTimeMode = DateTimeSerializationMode.Iso8601);
public sealed record ResultSerializationOptions(ResultSerializationFormat Format, bool IncludeHeaders = true, long MaximumOutputCharacters = 1_000_000, int ReadBatchSize = 256, ResultSerializationFormattingOptions? Formatting = null)
{
    public ResultSerializationFormattingOptions EffectiveFormatting => (Formatting ?? new()) with { IncludeHeaders = IncludeHeaders };
}
public sealed record SerializationOutcome(long RowsSerialized, long CharactersWritten, bool Completed, ResultSerializationStopReason? StopReason);

public interface IResultValueFormatter
{
    string FormatForDisplay(ResultCell cell, ResultColumn column, ResultDisplayFormattingOptions options);
    string FormatForSerialization(ResultCell cell, ResultColumn column, ResultSerializationFormattingOptions options);
}

public interface IResultSerializer
{
    ResultSerializationFormat Format { get; }
    Task<SerializationOutcome> SerializeAsync(IResultSetStore resultSet, ResultSelection selection, ResultSerializationOptions options, TextWriter writer, CancellationToken cancellationToken = default);
}
