using Microsoft.Extensions.Logging;
using PostgreManagementStudio.Core;

namespace PostgreManagementStudio.Results;

/// <summary>
/// Default <see cref="IResultSessionBuilder"/> that consumes the Sprint 001 event
/// stream and materialises an in-memory <see cref="IResultSession"/>. The builder owns
/// the session lifecycle and never exposes per-event mutators to the visual layer.
/// </summary>
public sealed class ResultSessionBuilder : IResultSessionBuilder
{
    private readonly IQueryExecutor _executor;
    private readonly ResultStorageOptions _options;
    private readonly ILogger<ResultSessionBuilder>? _logger;

    public ResultSessionBuilder(IQueryExecutor executor, ResultStorageOptions? options = null, ILogger<ResultSessionBuilder>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(executor);
        _executor = executor;
        _options = options ?? ResultStorageOptions.Default;
        _logger = logger;
    }

    public async Task<IResultSession> ExecuteAndBuildAsync(QueryRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var session = new ResultSession(_options, _logger is null ? null : (ILogger)_logger);
        try
        {
            await foreach (var ev in _executor.ExecuteAsync(request, cancellationToken).ConfigureAwait(false))
            {
                switch (ev)
                {
                    case ExecutionStarted started:
                        session.Start(started.StartedAt);
                        break;

                    case ResultSetStarted rs:
                    {
                        var store = session.CreateStore(rs.ResultSetIndex, rs.Schema);
                        _ = store; // writer is internal-only; events route through GetWriter
                        break;
                    }

                    case RowBatchReceived rb:
                    {
                        var writer = session.GetWriter(rb.ResultSetIndex);
                        await writer.AppendBatchAsync(rb.Batch, cancellationToken).ConfigureAwait(false);
                        // After a successful append, account session-level memory and trigger
                        // session-limit truncation if needed. The internal hook on the store
                        // also receives the per-batch bytes.
                        if (rb.Batch.Rows.Count > 0 && writer is ResultSetStore concrete)
                        {
                            session.AddReceivedRows(rb.Batch.Rows.Count);
                            // Compute batch bytes for session aggregate.
                            var bytes = EstimateBatchBytesForSession(rb.Batch);
                            session.OnBatchRetained(concrete, bytes);
                        }
                        else if (rb.Batch.Rows.Count > 0)
                        {
                            session.AddReceivedRows(rb.Batch.Rows.Count);
                        }
                        break;
                    }

                    case ResultSetCompleted rc:
                    {
                        var writer = session.GetWriter(rc.ResultSetIndex);
                        await writer.CompleteAsync(rc.RowCount, cancellationToken).ConfigureAwait(false);
                        if (writer is ResultSetStore s) session.OnResultSetCompleted(s);
                        break;
                    }

                    case DatabaseNoticeReceived notice:
                        session.AddNotice(notice.Notice);
                        break;

                    case CommandCompleted:
                        // Commands do not produce result sets; nothing to retain.
                        break;

                    case ExecutionFailed failed:
                    {
                        var stores = session.StoresInternal;
                        for (int i = 0; i < stores.Length; i++)
                        {
                            var store = stores[i];
                            if (store.Status is ResultSetStatus.Cancelled or ResultSetStatus.Completed or ResultSetStatus.Disposed or ResultSetStatus.Failed)
                                continue;
                            await store.FailAsync(failed.Error, cancellationToken).ConfigureAwait(false);
                        }
                        session.Fail(failed.Error, null);
                        break;
                    }

                    case ExecutionCancelled cancelled:
                    {
                        var stores = session.StoresInternal;
                        for (int i = 0; i < stores.Length; i++)
                        {
                            var store = stores[i];
                            if (store.Status is ResultSetStatus.Cancelled or ResultSetStatus.Completed or ResultSetStatus.Disposed or ResultSetStatus.Failed)
                                continue;
                            await store.CancelAsync(cancellationToken).ConfigureAwait(false);
                        }
                        session.Cancel(cancelled.Elapsed);
                        break;
                    }

                    case ExecutionCompleted completed:
                        session.Complete(completed.Elapsed, completed.ResultSetCount);
                        break;

                    default:
                        throw new InvalidOperationException($"Unsupported event type: {ev.GetType().FullName}");
                }
            }
        }
        catch (OperationCanceledException) when (session.Status is not ResultSessionStatus.Cancelled and not ResultSessionStatus.Failed)
        {
            // Cancellation raised before the executor emitted a terminal event. Apply cancel semantics.
            var stores = session.StoresInternal;
            for (int i = 0; i < stores.Length; i++)
            {
                try { await stores[i].CancelAsync(CancellationToken.None).ConfigureAwait(false); }
                catch (ResultSetTerminalException) { /* store may already be terminal */ }
            }
            session.Cancel(DateTimeOffset.UtcNow - session.CreatedAt);
        }
        catch (Exception ex) when (ex is not InvalidBatchException and not DuplicateResultSetIndexException and not ResultSetTerminalException and not OperationCanceledException)
        {
            // Unexpected — convert into a session-level failure so callers receive a structured result.
            var dbError = new DatabaseError(ex.Message, null, null, null, null, null, null, null, null, null, null);
            var stores = session.StoresInternal;
            for (int i = 0; i < stores.Length; i++)
            {
                try { await stores[i].FailAsync(dbError, CancellationToken.None).ConfigureAwait(false); }
                catch (ResultSetTerminalException) { /* may already be terminal */ }
            }
            session.Fail(dbError, DateTimeOffset.UtcNow - session.CreatedAt);
        }

        return session;
    }

    private static long EstimateBatchBytesForSession(ResultRowBatch batch)
    {
        long bytes = ResultSizeEstimator.EstimateBatchOverheadBytes(batch.Rows.Count);
        for (int i = 0; i < batch.Rows.Count; i++)
        {
            var row = batch.Rows[i];
            bytes += ResultSizeEstimator.EstimateRowOverheadBytes(row);
            for (int c = 0; c < row.Cells.Count; c++) bytes += ResultSizeEstimator.EstimateCellBytes(row.Cells[c]);
        }
        return bytes;
    }
}