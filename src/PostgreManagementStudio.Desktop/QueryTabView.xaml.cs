using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PostgreManagementStudio.Application;
using PostgreManagementStudio.Core;
using PostgreManagementStudio.Results;

namespace PostgreManagementStudio.Desktop;

public partial class QueryTabView : UserControl
{
    private readonly QueryDocument _document; public event EventHandler? DirtyChanged;
    private readonly DocumentFileService _fileService = new(); private SqlDocument _file = new() { DisplayName = "Query" };
    public QueryTabView(QueryDocument document) { InitializeComponent(); _document = document; SqlText.Text = document.SqlText; DatabaseText.Text = document.Database; }
    private async void Execute_Click(object sender, RoutedEventArgs e) { await ExecuteAsync(); }
    private async Task ExecuteAsync()
    {
        _document.SqlText = SqlText.Text; _document.Database = DatabaseText.Text; var selected = SqlText.SelectionLength > 0 ? SqlText.SelectedText : null; ExecuteButton.IsEnabled = false; StatusText.Text = "Running…"; MessagesText.Clear();
        try { var session = await _document.ExecuteAsync(selected); var output = new StringBuilder(_document.Message); ResultTabs.Items.Clear(); if (session is not null) { output.AppendLine(); foreach (var notice in session.Notices) output.AppendLine($"NOTICE [{notice.Severity}]: {notice.Message}"); for (var resultIndex = 0; resultIndex < session.ResultSets.Count; resultIndex++) { var store = session.ResultSets[resultIndex]; var rows = await store.GetRowsAsync(0, (int)Math.Min(store.LoadedRowCount, 10_000), CancellationToken.None); ResultTabs.Items.Add(CreateResultTab(store, rows)); if (store.LoadedRowCount > 10_000) output.AppendLine("Result display limited to 10,000 rows."); } if (session.ResultSets.Count > 0) ResultSummary.Text = string.Join(" | ", session.ResultSets.Select((s, i) => $"Results {i + 1}: {s.LoadedRowCount:N0} rows · {s.Schema.Columns.Count} columns")); } MessagesText.Text = output.ToString(); StatusText.Text = _document.State.ToString(); }
        catch (Exception ex) { StatusText.Text = "Error"; MessagesText.Text = ex.Message; } finally { ExecuteButton.IsEnabled = true; DirtyChanged?.Invoke(this, EventArgs.Empty); }
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) => _document.Cancel();
    private TabItem CreateResultTab(IResultSetStore store, IReadOnlyList<ResultRow> rows)
    {
        var view = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, CanUserResizeColumns = true, SelectionUnit = DataGridSelectionUnit.CellOrRowHeader, HeadersVisibility = DataGridHeadersVisibility.All, EnableRowVirtualization = true, EnableColumnVirtualization = true };
        view.Columns.Add(new DataGridTextColumn { Header = "#", Binding = new System.Windows.Data.Binding("RowIndex"), Width = 55 });
        for (var column = 0; column < store.Schema.Columns.Count; column++) view.Columns.Add(new DataGridTextColumn { Header = $"{store.Schema.Columns[column].Name}\n{store.Schema.Columns[column].PostgreSqlTypeName}", Binding = new System.Windows.Data.Binding($"Values[{column}]"), Width = new DataGridLength(1, DataGridLengthUnitType.Star), MinWidth = 80, MaxWidth = 420 });
        view.ItemsSource = rows.Select((row, index) => new GridRow(index, row.Cells.Select((cell, i) => new DefaultResultValueFormatter().FormatForDisplay(cell, store.Schema.Columns[i], new(512))).ToArray())).ToArray(); return new TabItem { Header = $"Results {store.ResultSetIndex + 1}", Content = view, Tag = view };
    }
    private void Copy_Click(object sender, RoutedEventArgs e) => CopyGrid(false);
    private void CopyWithHeaders_Click(object sender, RoutedEventArgs e) => CopyGrid(true);
    private void CopyGrid(bool headers) { if (ResultTabs.SelectedItem is not TabItem tab || tab.Tag is not DataGrid grid) return; var lines = new List<string>(); if (headers) lines.Add(string.Join("\t", grid.Columns.Skip(1).Select(c => c.Header?.ToString()?.Split('\n')[0]))); foreach (var item in grid.SelectedItems.Cast<GridRow>()) lines.Add(string.Join("\t", item.Values)); if (lines.Count > 0) Clipboard.SetText(string.Join(Environment.NewLine, lines)); }
    private async void Open_Click(object sender, RoutedEventArgs e) { var dialog = new OpenFileDialog { Filter = "SQL files (*.sql)|*.sql|All files (*.*)|*.*", Multiselect = false }; if (dialog.ShowDialog() != true) return; try { var loaded = await _fileService.LoadAsync(dialog.FileName); _file = SqlDocument.FromLoaded(loaded); SqlText.Text = _file.Text; StatusText.Text = $"Opened {dialog.FileName}"; } catch (Exception ex) { MessagesText.Text = ex.Message; } }
    private async void Save_Click(object sender, RoutedEventArgs e) { if (_file.FilePath is null) { await SaveAsAsync(); return; } await SaveToAsync(_file.FilePath); }
    private async void SaveAs_Click(object sender, RoutedEventArgs e) => await SaveAsAsync();
    private async Task SaveAsAsync() { var dialog = new SaveFileDialog { Filter = "SQL files (*.sql)|*.sql|All files (*.*)|*.*", DefaultExt = ".sql", AddExtension = true }; if (dialog.ShowDialog() == true) await SaveToAsync(dialog.FileName); }
    private async Task SaveToAsync(string path) { try { _file.SetText(SqlText.Text); await _fileService.SaveAsync(_file, path); StatusText.Text = $"Saved {path}"; DirtyChanged?.Invoke(this, EventArgs.Empty); } catch (Exception ex) { MessagesText.Text = $"Save failed: {ex.Message}"; } }
    private async void Reload_Click(object sender, RoutedEventArgs e) { if (_file.FilePath is null) return; try { var loaded = await _fileService.LoadAsync(_file.FilePath); _file = SqlDocument.FromLoaded(loaded); SqlText.Text = _file.Text; StatusText.Text = "Reloaded from disk."; } catch (Exception ex) { MessagesText.Text = ex.Message; } }
    private void FindNext_Click(object sender, RoutedEventArgs e) { if (string.IsNullOrEmpty(FindText.Text)) return; var index = new FindReplaceService().FindNext(SqlText.Text, FindText.Text, SqlText.SelectionStart + SqlText.SelectionLength, new()); if (index >= 0) { SqlText.Select(index, FindText.Text.Length); SqlText.Focus(); } else StatusText.Text = "No match."; }
    private void ReplaceAll_Click(object sender, RoutedEventArgs e) { if (string.IsNullOrEmpty(FindText.Text)) return; var service = new FindReplaceService(); var result = service.ReplaceAll(SqlText.Text, FindText.Text, ReplaceText.Text, new(), out var count); if (count > 0) SqlText.Text = result; StatusText.Text = $"{count} replacements made."; }
    private void GoToLine_Click(object sender, RoutedEventArgs e) { var dialog = new InputDialog("Go to line", "Line number:"); if (dialog.ShowDialog() != true || !int.TryParse(dialog.Value, out var line) || line < 1) { StatusText.Text = "Enter a positive line number."; return; } var index = 0; for (var i = 1; i < line && index < SqlText.Text.Length; i++) index = SqlText.Text.IndexOf('\n', index) + 1; if (index <= 0 && line > 1) { StatusText.Text = "Line is beyond the document."; return; } SqlText.Focus(); SqlText.CaretIndex = index; }
    private void SqlText_TextChanged(object sender, TextChangedEventArgs e) { _document.SqlText = SqlText.Text; _document.MarkDirty(); DirtyChanged?.Invoke(this, EventArgs.Empty); }
    private async void UserControl_KeyDown(object sender, System.Windows.Input.KeyEventArgs e) { if (e.Key == System.Windows.Input.Key.F5 || (e.Key == System.Windows.Input.Key.Enter && System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control)) { _ = ExecuteAsync(); e.Handled = true; } else if (e.Key == System.Windows.Input.Key.Escape) _document.Cancel(); else if (e.Key == System.Windows.Input.Key.Space && System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control) { var items = await new SqlCompletionEngine().GetCompletionsAsync(SqlText.Text, SqlText.CaretIndex, null); var menu = new ContextMenu(); foreach (var item in items.Take(30)) { var entry = new MenuItem { Header = $"{item.DisplayText} [{item.Kind}]" }; entry.Click += (_, _) => { var start = SqlText.CaretIndex; while (start > 0 && (char.IsLetterOrDigit(SqlText.Text[start - 1]) || SqlText.Text[start - 1] == '_')) start--; SqlText.Select(start, SqlText.CaretIndex - start); SqlText.SelectedText = item.InsertionText; }; menu.Items.Add(entry); } menu.IsOpen = true; e.Handled = true; } }
    private sealed record GridRow(long RowIndex, IReadOnlyList<string> Values);
    private sealed class InputDialog : Window { public string Value => Box.Text; private readonly TextBox Box = new(); public InputDialog(string title, string prompt) { Title = title; Width = 300; Height = 130; WindowStartupLocation = WindowStartupLocation.CenterOwner; var panel = new StackPanel { Margin = new Thickness(10) }; panel.Children.Add(new TextBlock { Text = prompt }); panel.Children.Add(Box); var button = new Button { Content = "OK", IsDefault = true, Margin = new Thickness(0, 8, 0, 0) }; button.Click += (_, _) => DialogResult = true; panel.Children.Add(button); Content = panel; } }
}
