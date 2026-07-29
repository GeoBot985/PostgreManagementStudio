using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using PostgreManagementStudio.Application;
using PostgreManagementStudio.Core;
using PostgreManagementStudio.Postgres;

namespace PostgreManagementStudio.Desktop;

public sealed class MaintenanceWorkspaceWindow : Window
{
    private readonly NpgsqlMaintenanceService _maintenance;
    private readonly PostgresVersionService _versions;
    private readonly DestructiveOperationGuard _guard;
    private readonly string _connectionString;
    private readonly DatabaseConnection _connection;
    private readonly string _environment;
    private readonly ComboBox _operation = new() { Width = 150 };
    private readonly CheckBox _analyze = new() { Content = "Analyze" };
    private readonly CheckBox _full = new() { Content = "Full" };
    private readonly CheckBox _freeze = new() { Content = "Freeze" };
    private readonly CheckBox _verbose = new() { Content = "Verbose" };
    private readonly CheckBox _concurrent = new() { Content = "Concurrent reindex" };
    private readonly CheckBox _clusterAll = new() { Content = "Cluster all" };
    private readonly TextBox _preview = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly TextBox _output = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button _previewButton = new() { Content = "Preview", Width = 90 };
    private readonly Button _runButton = new() { Content = "Run maintenance", Width = 130 };
    private readonly Button _cancelButton = new() { Content = "Cancel", Width = 90, IsEnabled = false };
    private CancellationTokenSource? _cancellation;
    private bool _closing;

    public MaintenanceWorkspaceWindow(NpgsqlMaintenanceService maintenance, PostgresVersionService versions,
        DestructiveOperationGuard guard, string connectionString, DatabaseConnection connection, string environment)
    {
        _maintenance = maintenance; _versions = versions; _guard = guard; _connectionString = connectionString;
        _connection = connection; _environment = environment;
        Title = "PostgreSQL maintenance"; Width = 760; Height = 620; MinWidth = 620; MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; Content = BuildContent();
        _operation.ItemsSource = Enum.GetValues<MaintenanceOperation>(); _operation.SelectedItem = MaintenanceOperation.Vacuum;
        _analyze.IsChecked = true; _verbose.IsChecked = true;
        AutomationProperties.SetName(_operation, "Maintenance operation"); AutomationProperties.SetName(_preview, "Maintenance preview");
        AutomationProperties.SetName(_output, "Maintenance output"); AutomationProperties.SetName(_runButton, "Run maintenance");
        AutomationProperties.SetName(_cancelButton, "Cancel maintenance");
        _operation.SelectionChanged += (_, _) => UpdatePreview(); _previewButton.Click += (_, _) => UpdatePreview();
        _runButton.Click += async (_, _) => await RunAsync(); _cancelButton.Click += (_, _) => _cancellation?.Cancel();
        Closed += (_, _) => CloseWorkspace(); UpdatePreview();
    }

    private UIElement BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(14) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        actions.Children.Add(_previewButton); actions.Children.Add(_runButton); actions.Children.Add(_cancelButton); DockPanel.SetDock(actions, Dock.Bottom); root.Children.Add(actions);
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "Database maintenance", FontSize = 18, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = $"Server: {_connection.Host}:{_connection.Port}\nDatabase: {_connection.Database}\nEnvironment: {_environment}", Margin = new Thickness(0, 4, 0, 10) });
        var choices = new WrapPanel(); choices.Children.Add(new TextBlock { Text = "Operation:", VerticalAlignment = VerticalAlignment.Center }); choices.Children.Add(_operation);
        choices.Children.Add(new Border { Child = _analyze, Margin = new Thickness(12, 0, 0, 0) }); choices.Children.Add(new Border { Child = _full, Margin = new Thickness(12, 0, 0, 0) });
        choices.Children.Add(new Border { Child = _freeze, Margin = new Thickness(12, 0, 0, 0) }); choices.Children.Add(new Border { Child = _verbose, Margin = new Thickness(12, 0, 0, 0) });
        choices.Children.Add(new Border { Child = _concurrent, Margin = new Thickness(12, 0, 0, 0) }); choices.Children.Add(new Border { Child = _clusterAll, Margin = new Thickness(12, 0, 0, 0) });
        panel.Children.Add(choices); panel.Children.Add(_status); panel.Children.Add(new TextBlock { Text = "SQL preview", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 2) }); panel.Children.Add(_preview);
        panel.Children.Add(new TextBlock { Text = "Operation output", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 2) }); panel.Children.Add(_output); root.Children.Add(panel); return root;
    }

    private async void UpdatePreview()
    {
        try
        {
            var inputs = CaptureInputs();
            var major = await GetMajorVersionAsync();
            var plan = BuildPlan(major, inputs);
            await Dispatcher.InvokeAsync(() =>
            {
                if (_closing) return;
                _preview.Text = string.Join(Environment.NewLine, plan.Statements);
                _status.Text = plan.IsHighImpact ? "High-impact operation: confirmation will identify the exact target." : "Review the target and SQL before running.";
            });
        }
        catch (Exception ex) { if (!_closing) await Dispatcher.InvokeAsync(() => { _preview.Text = string.Empty; _status.Text = DesktopErrorPresentation.Failure("Maintenance validation", ex); }); }
    }

    private async Task RunAsync()
    {
        if (_cancellation is not null) return;
        try
        {
            var inputs = CaptureInputs();
            var plan = BuildPlan(await GetMajorVersionAsync(), inputs);
            if (!_guard.Confirm(new(DestructiveOperationKind.Maintenance, "Confirm PostgreSQL maintenance", _connection.Database,
                $"Run {plan.Operation} against {_connection.Host}:{_connection.Port}/{_connection.Database}?{Environment.NewLine}{Environment.NewLine}{string.Join(Environment.NewLine, plan.Statements)}",
                "Maintenance may hold locks or affect performance. Cancel if the target is not exact.", _connection.Host, _connection.Database, _connection.Database, _environment, true))) return;
            _cancellation = new CancellationTokenSource(); SetBusy(true); _output.Clear(); var started = DateTimeOffset.UtcNow;
            var result = await _maintenance.ExecuteAsync(_connectionString, plan, new Progress<string>(line => _output.AppendText(line + Environment.NewLine)), _cancellation.Token);
            _status.Text = $"{result.Status} in {result.Elapsed:g}. Completed {result.Targets.Count(x => x.Succeeded):N0}/{result.Targets.Count:N0} target(s).";
            foreach (var message in result.Messages) _output.AppendText(message + Environment.NewLine);
        }
        catch (OperationCanceledException) { _status.Text = "Cancellation requested; PostgreSQL will report the terminal outcome."; }
        catch (Exception ex) { _status.Text = DesktopErrorPresentation.Failure("Maintenance", ex); }
        finally { _cancellation?.Dispose(); _cancellation = null; if (!_closing) SetBusy(false); }
    }

    private (MaintenanceOperation Operation, MaintenanceOptions Options) CaptureInputs()
    {
        var operation = _operation.SelectedItem is MaintenanceOperation selected ? selected : MaintenanceOperation.Vacuum;
        return (operation, new(_analyze.IsChecked == true, _full.IsChecked == true, _freeze.IsChecked == true,
            _verbose.IsChecked == true, Concurrent: _concurrent.IsChecked == true, ClusterAll: _clusterAll.IsChecked == true));
    }

    private MaintenancePlan BuildPlan(int majorVersion, (MaintenanceOperation Operation, MaintenanceOptions Options) inputs)
    {
        var target = new MaintenanceTarget(MaintenanceTargetKind.Database, _connection.Database);
        return new(inputs.Operation, new[] { target }, inputs.Options, new(majorVersion));
    }

    private async Task<int> GetMajorVersionAsync()
    {
        var version = await _versions.GetVersionAsync(_connectionString);
        var match = System.Text.RegularExpressions.Regex.Match(version, @"PostgreSQL\s+(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups[1].Value, out var major) ? major : 12;
    }

    private void SetBusy(bool busy) { _operation.IsEnabled = !busy; _previewButton.IsEnabled = !busy; _runButton.IsEnabled = !busy; _cancelButton.IsEnabled = busy; }
    private void CloseWorkspace() { if (_closing) return; _closing = true; _cancellation?.Cancel(); }
}
