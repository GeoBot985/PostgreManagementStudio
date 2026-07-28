using System.Collections.Concurrent;
using System.Threading.Channels;
using Npgsql;
using PostgreManagementStudio.Core;

namespace PostgreManagementStudio.Postgres;

public sealed class NpgsqlQueryExecutor(INpgsqlConnectionFactory? connectionFactory = null) : IQueryExecutor
{
    private readonly INpgsqlConnectionFactory _connections = connectionFactory ?? NpgsqlConnectionFactory.Shared;

    public async IAsyncEnumerable<QueryExecutionEvent> ExecuteAsync(QueryRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var channel = Channel.CreateUnbounded<QueryExecutionEvent>(new UnboundedChannelOptions { SingleWriter = true, SingleReader = true });
        _ = ProduceAsync(request, channel.Writer, cancellationToken);
        await foreach (var item in channel.Reader.ReadAllAsync()) yield return item;
    }

    private async Task ProduceAsync(QueryRequest request, ChannelWriter<QueryExecutionEvent> writer, CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow; var notices = new ConcurrentQueue<PostgresNotice>();
        await writer.WriteAsync(new ExecutionStarted(started));
        await using var connection = _connections.Create(request.ConnectionString, "PostgreManagementStudio - Query");
        void NoticeHandler(object? _, NpgsqlNoticeEventArgs e) => notices.Enqueue(e.Notice);
        connection.Notice += NoticeHandler;
        try
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand(request.Sql, connection) { CommandTimeout = request.Options.CommandTimeout is { } t ? (int)Math.Ceiling(t.TotalSeconds) : 0 };
            await using var reader = await command.ExecuteReaderAsync(System.Data.CommandBehavior.SequentialAccess, cancellationToken);
            var resultIndex = 0;
            do
            {
                await WriteNoticesAsync(notices, writer);
                if (reader.FieldCount == 0) continue;
                var columns = Enumerable.Range(0, reader.FieldCount).Select(i => new ResultColumn(i, reader.GetName(i), reader.GetDataTypeName(i), null, reader.GetFieldType(i), null)).ToArray();
                await writer.WriteAsync(new ResultSetStarted(resultIndex, new ResultSetSchema(columns)), cancellationToken);
                var rows = new List<ResultRow>(request.Options.RowBatchSize); long rowIndex = 0;
                while (await reader.ReadAsync(cancellationToken))
                {
                    var cells = new ResultCell[reader.FieldCount];
                    for (var i = 0; i < cells.Length; i++) { var isNull = await reader.IsDBNullAsync(i, cancellationToken); cells[i] = new ResultCell(isNull ? null : reader.GetValue(i), isNull); }
                    rows.Add(new ResultRow(cells));
                    rowIndex++;
                    if (rows.Count == request.Options.RowBatchSize) { await writer.WriteAsync(new RowBatchReceived(resultIndex, new ResultRowBatch(rowIndex - rows.Count, rows.ToArray())), cancellationToken); rows.Clear(); }
                }
                if (rows.Count > 0) await writer.WriteAsync(new RowBatchReceived(resultIndex, new ResultRowBatch(rowIndex - rows.Count, rows.ToArray())), cancellationToken);
                await writer.WriteAsync(new ResultSetCompleted(resultIndex, rowIndex), cancellationToken); resultIndex++;
            } while (await reader.NextResultAsync(cancellationToken));
            await WriteNoticesAsync(notices, writer);
            await writer.WriteAsync(new CommandCompleted("COMPLETED", reader.RecordsAffected >= 0 ? reader.RecordsAffected : null), cancellationToken);
            await writer.WriteAsync(new ExecutionCompleted(DateTimeOffset.UtcNow - started, resultIndex), cancellationToken);
        }
        catch (OperationCanceledException) { await writer.WriteAsync(new ExecutionCancelled(DateTimeOffset.UtcNow - started)); }
        catch (PostgresException ex) { await writer.WriteAsync(new ExecutionFailed(new DatabaseError(ex.MessageText, ex.Severity, ex.SqlState, ex.Detail, ex.Hint, ex.Position, ex.SchemaName, ex.TableName, ex.ColumnName, ex.ConstraintName, ex.Routine))); }
        catch (Exception ex) { await writer.WriteAsync(new ExecutionFailed(new DatabaseError(ex.Message, null, null, null, null, null, null, null, null, null, null))); }
        finally { connection.Notice -= NoticeHandler; writer.TryComplete(); }
    }

    private static async Task WriteNoticesAsync(ConcurrentQueue<PostgresNotice> notices, ChannelWriter<QueryExecutionEvent> writer)
    { while (notices.TryDequeue(out var n)) await writer.WriteAsync(new DatabaseNoticeReceived(new DatabaseNotice(n.Severity, n.SqlState, n.MessageText, n.Detail, n.Hint, DateTimeOffset.UtcNow))); }
}
