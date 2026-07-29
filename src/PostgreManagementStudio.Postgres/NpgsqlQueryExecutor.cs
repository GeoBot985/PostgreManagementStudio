using System.Collections.Concurrent;
using System.Threading.Channels;
using Npgsql;
using PostgreManagementStudio.Core;

namespace PostgreManagementStudio.Postgres;

public sealed class NpgsqlQueryExecutor(INpgsqlConnectionFactory? connectionFactory = null) : IQueryExecutor, IQueryExecutionScopeManager
{
    private readonly INpgsqlConnectionFactory _connections = connectionFactory ?? NpgsqlConnectionFactory.Shared;
    private readonly ConcurrentDictionary<Guid, ScopedConnection> _scopes = new();

    public async IAsyncEnumerable<QueryExecutionEvent> ExecuteAsync(
        QueryRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var channel = Channel.CreateBounded<QueryExecutionEvent>(new BoundedChannelOptions(4)
        {
            SingleWriter = true,
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait,
        });
        var producer = ProduceAsync(request, channel.Writer, cancellationToken);
        await foreach (var item in channel.Reader.ReadAllAsync(CancellationToken.None)) yield return item;
        await producer.ConfigureAwait(false);
    }

    public async ValueTask CloseScopeAsync(Guid executionScopeId)
    {
        if (!_scopes.TryRemove(executionScopeId, out var scope)) return;
        if (scope.Gate.Wait(0))
        {
            try { await scope.Connection.DisposeAsync().ConfigureAwait(false); }
            finally { scope.Gate.Release(); }
            return;
        }

        // A timed-out cancellation still owns this connection. Disposing it is
        // the provider-level abandonment path and interrupts pending I/O.
        await scope.Connection.DisposeAsync().ConfigureAwait(false);
    }

    private async Task ProduceAsync(QueryRequest request, ChannelWriter<QueryExecutionEvent> writer, CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        var notices = new ConcurrentQueue<PostgresNotice>();
        NpgsqlConnection? connection = null;
        ScopedConnection? scope = null;
        var scopedLockHeld = false;
        var discardConnection = false;
        NoticeEventHandler? noticeHandler = null;

        await writer.WriteAsync(new ExecutionStarted(started));
        try
        {
            if (request.Options.TransactionMode == QueryTransactionMode.UserManaged)
            {
                var scopeId = request.Options.ExecutionScopeId!.Value;
                scope = _scopes.GetOrAdd(scopeId, _ => new(
                    request.ConnectionString,
                    _connections.Create(request.ConnectionString, "PostgreManagementStudio - Transaction")));
                if (!string.Equals(scope.ConnectionString, request.ConnectionString, StringComparison.Ordinal))
                    throw new InvalidOperationException("The connection context cannot change while this editor owns a user-managed transaction session.");
                await scope.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                scopedLockHeld = true;
                connection = scope.Connection;
            }
            else
            {
                connection = _connections.Create(request.ConnectionString, "PostgreManagementStudio - Query");
            }

            noticeHandler = (_, e) => notices.Enqueue(e.Notice);
            connection.Notice += noticeHandler;
            if (connection.State != System.Data.ConnectionState.Open)
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var command = new NpgsqlCommand(request.Sql, connection)
            {
                CommandTimeout = request.Options.CommandTimeout is { } timeout ? (int)Math.Ceiling(timeout.TotalSeconds) : 0,
            };
            using var cancellationRegistration = cancellationToken.Register(static state =>
            {
                try { ((NpgsqlCommand)state!).Cancel(); }
                catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException or NpgsqlException) { }
            }, command);

            await using var reader = await command.ExecuteReaderAsync(System.Data.CommandBehavior.SequentialAccess, cancellationToken).ConfigureAwait(false);
            var resultIndex = 0;
            do
            {
                await WriteNoticesAsync(notices, writer).ConfigureAwait(false);
                if (reader.FieldCount == 0) continue;
                var columns = Enumerable.Range(0, reader.FieldCount)
                    .Select(i => new ResultColumn(i, reader.GetName(i), reader.GetDataTypeName(i), null, reader.GetFieldType(i), null))
                    .ToArray();
                await writer.WriteAsync(new ResultSetStarted(resultIndex, new ResultSetSchema(columns)), cancellationToken);
                var rows = new List<ResultRow>(request.Options.RowBatchSize);
                long rowIndex = 0;
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    var cells = new ResultCell[reader.FieldCount];
                    for (var i = 0; i < cells.Length; i++)
                    {
                        var isNull = await reader.IsDBNullAsync(i, cancellationToken).ConfigureAwait(false);
                        cells[i] = new ResultCell(isNull ? null : reader.GetValue(i), isNull);
                    }
                    rows.Add(new ResultRow(cells));
                    rowIndex++;
                    if (rows.Count == request.Options.RowBatchSize)
                    {
                        await writer.WriteAsync(new RowBatchReceived(resultIndex, new ResultRowBatch(rowIndex - rows.Count, rows.ToArray())), cancellationToken);
                        rows.Clear();
                    }
                }
                if (rows.Count > 0)
                    await writer.WriteAsync(new RowBatchReceived(resultIndex, new ResultRowBatch(rowIndex - rows.Count, rows.ToArray())), cancellationToken);
                await writer.WriteAsync(new ResultSetCompleted(resultIndex, rowIndex), cancellationToken);
                resultIndex++;
            } while (await reader.NextResultAsync(cancellationToken).ConfigureAwait(false));

            await WriteNoticesAsync(notices, writer).ConfigureAwait(false);
            await writer.WriteAsync(new CommandCompleted("COMPLETED", reader.RecordsAffected >= 0 ? reader.RecordsAffected : null), cancellationToken);
            await writer.WriteAsync(new ExecutionCompleted(DateTimeOffset.UtcNow - started, resultIndex), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await writer.WriteAsync(new ExecutionCancelled(DateTimeOffset.UtcNow - started));
        }
        catch (PostgresException ex)
        {
            if (ex.SqlState == "57014" && cancellationToken.IsCancellationRequested)
            {
                await writer.WriteAsync(new ExecutionCancelled(DateTimeOffset.UtcNow - started));
                return;
            }
            var kind = ex.SqlState switch
            {
                "57014" => DatabaseErrorKind.Timeout,
                "3D000" or "57P01" or "57P02" or "57P03" => DatabaseErrorKind.ConnectionLost,
                "23502" or "23503" or "23505" or "23514" => DatabaseErrorKind.Constraint,
                "28P01" or "28000" => DatabaseErrorKind.Authentication,
                _ => DatabaseErrorKind.Query,
            };
            discardConnection = kind == DatabaseErrorKind.ConnectionLost;
            if (discardConnection && connection is not null) NpgsqlConnection.ClearPool(connection);
            await writer.WriteAsync(new ExecutionFailed(new DatabaseError(
                ex.MessageText, ex.Severity, ex.SqlState, ex.Detail, ex.Hint, ex.Position,
                ex.SchemaName, ex.TableName, ex.ColumnName, ex.ConstraintName, ex.Routine,
                kind, ex.InternalPosition, ex.File, int.TryParse(ex.Line, out var sourceLine) ? sourceLine : null)));
        }
        catch (Exception ex) when (IsTimeout(ex))
        {
            await writer.WriteAsync(new ExecutionFailed(new DatabaseError(
                "The PostgreSQL command exceeded its configured timeout. It was cancelled and was not retried.",
                null, null, null, null, null, null, null, null, null, null,
                DatabaseErrorKind.Timeout)));
        }
        catch (Exception ex) when (IsConnectionFailure(ex))
        {
            discardConnection = true;
            if (connection is not null) NpgsqlConnection.ClearPool(connection);
            await writer.WriteAsync(new ExecutionFailed(new DatabaseError(
                "The PostgreSQL connection was lost. Verify the server, network, database, and credentials, then reconnect explicitly. The statement was not retried.",
                null, null, null, null, null, null, null, null, null, null,
                DatabaseErrorKind.ConnectionLost)));
        }
        catch (Exception ex)
        {
            var message = ex is InvalidOperationException && ex.Message.StartsWith("The connection context", StringComparison.Ordinal)
                ? ex.Message
                : $"The PostgreSQL provider could not complete the operation ({ex.GetType().Name}). Verify the connection and retry explicitly.";
            await writer.WriteAsync(new ExecutionFailed(new DatabaseError(
                message, null, null, null, null, null, null, null, null, null, null,
                DatabaseErrorKind.Provider)));
        }
        finally
        {
            if (connection is not null && noticeHandler is not null) connection.Notice -= noticeHandler;
            if (scope is null)
            {
                if (connection is not null) await connection.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                if (discardConnection && request.Options.ExecutionScopeId is { } scopeId
                    && _scopes.TryRemove(new KeyValuePair<Guid, ScopedConnection>(scopeId, scope)))
                    await scope.Connection.DisposeAsync().ConfigureAwait(false);
                if (scopedLockHeld) scope.Gate.Release();
            }
            writer.TryComplete();
        }
    }

    private static async Task WriteNoticesAsync(ConcurrentQueue<PostgresNotice> notices, ChannelWriter<QueryExecutionEvent> writer)
    {
        while (notices.TryDequeue(out var notice))
            await writer.WriteAsync(new DatabaseNoticeReceived(new(
                notice.Severity, notice.SqlState, notice.MessageText, notice.Detail, notice.Hint, DateTimeOffset.UtcNow)));
    }

    private static bool IsConnectionFailure(Exception exception)
        => exception is NpgsqlException { InnerException: System.Net.Sockets.SocketException or IOException }
            or NpgsqlException { IsTransient: true }
            or IOException
            or System.Net.Sockets.SocketException;

    private static bool IsTimeout(Exception exception)
        => exception is TimeoutException
            || exception.InnerException is TimeoutException
            || exception.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("timed out", StringComparison.OrdinalIgnoreCase);

    private sealed record ScopedConnection(
        [property: System.Diagnostics.DebuggerBrowsable(System.Diagnostics.DebuggerBrowsableState.Never)] string ConnectionString,
        NpgsqlConnection Connection)
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public override string ToString() => $"ScopedConnection ({Connection.State}, connection string redacted)";
    }
}
