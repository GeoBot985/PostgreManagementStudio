using System.Diagnostics;
using PostgreManagementStudio.Core;

namespace PostgreManagementStudio.Results;

public sealed record FormattedResultRow(long RowIndex, IReadOnlyList<string> Values);

public sealed record ResultDisplayPage(
    long StartRowIndex,
    int PageSize,
    long RetainedRowCount,
    long ReceivedRowCount,
    long FinalRowCount,
    IReadOnlyList<ResultRow> SourceRows,
    IReadOnlyList<FormattedResultRow> DisplayRows,
    int IncompletePreviewCount,
    TimeSpan ReadDuration,
    TimeSpan FormattingDuration)
{
    public bool HasPrevious => StartRowIndex > 0;
    public bool HasNext => StartRowIndex + SourceRows.Count < RetainedRowCount;
    public long EndRowIndex => SourceRows.Count == 0 ? StartRowIndex : StartRowIndex + SourceRows.Count - 1;
}

public sealed class ResultDisplayPageService(IResultValueFormatter? formatter = null)
{
    public const int DefaultPageSize = 250;
    public const int MaximumPageSize = 1_000;
    private readonly IResultValueFormatter _formatter = formatter ?? new DefaultResultValueFormatter();

    public async Task<ResultDisplayPage> LoadAsync(
        IResultSetStore store,
        long startRowIndex,
        int pageSize = DefaultPageSize,
        int maximumTextLength = 512,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (startRowIndex < 0) throw new ArgumentOutOfRangeException(nameof(startRowIndex));
        if (pageSize is < 1 or > MaximumPageSize) throw new ArgumentOutOfRangeException(nameof(pageSize));
        if (maximumTextLength is < 16 or > 32_768) throw new ArgumentOutOfRangeException(nameof(maximumTextLength));
        cancellationToken.ThrowIfCancellationRequested();

        var retained = store.LoadedRowCount;
        var normalisedStart = retained == 0 ? 0 : Math.Min(startRowIndex, ((retained - 1) / pageSize) * pageSize);
        var readStarted = Stopwatch.GetTimestamp();
        var rows = await store.GetRowsAsync(normalisedStart, pageSize, cancellationToken).ConfigureAwait(false);
        var readDuration = Stopwatch.GetElapsedTime(readStarted);

        var formattingStarted = Stopwatch.GetTimestamp();
        var display = new FormattedResultRow[rows.Count];
        var incomplete = 0;
        var options = new ResultDisplayFormattingOptions(maximumTextLength);
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = rows[rowIndex];
            var values = new string[row.Cells.Count];
            for (var columnIndex = 0; columnIndex < row.Cells.Count; columnIndex++)
            {
                var cell = row.Cells[columnIndex];
                var formatted = _formatter.FormatForDisplay(
                    cell,
                    store.Schema.Columns[columnIndex],
                    options);
                values[columnIndex] = formatted;
                if (IsIncomplete(cell, maximumTextLength, formatted)) incomplete++;
            }
            display[rowIndex] = new(normalisedStart + rowIndex + 1, values);
        }

        return new(
            normalisedStart,
            pageSize,
            retained,
            store.ReceivedRowCount,
            store.FinalRowCount,
            rows,
            display,
            incomplete,
            readDuration,
            Stopwatch.GetElapsedTime(formattingStarted));
    }

    private static bool IsIncomplete(ResultCell cell, int maximumTextLength, string formatted) => cell.Value switch
    {
        string value => value.Length > maximumTextLength,
        byte[] value => value.Length * 2 + 2 > maximumTextLength,
        Array value => value.Length > 32,
        System.Text.Json.JsonDocument or System.Text.Json.JsonElement =>
            formatted.Length >= maximumTextLength && formatted.EndsWith('…'),
        _ => false,
    };
}
