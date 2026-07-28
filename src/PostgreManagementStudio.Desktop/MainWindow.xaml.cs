using System.Text;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using PostgreManagementStudio.Application;
using PostgreManagementStudio.Core;
using PostgreManagementStudio.Postgres;
using PostgreManagementStudio.Results;

namespace PostgreManagementStudio.Desktop;

/// <summary>
/// Temporary WPF preview. Uses the Sprint 002 result-store layer via
/// <see cref="ResultExecutionService"/> so rows are retained in the store
/// rather than built into one WPF object per cell. The preview requests
/// pages of rows on demand as the user scrolls.
/// </summary>
public partial class MainWindow : Window
{
    private CancellationTokenSource? _executionCancellation;
    private IResultSession? _session;
    private readonly ResultExecutionService _service = new(new NpgsqlQueryExecutor());
    private const int PageSize = 100;

    public MainWindow() => InitializeComponent();

    private async void Execute_Click(object sender, RoutedEventArgs e)
    {
        var connectionString = Environment.GetEnvironmentVariable("PMS_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            StatusText.Text = "Set PMS_CONNECTION_STRING first.";
            return;
        }
        // Cancel any existing session preview state.
        if (_session is not null)
        {
            try { await _session.DisposeAsync(); } catch { /* best-effort */ }
            _session = null;
        }
        _executionCancellation = new CancellationTokenSource();
        ExecuteButton.IsEnabled = false;
        StatusText.Text = "Running…";
        CountsText.Text = string.Empty;
        MemoryText.Text = string.Empty;
        TruncationText.Text = string.Empty;
        NoticesText.Text = string.Empty;
        ResultSetSelector.Items.Clear();
        RowPreview.ItemsSource = null;
        FooterText.Text = string.Empty;

        try
        {
            var session = await _service.ExecuteAndBuildAsync(
                new QueryRequest(SqlText.Text, connectionString),
                _executionCancellation.Token);
            _session = session;

            // Populate result-set selector.
            foreach (var store in session.ResultSets)
                ResultSetSelector.Items.Add(new ResultSetOption(store));
            if (ResultSetSelector.Items.Count > 0)
                ResultSetSelector.SelectedIndex = 0;
            else
                StatusText.Text = "No result sets returned.";

            StatusText.Text = $"Finished: {session.Status}";
            CountsText.Text = $"Result sets: {session.ResultSets.Count} | Received rows: {session.ReceivedRowCount:N0} | Retained rows: {session.RetainedRowCount:N0} | Elapsed: {session.Elapsed?.TotalMilliseconds ?? 0:N0} ms";
            MemoryText.Text = $"Estimated memory: {session.EstimatedMemoryBytes:N0} bytes";
            if (session.WasTruncated)
            {
                TruncationText.Text = $"Truncated: {session.TruncationReason} — retained prefix shown; full row count available on the affected set(s).";
            }
            if (session.Notices.Count > 0)
            {
                var sb = new StringBuilder("Notices: ");
                foreach (var n in session.Notices)
                    sb.AppendLine($"[{n.Severity}] {n.Message}");
                NoticesText.Text = sb.ToString();
            }
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Cancelled.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Unexpected error: {ex.Message}";
        }
        finally
        {
            _executionCancellation.Dispose();
            _executionCancellation = null;
            ExecuteButton.IsEnabled = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _executionCancellation?.Cancel();

    private async void Preview_Click(object sender, RoutedEventArgs e)
    {
        if (ResultSetSelector.SelectedItem is not ResultSetOption option) { FooterText.Text = "Select a result set first."; return; }
        if (!long.TryParse(StartRowText.Text, out var startRow) || !long.TryParse(EndRowText.Text, out var endRow) || !int.TryParse(StartColumnText.Text, out var startColumn) || !int.TryParse(EndColumnText.Text, out var endColumn)) { FooterText.Text = "Selection indexes must be numeric."; return; }
        try
        {
            var selection = new ResultSelection(startRow, endRow, startColumn, endColumn);
            var format = Enum.Parse<ResultSerializationFormat>(((ComboBoxItem)FormatSelector.SelectedItem).Content.ToString()!);
            var serializer = new ResultSerializer(new DefaultResultValueFormatter(), format);
            using var writer = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
            var outcome = await serializer.SerializeAsync(option.Store, selection, new ResultSerializationOptions(format, IncludeHeaders.IsChecked == true, 100_000), writer);
            SerializationPreview.Text = writer.ToString(); FooterText.Text = $"Serialized {outcome.RowsSerialized:N0} rows, {outcome.CharactersWritten:N0} characters ({outcome.StopReason?.ToString() ?? "complete"}).";
        }
        catch (Exception ex) { FooterText.Text = $"Serialization error: {ex.Message}"; }
    }

    private async void ResultSetSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ResultSetSelector.SelectedItem is not ResultSetOption opt || _session is null) return;
        try
        {
            // Page the first page in immediately so the user sees something.
            await LoadPageAsync(opt.Store, 0);
            FooterText.Text = $"Result set {opt.Store.ResultSetIndex} ({opt.Store.LoadedRowCount:N0} retained, {opt.Store.FinalRowCount:N0} final)";
        }
        catch (Exception ex)
        {
            FooterText.Text = $"Preview error: {ex.Message}";
        }
    }

    /// <summary>
    /// Loads a 100-row page starting at <paramref name="startRowIndex"/> from the store
    /// and projects it into a flat view-model list bound to the ListView. No WPF object
    /// per cell — view-model exposes a single formatted string per row.
    /// </summary>
    private async Task LoadPageAsync(IResultSetStore store, long startRowIndex)
    {
        var rows = await store.GetRowsAsync(startRowIndex, PageSize, CancellationToken.None);
        var viewModels = new PreviewRow[rows.Count];
        for (int i = 0; i < rows.Count; i++)
        {
            viewModels[i] = new PreviewRow(startRowIndex + i, FormatCells(rows[i]));
        }
        RowPreview.ItemsSource = viewModels;
    }

    private static string FormatCells(ResultRow row)
    {
        if (row.Cells.Count == 0) return string.Empty;
        var sb = new StringBuilder();
        for (int i = 0; i < row.Cells.Count; i++)
        {
            if (i > 0) sb.Append(" | ");
            var c = row.Cells[i];
            sb.Append(c.IsNull ? "NULL" : (c.Value?.ToString() ?? string.Empty));
        }
        return sb.ToString();
    }

    /// <summary>View-model for the preview list. Only one object per row; cell values are formatted lazily into a single string.</summary>
    private sealed record PreviewRow(long RowIndex, string Value);

    /// <summary>Combo-box item: a result-set store reference with a friendly label.</summary>
    private sealed record ResultSetOption(IResultSetStore Store)
    {
        public override string ToString() => $"#{Store.ResultSetIndex} — {Store.Schema.Columns.Count} cols, {Store.LoadedRowCount:N0} rows";
    }
}
