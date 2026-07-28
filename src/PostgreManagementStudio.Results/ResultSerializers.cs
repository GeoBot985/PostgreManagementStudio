using System.Net;
using PostgreManagementStudio.Core;

namespace PostgreManagementStudio.Results;

public sealed class ResultSerializer(IResultValueFormatter formatter, ResultSerializationFormat format) : IResultSerializer
{
    public ResultSerializationFormat Format => format;

    public async Task<SerializationOutcome> SerializeAsync(IResultSetStore resultSet, ResultSelection selection, ResultSerializationOptions options, TextWriter writer, CancellationToken cancellationToken = default)
    {
        Validate(resultSet, selection, options); var rows = 0L; var chars = 0L; var lineEnding = options.EffectiveFormatting.LineEnding;
        try
        {
            if (format == ResultSerializationFormat.HtmlTable) await WriteAsync("<table>", false);
            if (options.IncludeHeaders)
            {
                var headers = Enumerable.Range(selection.StartColumnIndex, selection.ColumnCount).Select(i => Escape(resultSet.Schema.Columns[i].Name, false));
                await WriteAsync(format == ResultSerializationFormat.HtmlTable ? "<thead><tr><th>" + string.Join("</th><th>", headers) + "</th></tr></thead><tbody>" : string.Join(Delimiter(), headers) + End(), false);
            }
            for (var position = selection.StartRowIndex; position <= selection.EndRowIndex; position += options.ReadBatchSize)
            {
                cancellationToken.ThrowIfCancellationRequested(); var take = (int)Math.Min(options.ReadBatchSize, selection.EndRowIndex - position + 1); var batch = await resultSet.GetRowsAsync(position, take, cancellationToken);
                foreach (var row in batch)
                {
                    var values = Enumerable.Range(selection.StartColumnIndex, selection.ColumnCount).Select(i => formatter.FormatForSerialization(row.Cells[i], resultSet.Schema.Columns[i], options.EffectiveFormatting)).ToArray();
                    var serialized = format == ResultSerializationFormat.HtmlTable ? "<tr><td>" + string.Join("</td><td>", values.Select(v => Escape(v, true))) + "</td></tr>" : string.Join(Delimiter(), values.Select(v => Escape(v, true))) + End();
                    await WriteAsync(serialized, true); rows++;
                }
            }
            if (format == ResultSerializationFormat.HtmlTable) await WriteAsync("</tbody></table>", false);
            return new SerializationOutcome(rows, chars, true, null);
        }
        catch (OperationCanceledException) { return new SerializationOutcome(rows, chars, false, ResultSerializationStopReason.Cancelled); }
        catch (OutputLimitException) { return new SerializationOutcome(rows, chars, false, ResultSerializationStopReason.MaximumOutputExceeded); }
        async Task WriteAsync(string text, bool countRow) { if (chars + text.Length > options.MaximumOutputCharacters) throw new OutputLimitException(); await writer.WriteAsync(text); chars += text.Length; }
        string End() => format == ResultSerializationFormat.HtmlTable ? "" : lineEnding;
        string Delimiter() => format == ResultSerializationFormat.Csv ? "," : format == ResultSerializationFormat.TabSeparatedValues ? "\t" : format == ResultSerializationFormat.HtmlTable ? "" : "\t";
        string Escape(string value, bool data) => format switch { ResultSerializationFormat.Csv => Csv(value), ResultSerializationFormat.TabSeparatedValues => Tsv(value), ResultSerializationFormat.HtmlTable => Html(value, data), _ => value };
        static string Csv(string value) => value.Contains(',') || value.Contains('"') || value.Contains('\r') || value.Contains('\n') ? '"' + value.Replace("\"", "\"\"") + '"' : value;
        static string Tsv(string value) => value.Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\r", "\\r").Replace("\n", "\\n");
        static string Html(string value, bool data) => WebUtility.HtmlEncode(value).Replace("\r\n", "<br>").Replace("\n", "<br>");
    }

    private static void Validate(IResultSetStore store, ResultSelection selection, ResultSerializationOptions options)
    { if (selection.EndColumnIndex >= store.Schema.Columns.Count || selection.EndRowIndex >= store.LoadedRowCount) throw new ArgumentOutOfRangeException(nameof(selection)); if (options.MaximumOutputCharacters <= 0 || options.ReadBatchSize <= 0) throw new ArgumentOutOfRangeException(nameof(options)); }
    private sealed class OutputLimitException : Exception;
}
