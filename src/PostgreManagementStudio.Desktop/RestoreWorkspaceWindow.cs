using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Microsoft.Win32;
using PostgreManagementStudio.Application;
using PostgreManagementStudio.Core;

namespace PostgreManagementStudio.Desktop;

public sealed class RestoreWorkspaceWindow : Window
{
    private readonly BackupRestoreOperationService _operations;
    private readonly PostgreSqlToolDiscoveryService _tools;
    private readonly BackupInspectionService _inspection;
    private readonly DestructiveOperationGuard _destructive;
    private readonly DatabaseConnection _connection;
    private readonly string _connectionString;
    private readonly string _profileId;
    private readonly string _serverIdentity;
    private readonly string? _environment;
    private readonly BackupRestoreOperationController _controller = new();
    private readonly TextBox _source = new() { MinWidth = 420 };
    private readonly TextBlock _sourceSummary = new() { TextWrapping = TextWrapping.Wrap, MinHeight = 48 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBox _output = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = 150 };
    private readonly Button _inspect = new() { Content = "Inspect source", Width = 120 };
    private readonly Button _restore = new() { Content = "Restore", Width = 100, IsEnabled = false };
    private readonly Button _cancel = new() { Content = "Cancel operation", Width = 120, IsEnabled = false };
    private readonly CheckBox _clean = new() { Content = "Clean existing objects" };
    private readonly CheckBox _createDatabase = new() { Content = "Create database" };
    private readonly CheckBox _dataOnly = new() { Content = "Data only" };
    private readonly CheckBox _schemaOnly = new() { Content = "Schema only" };
    private readonly CheckBox _noOwner = new() { Content = "Do not restore ownership" };
    private readonly CheckBox _noPrivileges = new() { Content = "Do not restore privileges" };
    private readonly CheckBox _singleTransaction = new() { Content = "Single transaction where supported", IsChecked = true };
    private readonly CheckBox _verbose = new() { Content = "Verbose tool output" };
    private BackupInspectionResult? _inspectionResult;
    private ValidatedPostgreSqlTools? _validatedTools;
    private RestoreOperationPlan? _plan;
    private bool _closing;

    public RestoreWorkspaceWindow(
        BackupRestoreOperationService operations,
        PostgreSqlToolDiscoveryService tools,
        BackupInspectionService inspection,
        DestructiveOperationGuard destructive,
        DatabaseConnection connection,
        string connectionString,
        string profileId,
        string serverIdentity,
        string? environment)
    {
        _operations = operations;
        _tools = tools;
        _inspection = inspection;
        _destructive = destructive;
        _connection = connection;
        _connectionString = connectionString;
        _profileId = profileId;
        _serverIdentity = serverIdentity;
        _environment = environment;

        Title = "Restore PostgreSQL database";
        Width = 760;
        Height = 680;
        MinWidth = 650;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = BuildContent();
        AutomationProperties.SetName(_source, "Backup source");
        AutomationProperties.SetName(_output, "Restore output");
        AutomationProperties.SetName(_inspect, "Inspect backup source");
        AutomationProperties.SetName(_restore, "Restore database");
        AutomationProperties.SetName(_cancel, "Cancel restore");
        _source.TextChanged += (_, _) => InvalidatePlan("Select and inspect a backup source.");
        _inspect.Click += async (_, _) => await InspectAsync();
        _restore.Click += async (_, _) => await RestoreAsync();
        _cancel.Click += (_, _) => _controller.Cancel();
        foreach (var box in new[] { _clean, _createDatabase, _dataOnly, _schemaOnly, _noOwner, _noPrivileges, _singleTransaction, _verbose })
            box.Checked += (_, _) => InvalidatePlan("Options changed. Inspect the source again before restoring.");
        foreach (var box in new[] { _clean, _createDatabase, _dataOnly, _schemaOnly, _noOwner, _noPrivileges, _singleTransaction, _verbose })
            box.Unchecked += (_, _) => InvalidatePlan("Options changed. Inspect the source again before restoring.");
        Closed += (_, _) => CloseWorkspace();
        _status.Text = "No backup selected. Select a source and inspect it before restoring.";
    }

    private UIElement BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(14) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(_inspect);
        buttons.Children.Add(_restore);
        buttons.Children.Add(_cancel);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "Restore workspace", FontSize = 18, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = "Review the source, exact target, restore options and destructive consequences before execution.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 12) });
        panel.Children.Add(new TextBlock { Text = "Target", FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = $"Server: {_connection.Host}:{_connection.Port}\nDatabase: {_connection.Database}\nUser: {_connection.Username}", Margin = new Thickness(0, 4, 0, 12) });
        panel.Children.Add(new TextBlock { Text = "Backup source", FontWeight = FontWeights.SemiBold });
        var sourceRow = new StackPanel { Orientation = Orientation.Horizontal };
        sourceRow.Children.Add(_source);
        var browse = new Button { Content = "Browse…", Width = 90, Margin = new Thickness(8, 0, 0, 0) };
        browse.Click += (_, _) => BrowseSource();
        sourceRow.Children.Add(browse);
        panel.Children.Add(sourceRow);
        panel.Children.Add(_sourceSummary);
        panel.Children.Add(new TextBlock { Text = "Restore options", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 4) });
        var options = new WrapPanel();
        foreach (var box in new[] { _clean, _createDatabase, _dataOnly, _schemaOnly, _noOwner, _noPrivileges, _singleTransaction, _verbose })
            options.Children.Add(new Border { Child = box, Margin = new Thickness(0, 0, 16, 6) });
        panel.Children.Add(options);
        panel.Children.Add(new TextBlock { Text = "Status", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 4) });
        panel.Children.Add(_status);
        panel.Children.Add(new TextBlock { Text = "Output and diagnostics", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 4) });
        panel.Children.Add(_output);
        scroll.Content = panel;
        root.Children.Add(scroll);
        return root;
    }

    private void BrowseSource()
    {
        var dialog = new OpenFileDialog { Filter = "PostgreSQL backups (*.backup;*.sql;*.tar)|*.backup;*.sql;*.tar|All files (*.*)|*.*", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true) _source.Text = dialog.FileName;
    }

    private async Task InspectAsync()
    {
        if (string.IsNullOrWhiteSpace(_source.Text)) { SetStatus("No backup selected."); return; }
        SetBusy(true, "Validating source and PostgreSQL restore tools…");
        try
        {
            _validatedTools = await _tools.DiscoverAsync();
            var detected = BackupInspectionService.DetectFormat(_source.Text);
            if (detected is null) throw new BackupRestoreException(BackupRestoreFailureCategory.InvalidBackup, "The selected source is not a recognised PostgreSQL backup.");
            _inspectionResult = await _inspection.InspectAsync(_source.Text, detected.Value, _validatedTools.Paths);
            if (!_inspectionResult.IsValid) { _plan = null; _restore.IsEnabled = false; _sourceSummary.Text = _inspectionResult.Warning ?? "The backup source is invalid."; SetStatus("Invalid backup source."); return; }
            _sourceSummary.Text = $"Format: {_inspectionResult.Format}\nSize: {_inspectionResult.SizeBytes:N0} bytes\nObjects inspected: {_inspectionResult.ObjectCount:N0}" +
                (string.IsNullOrWhiteSpace(_inspectionResult.SourceDatabase) ? "" : $"\nSource database: {_inspectionResult.SourceDatabase}");
            _plan = BuildPlan();
            _restore.IsEnabled = true;
            SetStatus(_inspectionResult.Warning is null ? "Ready to restore. Review the exact target and confirm the destructive action." : $"Ready with warning: {_inspectionResult.Warning}");
        }
        catch (OperationCanceledException) { SetStatus("Inspection cancelled."); }
        catch (Exception ex) { _plan = null; _restore.IsEnabled = false; SetStatus(DesktopErrorPresentation.Failure("Backup inspection", ex)); }
        finally { SetBusy(false); }
    }

    private RestoreOperationPlan BuildPlan()
    {
        if (_inspectionResult is null || _validatedTools is null) throw new InvalidOperationException("Inspect the source first.");
        return BackupOperationPlanFactory.CreateRestore(_profileId, _serverIdentity,
            new RestoreOptions(_connection, _source.Text, _inspectionResult.Format,
                _clean.IsChecked == true, _createDatabase.IsChecked == true, _dataOnly.IsChecked == true,
                _schemaOnly.IsChecked == true, _noOwner.IsChecked == true, _noPrivileges.IsChecked == true,
                ExitOnError: true, _singleTransaction.IsChecked == true, _verbose.IsChecked == true),
            _inspectionResult, _validatedTools, null);
    }

    private async Task RestoreAsync()
    {
        if (_plan is null) { SetStatus("Inspect the source before restoring."); return; }
        try
        {
            var plan = BuildPlan();
            if (!_destructive.Confirm(new(DestructiveOperationKind.Restore, "Confirm PostgreSQL restore", plan.Connection.Database,
                RestoreConfirmation.Summary(plan), "Verify the backup and target before continuing. A failed or cancelled restore may leave partial changes.",
                plan.Connection.Host, plan.Connection.Database, plan.Connection.Database, _environment,
                SessionIdentityCertain: true))) { SetStatus("Restore cancelled before execution."); return; }
            _plan = plan;
            SetBusy(true, "Restore running…");
            _output.Clear();
            var result = await _operations.ExecuteRestoreAsync(plan, RestoreConfirmation.Create(plan), _controller,
                new Progress<ProcessOutputEntry>(entry => _output.AppendText($"{entry.Timestamp:HH:mm:ss} {(entry.IsError ? "ERR" : "OUT")} {BackupSecretRedactor.Redact(entry.Line)}{Environment.NewLine}")));
            if (result is null) { SetStatus("Restore result superseded by workspace shutdown."); return; }
            _status.Text = $"{result.State}: {BackupSecretRedactor.Redact(result.Message)}" +
                (result.TargetMayBePartiallyModified ? " Target may contain partial changes." : "");
            foreach (var warning in result.Warnings) _output.AppendText($"WARNING: {BackupSecretRedactor.Redact(warning)}{Environment.NewLine}");
        }
        catch (OperationCanceledException) { SetStatus("Restore cancelled. The target may contain partial changes."); }
        catch (Exception ex) { SetStatus(DesktopErrorPresentation.Failure("Restore", ex)); }
        finally { SetBusy(false); }
    }

    private void InvalidatePlan(string message)
    {
        if (_closing) return;
        _plan = null;
        _restore.IsEnabled = false;
        SetStatus(message);
    }

    private void SetBusy(bool busy, string? message = null)
    {
        _inspect.IsEnabled = !busy;
        _restore.IsEnabled = !busy && _plan is not null;
        _cancel.IsEnabled = busy;
        _source.IsEnabled = !busy;
        if (message is not null) _status.Text = message;
    }

    private void SetStatus(string value) => _status.Text = UntrustedText.ForDisplay(value, 4_096);

    private void CloseWorkspace()
    {
        if (_closing) return;
        _closing = true;
        _controller.Cancel();
        _ = _controller.DisposeAsync();
    }
}
