using PostgreManagementStudio.Core;

namespace PostgreManagementStudio.Results;

/// <summary>
/// Approximate, type-aware memory accounting. The values are deterministic,
/// monotonic while rows are appended, and reduced to zero after disposal.
/// Constants document the per-type reference and content overhead used.
/// </summary>
internal static class ResultSizeEstimator
{
    // Object headers and reference slots on a 64-bit .NET runtime. These are
    // estimates, not runtime measurements, but stay close enough for limit enforcement.
    internal const int ObjectHeaderBytes = 24;       // .NET object header + method-table pointer
    internal const int ObjectReferenceBytes = 8;     // 64-bit reference slot
    internal const int PaddingBytes = 8;             // 64-bit alignment pad inside aggregates
    internal const int NullCellBytes = 16;           // ResultCell(object? Value, bool IsNull) when null
    internal const int BoxedIntBytes = 24;           // boxed Int32
    internal const int BoxedLongBytes = 24;          // boxed Int64
    internal const int BoxedBoolBytes = 24;          // boxed Boolean
    internal const int BoxedDoubleBytes = 24;        // boxed Double
    internal const int BoxedDateTimeBytes = 24;      // boxed DateTime
    internal const int BoxedGuidBytes = 24;          // boxed Guid
    internal const int BoxedDecimalBytes = 40;       // boxed Decimal (16 payload + header)
    internal const int BoxedTimeSpanBytes = 24;      // boxed TimeSpan (same layout as DateTime)
    internal const int StringHeaderBytes = 24;       // string object header + length + sync block
    internal const int CharSizeBytes = 2;            // UTF-16 code unit
    internal const int ByteArrayHeaderBytes = 32;    // byte[] object header + length + sync block (sync block conservatively counted)
    internal const int ArrayHeaderBytes = 32;        // generic array reference header

    /// <summary>
    /// Estimates the retained cost of a single <see cref="ResultCell"/>.
    /// </summary>
    public static int EstimateCellBytes(ResultCell cell)
    {
        if (cell.IsNull || cell.Value is null) return NullCellBytes;
        var v = cell.Value;
        // Nullable<T> boxes to T; we keep the value as object so the underlying type matters.
        return v switch
        {
            string s => StringHeaderBytes + s.Length * CharSizeBytes,
            byte[] b => ByteArrayHeaderBytes + b.Length,
            bool => BoxedBoolBytes,
            int => BoxedIntBytes,
            uint => BoxedIntBytes,
            long => BoxedLongBytes,
            ulong => BoxedLongBytes,
            short => BoxedIntBytes,
            ushort => BoxedIntBytes,
            byte => BoxedIntBytes,
            sbyte => BoxedIntBytes,
            double => BoxedDoubleBytes,
            float => BoxedDoubleBytes,
            decimal => BoxedDecimalBytes,
            DateTime => BoxedDateTimeBytes,
            DateTimeOffset => BoxedDateTimeBytes,
            TimeSpan => BoxedTimeSpanBytes,
            Guid => BoxedGuidBytes,
            _ when v.GetType().IsArray => EstimateArrayBytes((Array)v),
            _ => ObjectHeaderBytes + PaddingBytes // unknown object; fall back to header + alignment pad
        };
    }

    /// <summary>
    /// Estimates the retained cost of a single <see cref="ResultRow"/>.
    /// Cells are not counted again; only the row container overhead is added.
    /// </summary>
    public static int EstimateRowOverheadBytes(ResultRow _) => ObjectHeaderBytes + PaddingBytes;

    /// <summary>
    /// Estimates the retained cost of the schema (column metadata).
    /// </summary>
    public static long EstimateSchemaBytes(ResultSetSchema schema)
    {
        if (schema.Columns.Count == 0) return ObjectHeaderBytes;
        long total = ObjectHeaderBytes;
        foreach (var c in schema.Columns)
        {
            // ResultColumn object + (nullable string) headers + reference slots.
            total += ObjectHeaderBytes;
            total += c.Name is null ? ObjectReferenceBytes : StringHeaderBytes + c.Name.Length * CharSizeBytes;
            total += c.PostgreSqlTypeName is null ? ObjectReferenceBytes : StringHeaderBytes + c.PostgreSqlTypeName.Length * CharSizeBytes;
            total += ObjectReferenceBytes; // ClrType
            total += PaddingBytes;
        }
        // The Columns array itself.
        total += ArrayHeaderBytes + (long)schema.Columns.Count * ObjectReferenceBytes;
        return total;
    }

    private static int EstimateArrayBytes(Array array)
    {
        // Conservative: header + per-element reference (length × 8). Nested arrays are
        // bounded by ResultSetSchema column count and a single nesting level is sufficient
        // for in-memory accounting.
        return ArrayHeaderBytes + array.Length * ObjectReferenceBytes;
    }

    /// <summary>
    /// Per-batch overhead in bytes (BatchSegment container + inner ResultRow[] array slots).
    /// </summary>
    public static long EstimateBatchOverheadBytes(int rowCount)
        => ObjectHeaderBytes + ArrayHeaderBytes + (long)rowCount * ObjectReferenceBytes + PaddingBytes;
}