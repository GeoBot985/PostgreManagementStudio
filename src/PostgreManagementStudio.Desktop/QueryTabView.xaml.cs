using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PostgreManagementStudio.Application;
using PostgreManagementStudio.Core;

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
        try { var session = await _document.ExecuteAsync(selected); var output = new StringBuilder(_document.Message); if (session is not null) { output.AppendLine(); foreach (var notice in session.Notices) output.AppendLine($"NOTICE [{notice.Severity}]: {notice.Message}"); if (session.ResultSets.Count > 0) { var store = session.ResultSets[0]; var rows = await store.GetRowsAsync(0, (int)Math.Min(store.LoadedRowCount, 10_000), CancellationToken.None); RowsView.ItemsSource = rows.Select((r, i) => new PreviewRow(i, string.Join(" | ", r.Cells.Select(c => c.IsNull ? "NULL" : c.Value?.ToString() ?? string.Empty)))).ToArray(); if (store.LoadedRowCount > 10_000) output.AppendLine("Result display limited to 10,000 rows."); } } MessagesText.Text = output.ToString(); StatusText.Text = _document.State.ToString(); }
        catch (Exception ex) { StatusText.Text = "Error"; MessagesText.Text = ex.Message; } finally { ExecuteButton.IsEnabled = true; DirtyChanged?.Invoke(this, EventArgs.Empty); }
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) => _document.Cancel();
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
    private void UserControl_KeyDown(object sender, System.Windows.Input.KeyEventArgs e) { if (e.Key == System.Windows.Input.Key.F5 || (e.Key == System.Windows.Input.Key.Enter && System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control)) { _ = ExecuteAsync(); e.Handled = true; } else if (e.Key == System.Windows.Input.Key.Escape) _document.Cancel(); }
    private sealed record PreviewRow(long RowIndex, string Value);
    private sealed class InputDialog : Window { public string Value => Box.Text; private readonly TextBox Box = new(); public InputDialog(string title, string prompt) { Title = title; Width = 300; Height = 130; WindowStartupLocation = WindowStartupLocation.CenterOwner; var panel = new StackPanel { Margin = new Thickness(10) }; panel.Children.Add(new TextBlock { Text = prompt }); panel.Children.Add(Box); var button = new Button { Content = "OK", IsDefault = true, Margin = new Thickness(0, 8, 0, 0) }; button.Click += (_, _) => DialogResult = true; panel.Children.Add(button); Content = panel; } }
}
