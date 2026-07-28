using System.Text;
using System.Windows;
using System.Windows.Controls;
using PostgreManagementStudio.Application;
using PostgreManagementStudio.Core;

namespace PostgreManagementStudio.Desktop;

public partial class QueryTabView : UserControl
{
    private readonly QueryDocument _document; public event EventHandler? DirtyChanged;
    public QueryTabView(QueryDocument document) { InitializeComponent(); _document = document; SqlText.Text = document.SqlText; DatabaseText.Text = document.Database; }
    private async void Execute_Click(object sender, RoutedEventArgs e) { await ExecuteAsync(); }
    private async Task ExecuteAsync()
    {
        _document.SqlText = SqlText.Text; _document.Database = DatabaseText.Text; var selected = SqlText.SelectionLength > 0 ? SqlText.SelectedText : null; ExecuteButton.IsEnabled = false; StatusText.Text = "Running…"; MessagesText.Clear();
        try { var session = await _document.ExecuteAsync(selected); var output = new StringBuilder(_document.Message); if (session is not null) { output.AppendLine(); foreach (var notice in session.Notices) output.AppendLine($"NOTICE [{notice.Severity}]: {notice.Message}"); if (session.ResultSets.Count > 0) { var store = session.ResultSets[0]; var rows = await store.GetRowsAsync(0, (int)Math.Min(store.LoadedRowCount, 10_000), CancellationToken.None); RowsView.ItemsSource = rows.Select((r, i) => new PreviewRow(i, string.Join(" | ", r.Cells.Select(c => c.IsNull ? "NULL" : c.Value?.ToString() ?? string.Empty)))).ToArray(); if (store.LoadedRowCount > 10_000) output.AppendLine("Result display limited to 10,000 rows."); } } MessagesText.Text = output.ToString(); StatusText.Text = _document.State.ToString(); }
        catch (Exception ex) { StatusText.Text = "Error"; MessagesText.Text = ex.Message; } finally { ExecuteButton.IsEnabled = true; DirtyChanged?.Invoke(this, EventArgs.Empty); }
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) => _document.Cancel();
    private void SqlText_TextChanged(object sender, TextChangedEventArgs e) { _document.SqlText = SqlText.Text; _document.MarkDirty(); DirtyChanged?.Invoke(this, EventArgs.Empty); }
    private void UserControl_KeyDown(object sender, System.Windows.Input.KeyEventArgs e) { if (e.Key == System.Windows.Input.Key.F5 || (e.Key == System.Windows.Input.Key.Enter && System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control)) { _ = ExecuteAsync(); e.Handled = true; } else if (e.Key == System.Windows.Input.Key.Escape) _document.Cancel(); }
    private sealed record PreviewRow(long RowIndex, string Value);
}
