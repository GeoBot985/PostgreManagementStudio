using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;
using PostgreManagementStudio.Application;
using PostgreManagementStudio.Core;
using PostgreManagementStudio.Postgres;
using PostgreManagementStudio.Results;

namespace PostgreManagementStudio.Desktop;

public enum DataTransferWorkspaceMode { Import, Export }

public sealed class DataTransferWorkspaceWindow : Window
{
    private readonly DataTransferWorkspaceMode _mode;
    private readonly NpgsqlDataTransferService? _importService;
    private readonly IResultExportService? _exportService;
    private readonly TransferHistoryService _history;
    private readonly string _connectionString;
    private readonly IResultSetStore? _resultSet;
    private readonly TextBox _path = new() { MinWidth = 360 };
    private readonly TextBox _schema = new() { Width = 120, Text = "public" };
    private readonly TextBox _table = new() { Width = 160 };
    private readonly ComboBox _format = new() { Width = 130 };
    private readonly ComboBox _scope = new() { Width = 180 };
    private readonly ComboBox _existing = new() { Width = 120 };
    private readonly ComboBox _strategy = new() { Width = 150 };
    private readonly CheckBox _headers = new() { Content = "Include headers", IsChecked = true };
    private readonly CheckBox _continueErrors = new() { Content = "Continue and collect rejected rows" };
    private readonly TextBox _delimiter = new() { Width = 40, Text = "," };
    private readonly DataGrid _preview = new() { IsReadOnly = false, AutoGenerateColumns = false, CanUserSortColumns = true };
    private readonly DataGrid _historyGrid = new() { IsReadOnly = true, AutoGenerateColumns = false, CanUserSortColumns = true };
    private readonly TextBox _output = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button _browse = new() { Content = "Browse…", Width = 80 };
    private readonly Button _inspect = new() { Content = "Inspect / Preview", Width = 120 };
    private readonly Button _validate = new() { Content = "Validate plan", Width = 95 };
    private readonly Button _run = new() { Content = "Run transfer", Width = 100 };
    private readonly Button _cancel = new() { Content = "Cancel", Width = 80, IsEnabled = false };
    private readonly Button _clearHistory = new() { Content = "Clear history", Width = 90 };
    private readonly ObservableCollection<MappingRow> _mappings = new();
    private CancellationTokenSource? _cancellation;
    private IReadOnlyList<DestinationColumn> _destinationColumns = Array.Empty<DestinationColumn>();
    private DelimitedFileSettings _fileSettings = new();
    private long _configurationVersion;
    private long _validatedVersion = -1;
    private bool _closing;

    public DataTransferWorkspaceWindow(DataTransferWorkspaceMode mode, TransferHistoryService history, string connectionString,
        NpgsqlDataTransferService? importService = null, IResultExportService? exportService = null, IResultSetStore? resultSet = null)
    {
        _mode = mode; _history = history; _connectionString = connectionString; _importService = importService; _exportService = exportService; _resultSet = resultSet;
        Title = mode == DataTransferWorkspaceMode.Import ? "Import data into PostgreSQL" : "Export query result"; Width = 980; Height = 700; MinWidth = 740; MinHeight = 520; WindowStartupLocation = WindowStartupLocation.CenterOwner; Content = BuildContent();
        AutomationProperties.SetName(_path, mode == DataTransferWorkspaceMode.Import ? "Import source file" : "Export destination file"); AutomationProperties.SetName(_preview, "Transfer preview or mapping"); AutomationProperties.SetName(_output, "Transfer output"); AutomationProperties.SetName(_run, "Run transfer"); AutomationProperties.SetName(_cancel, "Cancel transfer");
        _browse.Click += (_, _) => Browse(); _inspect.Click += async (_, _) => await InspectAsync(); _validate.Click += (_, _) => ValidatePlan(); _run.Click += async (_, _) => await RunAsync(); _cancel.Click += (_, _) => _cancellation?.Cancel(); _clearHistory.Click += (_, _) => ClearHistory(); _path.TextChanged += (_, _) => InvalidatePlan(); _schema.TextChanged += (_, _) => InvalidatePlan(); _table.TextChanged += (_, _) => InvalidatePlan(); _existing.SelectionChanged += (_, _) => InvalidatePlan(); _strategy.SelectionChanged += (_, _) => InvalidatePlan(); _continueErrors.Checked += (_, _) => InvalidatePlan(); _continueErrors.Unchecked += (_, _) => InvalidatePlan(); Closed += (_, _) => CloseWorkspace(); RefreshHistory();
    }

    private UIElement BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(12) }; var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; actions.Children.Add(_validate); actions.Children.Add(_run); actions.Children.Add(_cancel); DockPanel.SetDock(actions, Dock.Bottom); root.Children.Add(actions);
        var panel = new StackPanel(); panel.Children.Add(new TextBlock { Text = Title, FontSize = 18, FontWeight = FontWeights.SemiBold });
        var source = new WrapPanel { Margin = new Thickness(0, 8, 0, 4) }; source.Children.Add(new TextBlock { Text = _mode == DataTransferWorkspaceMode.Import ? "Source file:" : "Destination file:", VerticalAlignment = VerticalAlignment.Center }); source.Children.Add(_path); source.Children.Add(_browse);
        if (_mode == DataTransferWorkspaceMode.Import) { source.Children.Add(new TextBlock { Text = "Schema:", Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center }); source.Children.Add(_schema); source.Children.Add(new TextBlock { Text = "Table:", Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center }); source.Children.Add(_table); } panel.Children.Add(source);
        if (_mode == DataTransferWorkspaceMode.Import) { var options = new WrapPanel(); options.Children.Add(new TextBlock { Text = "Existing data:", VerticalAlignment = VerticalAlignment.Center }); options.Children.Add(_existing); options.Children.Add(new TextBlock { Text = "Execution:", Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center }); options.Children.Add(_strategy); options.Children.Add(new Border { Child = _continueErrors, Margin = new Thickness(10, 0, 0, 0) }); panel.Children.Add(options); _existing.ItemsSource = Enum.GetValues<ExistingDataMode>(); _existing.SelectedItem = ExistingDataMode.Append; _strategy.ItemsSource = Enum.GetValues<ImportStrategy>(); _strategy.SelectedItem = ImportStrategy.Copy; } else { var options = new WrapPanel(); options.Children.Add(new TextBlock { Text = "Format:", VerticalAlignment = VerticalAlignment.Center }); options.Children.Add(_format); options.Children.Add(new TextBlock { Text = "Delimiter:", Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center }); options.Children.Add(_delimiter); options.Children.Add(new Border { Child = _headers, Margin = new Thickness(10, 0, 0, 0) }); panel.Children.Add(options); _format.ItemsSource = Enum.GetValues<ResultExportFormat>(); _format.SelectedItem = ResultExportFormat.Csv; _scope.ItemsSource = new[] { "All currently loaded rows" }; _scope.SelectedIndex = 0; options.Children.Add(new TextBlock { Text = "Scope:", Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center }); options.Children.Add(_scope); }
        panel.Children.Add(_status); _preview.Columns.Add(new DataGridTextColumn { Header = "Source column", Binding = new Binding(nameof(MappingRow.SourceName)) }); _preview.Columns.Add(new DataGridTextColumn { Header = "Destination column", Binding = new Binding(nameof(MappingRow.DestinationName)) }); _preview.Columns.Add(new DataGridTextColumn { Header = "Type", Binding = new Binding(nameof(MappingRow.DestinationType)) }); _preview.Columns.Add(new DataGridTextColumn { Header = "Mapping", Binding = new Binding(nameof(MappingRow.Status)) }); _preview.ItemsSource = _mappings;
        var tabs = new TabControl(); tabs.Items.Add(new TabItem { Header = _mode == DataTransferWorkspaceMode.Import ? "Mapping and preview" : "Export scope", Content = _preview }); tabs.Items.Add(new TabItem { Header = "Output", Content = _output }); foreach (var column in new[] { ("Started", "Started"), ("Operation", "Operation"), ("Status", "Status"), ("Read", "RowsRead"), ("Written", "RowsWritten"), ("Rejected", "RowsRejected") }) _historyGrid.Columns.Add(new DataGridTextColumn { Header = column.Item1, Binding = new Binding(column.Item2) }); tabs.Items.Add(new TabItem { Header = "Transfer history", Content = new DockPanel { LastChildFill = true, Children = { new Border { Child = _clearHistory, HorizontalAlignment = HorizontalAlignment.Right }, _historyGrid } } }); panel.Children.Add(tabs); root.Children.Add(panel); return root;
    }

    private void Browse()
    {
        if (_mode == DataTransferWorkspaceMode.Import) { var dialog = new OpenFileDialog { Filter = "Delimited files (*.csv;*.tsv;*.txt)|*.csv;*.tsv;*.txt|All files (*.*)|*.*" }; if (dialog.ShowDialog(this) == true) { _path.Text = dialog.FileName; _ = InspectAsync(); } }
        else { var dialog = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv|TSV (*.tsv)|*.tsv|JSON (*.json)|*.json|SQL inserts (*.sql)|*.sql", FileName = "query-results.csv" }; if (dialog.ShowDialog(this) == true) _path.Text = dialog.FileName; }
    }
    private async Task InspectAsync()
    {
        if (_mode == DataTransferWorkspaceMode.Export) { _status.Text = _resultSet is null ? "No result set is available." : $"Export source: {_resultSet.LoadedRowCount:N0} currently loaded row(s). The exporter streams only retained rows."; return; }
        try
        {
            SetBusy(true); _status.Text = "Inspecting bounded file preview…"; var path = _path.Text; var data = await Task.Run(() => ReadPreview(path)); _fileSettings = data.Settings; _mappings.Clear(); _destinationColumns = data.Headers.Select(x => new DestinationColumn(x, "text", true)).ToArray(); foreach (var header in data.Headers.Select((name, ordinal) => new { name, ordinal })) _mappings.Add(new MappingRow(header.name, header.name, "text", "Auto-mapped")); _preview.ItemsSource = _mode == DataTransferWorkspaceMode.Import ? _mappings : null; _status.Text = $"Detected {_fileSettings.Delimiter} delimiter; bounded preview contains {data.Rows.Count:N0} row(s). Mapping is editable before validation."; _configurationVersion++;
        }
        catch (Exception ex) { _status.Text = DesktopErrorPresentation.Failure("File inspection", ex); }
        finally { if (!_closing) SetBusy(false); }
    }
    private static (DelimitedFileSettings Settings, IReadOnlyList<string> Headers, IReadOnlyList<string[]> Rows) ReadPreview(string path)
    {
        var detected = DataFormatDetector.Detect(path); if (detected.Format is TransferFormat.Json or TransferFormat.JsonLines) throw new NotSupportedException("JSON import is available in the transfer service but this workspace currently accepts delimited files only."); var settings = detected.Settings with { HasHeader = true }; var all = new DelimitedFileReader().Read(path, settings).Take(100).ToArray(); var rawSettings = settings; var first = new DelimitedFileReader().Read(path, settings with { HasHeader = false }).Take(101).ToArray(); if (first.Length == 0) throw new InvalidOperationException("The selected file contains no rows."); return (rawSettings, first[0], all);
    }
    private void ValidatePlan()
    {
        try
        {
            if (_mode == DataTransferWorkspaceMode.Export) { if (_resultSet is null || string.IsNullOrWhiteSpace(_path.Text)) throw new InvalidOperationException("Choose an export source and destination path."); _validatedVersion = _configurationVersion; _status.Text = "Export plan is valid for all currently loaded rows."; return; }
            var mappings = _mappings.Select((x, i) => new ColumnMapping(i, string.IsNullOrWhiteSpace(x.DestinationName) ? null : x.DestinationName)).ToArray(); var mode = _existing.SelectedItem is ExistingDataMode.Truncate or ExistingDataMode.Delete ? ImportMode.Replace : ImportMode.Append; var plan = new ImportPlan(_path.Text, _schema.Text, _table.Text, mappings, mode, (_strategy.SelectedItem is ImportStrategy selected && selected == ImportStrategy.Copy) ? ImportExecutionMethod.Copy : ImportExecutionMethod.BatchedParameterisedInsert, _continueErrors.IsChecked == true ? TransactionMode.ContinueWithErrors : TransactionMode.AllRows, _continueErrors.IsChecked == true ? ImportErrorStrategy.ContinueAndCollectRejected : ImportErrorStrategy.StopOnFirstError, mode == ImportMode.Replace); var result = ImportPlanValidator.Validate(plan, _destinationColumns); if (result.Errors.Count > 0) throw new InvalidOperationException(string.Join(Environment.NewLine, result.Errors)); _validatedVersion = _configurationVersion; _status.Text = result.Warnings.Count == 0 ? "Import plan is valid and ready." : "Import plan is valid with warnings: " + string.Join(" ", result.Warnings);
        }
        catch (Exception ex) { _validatedVersion = -1; _status.Text = DesktopErrorPresentation.Failure("Transfer validation", ex); }
    }
    private async Task RunAsync()
    {
        if (_validatedVersion != _configurationVersion) { ValidatePlan(); if (_validatedVersion != _configurationVersion) return; } if (_cancellation is not null) return; _cancellation = new CancellationTokenSource(); var started = DateTimeOffset.UtcNow; SetBusy(true); _output.Clear();
        try
        {
            if (_mode == DataTransferWorkspaceMode.Export)
            {
                var format = _format.SelectedItem is ResultExportFormat f ? f : ResultExportFormat.Csv; var delimiter = string.IsNullOrEmpty(_delimiter.Text) ? "," : _delimiter.Text; var outcome = await _exportService!.ExportAsync(new ResultExportRequest(_resultSet!, null, format, ResultExportScope.EntireResult, _path.Text, new(_headers.IsChecked == true, delimiter)), new Progress<ResultExportProgress>(p => _status.Text = $"{p.Phase}: {p.RowsWritten:N0}/{p.TotalRows:N0}"), _cancellation.Token); _status.Text = outcome.Completed ? $"Completed: {outcome.RowsWritten:N0} row(s), {outcome.BytesWritten:N0} bytes." : "Export cancelled; temporary output was removed."; _history.Add(new(started, DateTimeOffset.UtcNow, "Export", "Loaded query result", outcome.Path, outcome.Completed ? "Completed" : "Cancelled", outcome.RowsWritten, outcome.RowsWritten, 0, outcome.Path, Array.Empty<string>()));
            }
            else
            {
                var mappings = _mappings.Select((x, i) => new ColumnMapping(i, string.IsNullOrWhiteSpace(x.DestinationName) ? null : x.DestinationName)).ToArray(); var request = new ImportRequest(_path.Text, _schema.Text, _table.Text, mappings, _fileSettings, new((_strategy.SelectedItem is ImportStrategy.BatchInsert) ? ImportStrategy.BatchInsert : ImportStrategy.Copy, _existing.SelectedItem is ExistingDataMode.Truncate ? ExistingDataMode.Truncate : _existing.SelectedItem is ExistingDataMode.Delete ? ExistingDataMode.Delete : ExistingDataMode.Append, _continueErrors.IsChecked == true ? TransactionMode.ContinueWithErrors : TransactionMode.AllRows, ContinueOnError: _continueErrors.IsChecked == true, ErrorLimit: 100), _destinationColumns); var result = await _importService!.ImportAsync(_connectionString, request, new Progress<ImportProgress>(p => _status.Text = $"{p.Phase}: {p.RowsWritten:N0} written, {p.RowsRejected:N0} rejected"), _cancellation.Token); _status.Text = $"{result.Status}: {result.RowsWritten:N0} written, {result.RowsRejected:N0} rejected."; _output.Text = string.Join(Environment.NewLine, result.Errors); _history.Add(new(started, DateTimeOffset.UtcNow, "Import", _path.Text, $"{_schema.Text}.{_table.Text}", result.Status, result.RowsRead, result.RowsWritten, result.RowsRejected, null, result.Errors));
            }
            RefreshHistory();
        }
        catch (OperationCanceledException) { _status.Text = "Cancellation requested; transfer stopped without starting another batch."; }
        catch (Exception ex) { _status.Text = DesktopErrorPresentation.Failure("Transfer", ex); }
        finally { _cancellation?.Dispose(); _cancellation = null; if (!_closing) SetBusy(false); }
    }
    private void RefreshHistory() => _historyGrid.ItemsSource = _history.Entries;
    private void ClearHistory() { if (MessageBox.Show(this, "Clear transfer history? This cannot be undone.", "Confirm clear history", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes) { _history.Clear(); RefreshHistory(); } }
    private void InvalidatePlan() { _configurationVersion++; if (!_closing && _validatedVersion >= 0) _status.Text = "Transfer plan is stale; validate again before execution."; }
    private void SetBusy(bool busy) { _browse.IsEnabled = !busy; _inspect.IsEnabled = !busy; _validate.IsEnabled = !busy; _run.IsEnabled = !busy; _cancel.IsEnabled = busy; }
    private void CloseWorkspace() { if (_closing) return; _closing = true; _cancellation?.Cancel(); }
    private sealed class MappingRow(string sourceName, string destinationName, string destinationType, string status) { public string SourceName { get; } = sourceName; public string DestinationName { get; set; } = destinationName; public string DestinationType { get; } = destinationType; public string Status { get; } = status; }
}
