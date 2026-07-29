using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using PostgreManagementStudio.Application;
using PostgreManagementStudio.Postgres;

namespace PostgreManagementStudio.Desktop;

public sealed class IndexWorkspaceWindow : Window
{
    private readonly NpgsqlIndexAnalysisService _service;
    private readonly NpgsqlMaintenanceService _maintenance;
    private readonly DestructiveOperationGuard _guard;
    private readonly PostgresVersionService _versions;
    private readonly string _connectionString;
    private readonly string _server;
    private readonly string _database;
    private readonly TextBox _filter = new() { Width = 240 };
    private readonly CheckBox _validOnly = new() { Content = "Valid only", IsChecked = true };
    private readonly CheckBox _uniqueOnly = new() { Content = "Unique only" };
    private readonly DataGrid _grid = new() { IsReadOnly = true, AutoGenerateColumns = false, CanUserSortColumns = true, SelectionMode = DataGridSelectionMode.Single };
    private readonly TextBox _details = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button _refresh = new() { Content = "Refresh", Width = 80 };
    private readonly Button _reindex = new() { Content = "Reindex selected", Width = 125, IsEnabled = false };
    private readonly Button _copy = new() { Content = "Copy definition", Width = 120, IsEnabled = false };
    private CancellationTokenSource? _loadCancellation;
    private IReadOnlyList<IndexMetadata> _all = Array.Empty<IndexMetadata>();
    private bool _closing;

    public IndexWorkspaceWindow(NpgsqlIndexAnalysisService service, NpgsqlMaintenanceService maintenance, DestructiveOperationGuard guard,
        PostgresVersionService versions, string connectionString, string server, string database)
    {
        _service = service; _maintenance = maintenance; _guard = guard; _versions = versions; _connectionString = connectionString; _server = server; _database = database;
        Title = "Index management"; Width = 1050; Height = 680; MinWidth = 760; MinHeight = 500; WindowStartupLocation = WindowStartupLocation.CenterOwner; Content = BuildContent();
        AutomationProperties.SetName(_filter, "Index search"); AutomationProperties.SetName(_grid, "Index inventory"); AutomationProperties.SetName(_details, "Index details"); AutomationProperties.SetName(_refresh, "Refresh indexes"); AutomationProperties.SetName(_reindex, "Reindex selected index");
        _filter.TextChanged += (_, _) => ApplyFilter(); _validOnly.Checked += (_, _) => ApplyFilter(); _validOnly.Unchecked += (_, _) => ApplyFilter(); _uniqueOnly.Checked += (_, _) => ApplyFilter(); _uniqueOnly.Unchecked += (_, _) => ApplyFilter();
        _refresh.Click += async (_, _) => await LoadAsync(); _grid.SelectionChanged += (_, _) => SelectionChanged(); _copy.Click += (_, _) => CopyDefinition(); _reindex.Click += async (_, _) => await ReindexAsync();
        Closed += (_, _) => CloseWorkspace(); _ = LoadAsync();
    }

    private UIElement BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(12) }; var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; actions.Children.Add(_refresh); actions.Children.Add(_copy); actions.Children.Add(_reindex); DockPanel.SetDock(actions, Dock.Bottom); root.Children.Add(actions);
        var panel = new StackPanel(); panel.Children.Add(new TextBlock { Text = "Index management", FontSize = 18, FontWeight = FontWeights.SemiBold }); panel.Children.Add(new TextBlock { Text = $"Server: {_server}\nDatabase: {_database}", Margin = new Thickness(0, 4, 0, 8) });
        var filters = new WrapPanel(); filters.Children.Add(new TextBlock { Text = "Name/schema/table:", VerticalAlignment = VerticalAlignment.Center }); filters.Children.Add(_filter); filters.Children.Add(new Border { Child = _validOnly, Margin = new Thickness(12, 0, 0, 0) }); filters.Children.Add(new Border { Child = _uniqueOnly, Margin = new Thickness(12, 0, 0, 0) }); panel.Children.Add(filters); panel.Children.Add(_status);
        foreach (var column in new[] { ("Schema", "SchemaName"), ("Table", "TableName"), ("Index", "IndexName"), ("Method", "AccessMethod"), ("Unique", "IsUnique"), ("Valid", "IsValid"), ("Scans", "ScanCount"), ("Size", "SizeBytes") }) _grid.Columns.Add(new DataGridTextColumn { Header = column.Item1, Binding = new Binding(column.Item2) });
        var body = new Grid(); body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(3, GridUnitType.Star) }); body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); Grid.SetRow(_grid, 0); Grid.SetRow(_details, 1); body.Children.Add(_grid); body.Children.Add(_details); panel.Children.Add(body); root.Children.Add(panel); return root;
    }

    private async Task LoadAsync()
    {
        _loadCancellation?.Cancel(); _loadCancellation?.Dispose(); _loadCancellation = new CancellationTokenSource(); var token = _loadCancellation.Token; SetBusy(true); _status.Text = "Loading index metadata…";
        try { var snapshot = await _service.LoadAsync(_connectionString, token); await Dispatcher.InvokeAsync(() => { if (_closing || token.IsCancellationRequested) return; _all = snapshot.Indexes; ApplyFilter(); _status.Text = $"{_all.Count:N0} index(es) loaded. Statistics source: pg_stat_all_indexes."; }); }
        catch (OperationCanceledException) { if (!_closing) await Dispatcher.InvokeAsync(() => _status.Text = "Index refresh cancelled."); }
        catch (Exception ex) { if (!_closing) await Dispatcher.InvokeAsync(() => _status.Text = DesktopErrorPresentation.Failure("Index refresh", ex)); }
        finally { if (!_closing) await Dispatcher.InvokeAsync(() => SetBusy(false)); }
    }
    private void ApplyFilter()
    {
        var text = _filter.Text.Trim(); var results = _all.Where(x => (!_validOnly.IsChecked.GetValueOrDefault() || x.IsValid && x.IsReady && x.IsLive) && (!_uniqueOnly.IsChecked.GetValueOrDefault() || x.IsUnique) && (text.Length == 0 || $"{x.SchemaName}.{x.TableName}.{x.IndexName}".Contains(text, StringComparison.OrdinalIgnoreCase))).OrderBy(x => x.SchemaName).ThenBy(x => x.TableName).ThenBy(x => x.IndexName).ToArray(); _grid.ItemsSource = results; _status.Text = $"{results.Length:N0} matching index(es) of {_all.Count:N0}.";
    }
    private void SelectionChanged()
    {
        var item = _grid.SelectedItem as IndexMetadata; _copy.IsEnabled = item is not null; _reindex.IsEnabled = item is not null && item.IsValid && item.IsReady && !_closing; if (item is null) { _details.Text = "Select an index to inspect its definition and statistics."; return; } _details.Text = $"{item.SchemaName}.{item.IndexName}\nTable: {item.SchemaName}.{item.TableName}\nAccess method: {item.AccessMethod}\nDefinition: {item.Keys.FirstOrDefault()?.Expression ?? "unavailable"}\nUnique: {item.IsUnique}; Primary: {item.IsPrimary}; Constraint-backed: {item.IsConstraintBacked}\nValid: {item.IsValid}; Ready: {item.IsReady}; Live: {item.IsLive}\nSize: {item.SizeBytes:N0} bytes; Scans: {item.ScanCount:N0}\nUsage is observational and may reset with PostgreSQL statistics resets.";
    }
    private void CopyDefinition() { if (_grid.SelectedItem is not IndexMetadata item) return; try { Clipboard.SetText(item.Keys.FirstOrDefault()?.Expression ?? string.Empty); _status.Text = "Index definition copied."; } catch (Exception ex) { _status.Text = DesktopErrorPresentation.Failure("Copy definition", ex); } }
    private async Task ReindexAsync()
    {
        if (_grid.SelectedItem is not IndexMetadata item) return; var versionText = await _versions.GetVersionAsync(_connectionString); var major = int.TryParse(System.Text.RegularExpressions.Regex.Match(versionText, @"PostgreSQL\s+(\d+)").Groups[1].Value, out var parsed) ? parsed : 12; var plan = new MaintenancePlan(MaintenanceOperation.Reindex, new[] { new MaintenanceTarget(MaintenanceTargetKind.Index, item.IndexName, item.SchemaName) }, new(Verbose: true, Concurrent: major >= 12), new(major));
        if (!_guard.Confirm(new(DestructiveOperationKind.Maintenance, "Confirm index rebuild", _database, $"Reindex exact target {item.SchemaName}.{item.IndexName} on {_server}?\n\n{plan.Statements.Single()}", "Reindexing can hold locks or consume considerable resources.", _server, _database, item.IndexName, "", true))) return;
        try { SetBusy(true); var result = await _maintenance.ExecuteAsync(_connectionString, plan, new Progress<string>(line => _status.Text = line)); _status.Text = result.Status; await LoadAsync(); } catch (Exception ex) { _status.Text = DesktopErrorPresentation.Failure("Reindex", ex); } finally { if (!_closing) SetBusy(false); }
    }
    private void SetBusy(bool busy) { _refresh.IsEnabled = !busy; _filter.IsEnabled = !busy; _validOnly.IsEnabled = !busy; _uniqueOnly.IsEnabled = !busy; if (busy) _reindex.IsEnabled = false; }
    private void CloseWorkspace() { if (_closing) return; _closing = true; _loadCancellation?.Cancel(); _loadCancellation?.Dispose(); _loadCancellation = null; }
}
