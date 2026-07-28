using PostgreManagementStudio.Core;

namespace PostgreManagementStudio.Results;

/// <summary>
/// Immutable retained batch: rows, the absolute start index, and the cached byte estimate.
/// Readers never mutate instances; the index swaps atomically to a newer array on append.
/// </summary>
internal sealed class BatchSegment
{
    public BatchSegment(long startRowIndex, ResultRow[] rows, long memoryBytes)
    {
        StartRowIndex = startRowIndex;
        Rows = rows;
        MemoryBytes = memoryBytes;
    }

    public long StartRowIndex { get; }
    public ResultRow[] Rows { get; }
    public int RowCount => Rows.Length;
    public long EndRowIndex => StartRowIndex + RowCount; // exclusive
    public long MemoryBytes { get; }
}