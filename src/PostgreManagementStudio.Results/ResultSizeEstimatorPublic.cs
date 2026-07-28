using PostgreManagementStudio.Core;

namespace PostgreManagementStudio.Results;

/// <summary>
/// Public-facing wrapper around the internal memory estimator. Used by tests
/// and by Application-layer diagnostics that need to print the heuristic value.
/// </summary>
public static class ResultSizeEstimatorPublic
{
    public static int EstimateCellBytes(ResultCell cell) => ResultSizeEstimator.EstimateCellBytes(cell);
    public static int EstimateRowOverheadBytes(ResultRow row) => ResultSizeEstimator.EstimateRowOverheadBytes(row);
    public static long EstimateSchemaBytes(ResultSetSchema schema) => ResultSizeEstimator.EstimateSchemaBytes(schema);
    public static long EstimateBatchOverheadBytes(int rowCount) => ResultSizeEstimator.EstimateBatchOverheadBytes(rowCount);
}