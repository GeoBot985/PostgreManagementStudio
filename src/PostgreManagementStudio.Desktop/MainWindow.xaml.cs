using System.ComponentModel;
using System.Data.Common;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PostgreManagementStudio.Application;
using PostgreManagementStudio.Core;
using PostgreManagementStudio.Postgres;
using PostgreManagementStudio.Results;

namespace PostgreManagementStudio.Desktop;

public partial class MainWindow : Window
{
    private readonly QueryTabManager _tabs;
    private readonly ObjectExplorerService _objectExplorer;
    private readonly ApplicationSettings _settings;
    private readonly DestructiveOperationGuard _destructiveOperations;
    private readonly BackupRestoreOperationService _backupRestore;
    private readonly PostgreSqlToolDiscoveryService _backupTools;
    private readonly BackupInspectionService _backupInspection;
    private readonly NpgsqlObjectSearchService _objectSearch;
    private readonly PostgresVersionService _postgresVersion;
    private readonly NpgsqlIndexAnalysisService _indexAnalysis;
    private readonly NpgsqlSchemaModelExtractor _schemaExtractor;
    private readonly NpgsqlDataTransferService _dataTransfer;
    private readonly IResultExportService _resultExport;
    private readonly TransferHistoryService _transferHistory;
    private readonly IConnectionProbe _connectionProbe;
    private readonly IConnectionRecoveryDiagnostics _connectionDiagnostics;
    private readonly IPerformanceDiagnostics _performanceDiagnostics;
    private readonly IConnectionProfileStore _connectionProfiles;
    private readonly CredentialLifecycleService _credentials;
    private readonly RecoverySnapshotService _recoverySnapshots;
    private readonly HashSet<ConnectionRecoverySession> _recoverySessions = [];
    private readonly HashSet<QueryTabView> _pendingRecoverySnapshots = [];
    private readonly DispatcherTimer _statusTimer;
    private readonly DispatcherTimer _recoveryTimer;
    private CancellationTokenSource? _metadataCancellation;
    private ShellConnectionInfo? _defaultConnection;
    private bool _shutdownInProgress;
    private bool _shutdownApproved;
    private int _statusTicks;
    private int _healthCheckRunning;
    private ObjectExplorerContext? _objectExplorerContext;

    public MainWindow(
        QueryTabManager tabs,
        ObjectExplorerService objectExplorer,
        DestructiveOperationGuard destructiveOperations,
        ApplicationSettings settings,
        BackupRestoreOperationService backupRestore,
        PostgreSqlToolDiscoveryService backupTools,
        BackupInspectionService backupInspection,
        NpgsqlObjectSearchService objectSearch,
        PostgresVersionService postgresVersion,
        NpgsqlIndexAnalysisService indexAnalysis,
        NpgsqlSchemaModelExtractor schemaExtractor,
        NpgsqlDataTransferService dataTransfer,
        IResultExportService resultExport,
        TransferHistoryService transferHistory,
        IConnectionProbe connectionProbe,
        IConnectionRecoveryDiagnostics connectionDiagnostics,
        IPerformanceDiagnostics performanceDiagnostics,
        IConnectionProfileStore connectionProfiles,
        CredentialLifecycleService credentials,
        RecoverySnapshotService recoverySnapshots)
    {
        using var performance = new PerformanceOperation(
            "MainWindowConstruction",
            performanceDiagnostics);
        InitializeComponent();
        _tabs = tabs;
        _objectExplorer = objectExplorer;
        _destructiveOperations = destructiveOperations;
        _settings = settings;
        _backupRestore = backupRestore;
        _backupTools = backupTools;
        _backupInspection = backupInspection;
        _objectSearch = objectSearch;
        _postgresVersion = postgresVersion;
        _indexAnalysis = indexAnalysis;
        _schemaExtractor = schemaExtractor;
        _dataTransfer = dataTransfer;
        _resultExport = resultExport;
        _transferHistory = transferHistory;
        _connectionProbe = connectionProbe;
        _connectionDiagnostics = connectionDiagnostics;
        _performanceDiagnostics = performanceDiagnostics;
        _connectionProfiles = connectionProfiles;
        _credentials = credentials;
        _recoverySnapshots = recoverySnapshots;
        _defaultConnection = ReadDevelopmentFallback();
        RegisterCommands();
        AddTab();
        _statusTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background,
            async (_, _) =>
            {
                UpdateShellState();
                if (++_statusTicks % 5 == 0
                    && Interlocked.Exchange(ref _healthCheckRunning, 1) == 0)
                {
                    try { await CheckActiveConnectionHealthAsync(); }
                    finally { Volatile.Write(ref _healthCheckRunning, 0); }
                }
            }, Dispatcher);
        _statusTimer.Start();
        _recoveryTimer = new DispatcherTimer(
            TimeSpan.FromSeconds(1),
            DispatcherPriority.Background,
            RecoveryTimer_Tick,
            Dispatcher);
    }

    private QueryTabView? ActiveView => (QueryTabs.SelectedItem as TabItem)?.Content as QueryTabView;
    private QueryDocument? ActiveDocument => ActiveView?.Document;

    private void RegisterCommands()
    {
        Bind(ShellCommands.NewQuery, _ => AddTab(), _ => true);
        BindAsync(ShellCommands.Connect, ConnectAsync, _ => ActiveView?.IsExecuting != true);
        BindAsync(ShellCommands.Reconnect, ReconnectAsync,
            _ => ActiveView?.Connection?.Session.CanReconnect == true && ActiveView.IsExecuting == false);
        BindAsync(ShellCommands.ChangeConnection, ConnectAsync, _ => ActiveView is { IsExecuting: false });
        Bind(ShellCommands.Disconnect, _ => DisconnectActive(), _ => State.CanExecute(ShellCommandId.ChangeConnection) && State.IsConnected);
        BindAsync(ShellCommands.OpenFile, () => ActiveView!.OpenFileAsync(), _ => State.CanExecute(ShellCommandId.OpenFile));
        BindAsync(ShellCommands.Save, () => ActiveView!.SaveAsync(), _ => State.CanExecute(ShellCommandId.Save));
        BindAsync(ShellCommands.SaveAs, () => ActiveView!.SaveAsAsync(), _ => State.CanExecute(ShellCommandId.SaveAs));
        BindAsync(ShellCommands.CloseDocument, e => CloseDocumentAsync(ResolveView(e.Parameter)), _ => ActiveView is not null);
        BindAsync(ShellCommands.CloseOtherDocuments, CloseOtherDocumentsAsync, _ => QueryTabs.Items.Count > 1);
        BindAsync(ShellCommands.CloseAllDocuments, CloseAllDocumentsAsync, _ => QueryTabs.Items.Count > 0);
        Bind(ShellCommands.NextDocument, _ => MoveDocument(1), _ => QueryTabs.Items.Count > 1);
        Bind(ShellCommands.PreviousDocument, _ => MoveDocument(-1), _ => QueryTabs.Items.Count > 1);
        BindAsync(ShellCommands.Execute, () => ActiveView!.ExecuteAsync(), _ => State.CanExecute(ShellCommandId.Execute));
        BindAsync(ShellCommands.Cancel, () => ActiveView!.CancelAsync(), _ => State.CanExecute(ShellCommandId.Cancel));
        BindAsync(ShellCommands.EstimatedPlan, () => ActiveView!.ShowEstimatedPlanAsync(), _ => State.CanExecute(ShellCommandId.EstimatedPlan));
        Bind(ShellCommands.ToggleActualPlan, _ => ToggleActualPlan(), _ => State.CanExecute(ShellCommandId.ActualPlan));
        BindAsync(ShellCommands.RefreshObjectExplorer, RefreshObjectExplorerAsync,
            _ => ActiveView?.Connection?.Session.Snapshot.State == RecoveryConnectionState.Connected);
        Bind(ShellCommands.Find, _ => ActiveView!.ShowFind(false), _ => ActiveView is not null);
        Bind(ShellCommands.FindNext, _ => ActiveView!.FindNext(), _ => ActiveView is not null);
        Bind(ShellCommands.Replace, _ => ActiveView!.ShowFind(true), _ => ActiveView is not null);
        Bind(ShellCommands.GoToLine, _ => ActiveView!.GoToLine(), _ => ActiveView is not null);
        Bind(ShellCommands.CopyResults, _ => ActiveView!.CopyResults(false), _ => State.CanExecute(ShellCommandId.ResultAction));
        Bind(ShellCommands.CopyResultsWithHeaders, _ => ActiveView!.CopyResults(true), _ => State.CanExecute(ShellCommandId.ResultAction));
        BindAsync(ShellCommands.ExportResults, () => ActiveView!.OpenExportWorkspaceAsync(), _ => State.CanExecute(ShellCommandId.ResultAction));
        Bind(ShellCommands.FindInResults, _ => ActiveView!.ShowResultSearch(), _ => State.CanExecute(ShellCommandId.ResultAction));
        Bind(ShellCommands.ClearResults, _ => ActiveView!.ClearResultView(), _ => State.CanExecute(ShellCommandId.ResultAction));
        BindAsync(ShellCommands.SearchObjects, () => ActiveView!.OpenSearchWorkspaceAsync(), _ => State.CanExecute(ShellCommandId.ConnectedTool));
        BindAsync(ShellCommands.IndexManagement, () => ActiveView!.OpenIndexWorkspaceAsync(), _ => State.CanExecute(ShellCommandId.ConnectedTool));
        BindAsync(ShellCommands.SchemaCompare, () => ActiveView!.OpenSchemaComparisonWorkspaceAsync(), _ => State.CanExecute(ShellCommandId.ConnectedTool));
        BindAsync(ShellCommands.ImportData, () => ActiveView!.OpenImportWorkspaceAsync(), _ => State.CanExecute(ShellCommandId.ConnectedTool));
        BindAsync(ShellCommands.Backup, () => ActiveView!.BackupDatabaseAsync(), _ => State.CanExecute(ShellCommandId.ConnectedTool));
        BindAsync(ShellCommands.Restore, () => ActiveView!.OpenRestoreWorkspaceAsync(), _ => State.CanExecute(ShellCommandId.ConnectedTool));
        BindAsync(ShellCommands.Maintenance, () => ActiveView!.OpenMaintenanceWorkspaceAsync(), _ => State.CanExecute(ShellCommandId.ConnectedTool));
        BindAsync(ShellCommands.Security, () => ActiveView!.ShowSecurityRolesAsync(), _ => State.CanExecute(ShellCommandId.ConnectedTool));
        Bind(ShellCommands.ShowObjectExplorer, _ => SetObjectExplorerVisible(ObjectExplorerPane.Visibility != Visibility.Visible), _ => true);
        Bind(ShellCommands.ShowResults, _ => ActiveView!.ShowOutput(0), _ => ActiveView is not null);
        Bind(ShellCommands.ShowMessages, _ => ActiveView!.ShowOutput(1), _ => ActiveView is not null);
        Bind(ShellCommands.ShowExecutionPlan, _ => ActiveView!.FocusPlanWorkspace(), _ => ActiveView is not null);
        BindAsync(ShellCommands.PerformanceDashboard, () => ActiveView!.OpenMonitoringWorkspaceAsync(), _ => State.CanExecute(ShellCommandId.ConnectedTool));
        BindAsync(ShellCommands.BlockingDiagnostics, () => ActiveView!.OpenMonitoringWorkspaceAsync(), _ => State.CanExecute(ShellCommandId.ConnectedTool));
        Bind(ShellCommands.About, _ => MessageBox.Show(this,
            $"PostgreManagementStudio {AssemblyVersionText()}\n\nA PostgreSQL management desktop for Windows.",
            "About PostgreManagementStudio", MessageBoxButton.OK, MessageBoxImage.Information), _ => true);
    }

    private static string AssemblyVersionText() =>
        (System.Reflection.Assembly.GetEntryAssembly()?
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
            .Select(attribute => attribute.InformationalVersion)
            .FirstOrDefault(version => !string.IsNullOrWhiteSpace(version))?
            .Split('+')[0])
        ?? System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3)
        ?? "0.0.0";

    private ShellCommandState State => new(
        ActiveView is not null,
        ActiveView?.Connection?.Session.Snapshot.State == RecoveryConnectionState.Connected,
        ActiveView?.IsExecuting == true,
        ActiveView?.HasResults == true,
        ActiveDocument?.IsDirty == true);

    private void Bind(RoutedUICommand command, Action<ExecutedRoutedEventArgs> execute, Func<CanExecuteRoutedEventArgs, bool> canExecute)
    {
        CommandBindings.Add(new CommandBinding(command,
            (_, e) => execute(e),
            (_, e) => { e.CanExecute = canExecute(e); e.Handled = true; }));
    }

    private void BindAsync(RoutedUICommand command, Func<Task> execute, Func<CanExecuteRoutedEventArgs, bool> canExecute) =>
        BindAsync(command, _ => execute(), canExecute);

    private void BindAsync(RoutedUICommand command, Func<ExecutedRoutedEventArgs, Task> execute, Func<CanExecuteRoutedEventArgs, bool> canExecute) =>
        CommandBindings.Add(new CommandBinding(command,
            async (_, e) =>
            {
                try { await ObserveAsync(() => execute(e)); }
                finally { CommandManager.InvalidateRequerySuggested(); UpdateShellState(); }
            },
            (_, e) => { e.CanExecute = canExecute(e); e.Handled = true; }));

    private void AddTab(RecoverySnapshot? recoverySnapshot = null)
    {
        var connection = recoverySnapshot is null ? _defaultConnection : null;
        var database = recoverySnapshot?.Database ?? connection?.Database ?? _settings.DefaultDatabase;
        var doc = _tabs.Open(null, database);
        doc.ConnectionProfileId = string.Empty;
        doc.CommandTimeout = TimeSpan.FromSeconds(_settings.CommandTimeoutSeconds);
        doc.CancellationTimeout = TimeSpan.FromSeconds(_settings.CancellationTimeoutSeconds);
        var view = new QueryTabView(doc, _destructiveOperations, _settings, _backupRestore,
            _backupTools, _backupInspection, _objectSearch, _postgresVersion, _indexAnalysis, _schemaExtractor,
            _dataTransfer, _resultExport, _transferHistory, _performanceDiagnostics);
        if (recoverySnapshot is not null)
            view.RestoreRecoverySnapshot(recoverySnapshot);
        var tab = new TabItem { Content = view, Tag = doc };
        tab.Header = CreateTabHeader(tab, view);
        tab.ToolTip = view.SafeToolTip;
        view.DirtyChanged += View_StateChanged;
        view.WorkspaceStateChanged += View_StateChanged;
        if (connection is not null)
        {
            TrackConnection(connection);
            view.ApplyConnection(connection);
        }
        QueryTabs.Items.Add(tab);
        QueryTabs.SelectedItem = tab;
        UpdateTab(tab, view);
        UpdateShellState();
    }

    private FrameworkElement CreateTabHeader(TabItem tab, QueryTabView view)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock { Text = view.DisplayTitle, VerticalAlignment = VerticalAlignment.Center, Tag = "title" });
        var close = new Button
        {
            Content = "×",
            FontSize = 14,
            Padding = new Thickness(3, 0, 3, 0),
            Margin = new Thickness(7, 0, 0, 0),
            ToolTip = "Close query (Ctrl+W)",
            Command = ShellCommands.CloseDocument,
            CommandParameter = tab,
        };
        panel.Children.Add(close);
        return panel;
    }

    private void View_StateChanged(object? sender, EventArgs e)
    {
        if (sender is QueryTabView view)
        {
            if (FindTab(view) is { } tab) UpdateTab(tab, view);
            ScheduleRecoverySnapshot(view);
        }
        UpdateShellState();
        CommandManager.InvalidateRequerySuggested();
    }

    private void ScheduleRecoverySnapshot(QueryTabView view)
    {
        if (!view.Document.IsDirty)
        {
            _pendingRecoverySnapshots.Remove(view);
            _recoverySnapshots.Remove(view.RecoveryId);
            return;
        }

        _pendingRecoverySnapshots.Add(view);
        _recoveryTimer.Stop();
        _recoveryTimer.Start();
    }

    private async void RecoveryTimer_Tick(object? sender, EventArgs e)
    {
        _recoveryTimer.Stop();
        var pending = _pendingRecoverySnapshots.ToArray();
        _pendingRecoverySnapshots.Clear();
        foreach (var view in pending)
        {
            if (!view.Document.IsDirty || FindTab(view) is null)
                continue;
            try
            {
                await _recoverySnapshots.WriteAsync(view.CreateRecoverySnapshot());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine(
                    $"workspace_recovery_write_failed type={ex.GetType().FullName} message={SecretRedactor.Redact(ex.Message)}");
            }
        }
    }

    private static void UpdateTab(TabItem tab, QueryTabView view)
    {
        if (tab.Header is StackPanel panel && panel.Children.OfType<TextBlock>().FirstOrDefault(x => Equals(x.Tag, "title")) is { } title)
            title.Text = view.DisplayTitle;
        tab.ToolTip = view.SafeToolTip;
    }

    private TabItem? FindTab(QueryTabView view) =>
        QueryTabs.Items.OfType<TabItem>().FirstOrDefault(x => ReferenceEquals(x.Content, view));

    private QueryTabView? ResolveView(object? parameter) => parameter switch
    {
        TabItem { Content: QueryTabView view } => view,
        QueryTabView view => view,
        _ => ActiveView,
    };

    private async Task<bool> CloseDocumentAsync(QueryTabView? view)
    {
        if (view is null) return true;
        var document = view.Document;
        if (document.IsExecuting)
        {
            var answer = MessageBox.Show(this, $"Cancel the running query in {view.DisplayTitle} and close it?",
                "Query is running", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes) return false;
            if (!await document.CancelAsync()) return false;
        }
        if (document.IsDirty)
        {
            var answer = MessageBox.Show(this, $"Save changes to {view.DisplayTitle.TrimEnd('*')}?",
                "Unsaved query", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
            if (answer == MessageBoxResult.Cancel) return false;
            if (answer == MessageBoxResult.Yes && !await view.SaveAsync()) return false;
            if (answer == MessageBoxResult.No) document.MarkDirty(false);
        }

        view.DirtyChanged -= View_StateChanged;
        view.WorkspaceStateChanged -= View_StateChanged;
        _pendingRecoverySnapshots.Remove(view);
        _recoverySnapshots.Remove(view.RecoveryId);
        await document.DisposeAsync();
        _tabs.TryClose(document, discardChanges: true);
        if (FindTab(view) is { } tab) QueryTabs.Items.Remove(tab);
        UpdateShellState();
        return true;
    }

    private async Task CloseOtherDocumentsAsync()
    {
        var keep = ActiveView;
        foreach (var view in QueryTabs.Items.OfType<TabItem>().Select(x => x.Content).OfType<QueryTabView>().Where(x => !ReferenceEquals(x, keep)).ToArray())
            if (!await CloseDocumentAsync(view)) return;
    }

    private async Task CloseAllDocumentsAsync()
    {
        foreach (var view in QueryTabs.Items.OfType<TabItem>().Select(x => x.Content).OfType<QueryTabView>().ToArray())
            if (!await CloseDocumentAsync(view)) return;
    }

    private void MoveDocument(int delta)
    {
        if (QueryTabs.Items.Count < 2) return;
        QueryTabs.SelectedIndex = (QueryTabs.SelectedIndex + delta + QueryTabs.Items.Count) % QueryTabs.Items.Count;
    }

    private async Task ConnectAsync()
    {
        var current = ActiveView?.Connection ?? _defaultConnection;
        var dialog = new ConnectionDialog(_connectionProbe, _connectionDiagnostics, _connectionProfiles, _credentials, current) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Connection is null) return;
        _defaultConnection = dialog.Connection;
        TrackConnection(dialog.Connection);
        if (ActiveView is null) AddTab();
        else ActiveView.ApplyConnection(dialog.Connection);
        UpdateShellState();
        await RefreshObjectExplorerAsync();
    }

    private async Task ReconnectAsync()
    {
        var session = ActiveView?.Connection?.Session;
        if (session?.CanReconnect != true) return;
        var snapshot = await session.ReconnectAsync();
        if (snapshot.State == RecoveryConnectionState.Connected)
            await RefreshObjectExplorerAsync();
    }

    private void DisconnectActive()
    {
        var connection = ActiveView?.Connection;
        if (connection is null || ActiveView?.IsExecuting == true) return;
        connection.Session.Disconnect();
        if (ReferenceEquals(_defaultConnection?.Session, connection.Session)) _defaultConnection = null;
        MarkObjectExplorerStale("Disconnected. Reconnect to refresh PostgreSQL objects.");
        UpdateShellState();
    }

    private void ToggleActualPlan()
    {
        if (ActiveView is null) return;
        ActiveView.IncludeActualPlan = !ActiveView.IncludeActualPlan;
        ActualPlanButton.IsChecked = ActiveView.IncludeActualPlan;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await ObserveAsync(async () =>
        {
            if (await RestoreWorkspaceAsync()) return;
            if (_defaultConnection is null) return;
            TrackConnection(_defaultConnection);
            await _defaultConnection.Session.ConnectAsync(_defaultConnection.Configuration);
        });
        UpdateShellState();
        CommandManager.InvalidateRequerySuggested();
    }

    private async Task<bool> RestoreWorkspaceAsync()
    {
        var snapshots = await _recoverySnapshots.ReadAllAsync();
        if (snapshots.Count == 0)
            return false;

        var initial = ActiveView;
        if (initial is not null)
            await CloseDocumentAsync(initial);
        foreach (var snapshot in snapshots)
            AddTab(snapshot);
        QueryStatusText.Text =
            $"Recovered {snapshots.Count:N0} unsaved quer{(snapshots.Count == 1 ? "y" : "ies")}; reconnect explicitly before execution.";
        return true;
    }

    private async void QueryTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        await ObserveAsync(async () =>
        {
            if (QueryTabs.SelectedItem is TabItem { Tag: QueryDocument doc }) _tabs.Activate(doc);
            ActualPlanButton.IsChecked = ActiveView?.IncludeActualPlan == true;
            UpdateShellState();
            CommandManager.InvalidateRequerySuggested();
            if (CurrentObjectExplorerContext() != _objectExplorerContext)
                await RefreshObjectExplorerAsync();
        });
    }

    private void DatabaseSelector_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (ActiveView is null || ActiveView.IsExecuting) return;
        ActiveView.ChangeDatabase(DatabaseSelector.Text);
        UpdateShellState();
    }

    private void ObjectExplorerTree_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var item = e.OriginalSource as DependencyObject;
        while (item is not null && item is not TreeViewItem)
            item = VisualTreeHelper.GetParent(item);
        if (item is TreeViewItem treeItem && treeItem.Tag is ObjectExplorerNode)
        {
            treeItem.IsSelected = true;
            treeItem.Focus();
        }
    }

    private async Task RefreshObjectExplorerAsync()
    {
        var document = ActiveDocument;
        var recovery = ActiveView?.Connection?.Session;
        if (document is null || recovery?.Snapshot.State != RecoveryConnectionState.Connected
            || string.IsNullOrWhiteSpace(document.ConnectionString))
        {
            MarkObjectExplorerStale("Object Explorer is stale. Reconnect to refresh PostgreSQL objects.");
            return;
        }
        var requestedContext = CurrentObjectExplorerContext();
        if (requestedContext is null)
            return;
        try
        {
            _metadataCancellation?.Cancel();
            _metadataCancellation?.Dispose();
            _metadataCancellation = CancellationTokenSource.CreateLinkedTokenSource(recovery.GenerationToken);
            var expanded = ExpandedIdentities(ObjectExplorerTree.Items).ToHashSet();
            var selectionPath = SelectedIdentityPath();
            var root = await _objectExplorer.LoadRootAsync(document.ConnectionString, document.Database, refresh: true,
                connectionGenerationId: recovery.Snapshot.GenerationId,
                cancellationToken: _metadataCancellation.Token);
            if (CurrentObjectExplorerContext() != requestedContext)
                return;
            ObjectExplorerTree.ItemsSource = null;
            ObjectExplorerTree.Items.Clear();
            ObjectExplorerTree.Items.Add(ToTreeItem(root, expanded));
            ObjectExplorerTree.ToolTip = null;
            ObjectExplorerHeader.Text = "Object Explorer";
            _objectExplorerContext = requestedContext;
            foreach (var identity in selectionPath)
                if (FindItem(ObjectExplorerTree.Items, identity) is { } selected)
                {
                    selected.IsSelected = true;
                    selected.BringIntoView();
                    break;
                }
        }
        catch (OperationCanceledException) { }
        catch (MetadataLoadException ex) when (ex.Error.Category is MetadataFailureCategory.ConnectionLost
            or MetadataFailureCategory.DatabaseUnavailable)
        {
            recovery.ReportFailure(DatabaseFailureClassifier.FromSqlState(ex.Error.SqlState, ex.Error.Message));
            MarkObjectExplorerStale(ex.Error.Message);
        }
        catch (Exception ex)
        {
            var message = $"Object Explorer unavailable: {SecretRedactor.Redact(ex.Message)}";
            if (ObjectExplorerTree.Items.Count == 0) ObjectExplorerTree.ItemsSource = new[] { message };
            else ObjectExplorerTree.ToolTip = message;
        }
    }

    private void TrackConnection(ShellConnectionInfo connection)
    {
        if (!_recoverySessions.Add(connection.Session)) return;
        connection.Session.StateChanged += RecoverySession_StateChanged;
    }

    private async void RecoverySession_StateChanged(object? sender, EventArgs e)
    {
        if (sender is not ConnectionRecoverySession session) return;
        try
        {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(() => RecoverySession_StateChanged(sender, e));
            return;
        }
        if (!ReferenceEquals(ActiveView?.Connection?.Session, session)) return;
        var snapshot = session.Snapshot;
        if (snapshot.State == RecoveryConnectionState.Connected)
        {
            // Query-tab subscribers apply the new generation during the same
            // StateChanged dispatch. Defer metadata until those subscribers
            // have made the connection usable.
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
            if (session.Snapshot.State == RecoveryConnectionState.Connected)
                await RefreshObjectExplorerAsync();
        }
        else if (snapshot.State == RecoveryConnectionState.Connecting
            && snapshot.GenerationId == Guid.Empty)
        {
            ObjectExplorerHeader.Text = "Object Explorer (connecting…)";
            ObjectExplorerTree.ItemsSource = new[] { "Connecting to PostgreSQL…" };
        }
        else
        {
            MarkObjectExplorerStale(snapshot.Failure?.Message ?? $"Connection state: {snapshot.State}.");
        }
        UpdateShellState();
        CommandManager.InvalidateRequerySuggested();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine(
                $"Connection state UI update failed: {SecretRedactor.Redact(ex.Message)}");
            if (Dispatcher.CheckAccess())
            {
                MarkObjectExplorerStale("Connection state changed. Reconnect or refresh Object Explorer.");
                UpdateShellState();
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    private void MarkObjectExplorerStale(string message)
    {
        _metadataCancellation?.Cancel();
        _objectExplorer.MarkStale();
        _objectExplorerContext = null;
        ObjectExplorerHeader.Text = "Object Explorer (stale — reconnect required)";
        ObjectExplorerTree.ToolTip = SecretRedactor.Redact(message);
        if (ObjectExplorerTree.Items.Count == 0)
            ObjectExplorerTree.ItemsSource = new[] { SecretRedactor.Redact(message) };
    }

    private void UpdateShellState()
    {
        var view = ActiveView;
        var doc = view?.Document;
        if (doc is null)
        {
            ConnectionToolbarText.Text = "No active query";
            DatabaseSelector.Text = string.Empty;
            SetStatus("Disconnected", "—", "—", "—", "No active query", "—", (1, 1));
            return;
        }
        var snapshot = view!.Connection?.Session.Snapshot;
        var connected = snapshot?.State == RecoveryConnectionState.Connected
            && !string.IsNullOrWhiteSpace(doc.ConnectionString);
        DatabaseSelector.IsEnabled = connected && !view!.IsExecuting;
        DatabaseSelector.Text = doc.Database;
        var connection = view.Connection;
        ConnectionToolbarText.Text = connection is null
            ? "Connect…"
            : $"{connection.Configuration.Profile.Name} · {connection.Username}@{connection.Host}:{connection.Port} ({snapshot!.State})";
        var elapsed = view!.ExecutionElapsed;
        var query = view.IsExecuting
            ? $"{doc.State} · {elapsed?.ToString(@"hh\:mm\:ss") ?? "00:00:00"}"
            : view.QueryStatus;
        var rows = view.HasResults ? $"{view.RowsReceived:N0} returned; {view.RowsAffected:N0} affected" : "—";
        var stateText = snapshot is null ? "Disconnected" : snapshot.State.ToString();
        if (connected && connection!.IsDevelopmentFallback) stateText += " (environment fallback)";
        if (connected)
        {
            stateText += $" · {connection!.Configuration.Profile.EnvironmentDisplayName}";
            if (connection.Configuration.Profile.EffectiveReadOnly) stateText += " · READ ONLY";
        }
        if (snapshot?.BackendProcessId is { } pid) stateText += $" · PID {pid}";
        SetStatus(stateText, connection is null ? "—" : $"{connection.Host}:{connection.Port}", doc.Database, connection?.Username ?? "—", query, rows, view.CaretPosition);
    }

    private async Task CheckActiveConnectionHealthAsync()
    {
        var session = ActiveView?.Connection?.Session;
        if (session?.Snapshot.State != RecoveryConnectionState.Connected) return;
        try { await session.CheckHealthAsync(session.GenerationToken); }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            session.ReportFailure(ex, FailureOperationPhase.Connect);
        }
    }

    private void SetStatus(string connection, string server, string database, string role, string query, string rows, (int Line, int Column) caret)
    {
        ConnectionStatusText.Text = connection;
        ServerStatusText.Text = $"Server: {server}";
        DatabaseStatusText.Text = $"Database: {database}";
        RoleStatusText.Text = $"Role: {role}";
        QueryStatusText.Text = query;
        RowsStatusText.Text = $"Rows: {rows}";
        CaretStatusText.Text = $"Ln {caret.Line}, Col {caret.Column}  INS";
    }

    private ObjectExplorerContext? CurrentObjectExplorerContext()
    {
        var view = ActiveView;
        var session = view?.Connection?.Session;
        var snapshot = session?.Snapshot;
        return view is null || session is null || snapshot?.State != RecoveryConnectionState.Connected
            ? null
            : new(session.LogicalSessionId, snapshot.GenerationId, view.Document.Database);
    }

    private ShellConnectionInfo? ReadDevelopmentFallback()
    {
        var value = Environment.GetEnvironmentVariable("PMS_CONNECTION_STRING");
        return string.IsNullOrWhiteSpace(value) ? null : ParseConnection(value, true);
    }

    private ShellConnectionInfo ParseConnection(string value, bool fallback)
    {
        var connection = DatabaseConnection.FromConnectionString(value);
        var raw = new DbConnectionStringBuilder { ConnectionString = value };
        var sslText = raw.TryGetValue("SSL Mode", out var ssl) ? Convert.ToString(ssl) : "Prefer";
        var sslMode = Enum.TryParse<Npgsql.SslMode>(sslText?.Replace(" ", ""), true, out var parsed) ? parsed : Npgsql.SslMode.Prefer;
        var configuration = EffectiveConnectionConfigurationBuilder.FromConnectionString(
            fallback ? "environment:PMS_CONNECTION_STRING" : $"interactive:{Guid.NewGuid():N}",
            value,
            "PostgreManagementStudio");
        return new(value, connection.Host, connection.Port, connection.Database, connection.Username,
            sslMode, null, null, fallback, configuration,
            new ConnectionRecoverySession(_connectionProbe, _connectionDiagnostics));
    }

    private void SetObjectExplorerVisible(bool visible)
    {
        ObjectExplorerPane.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        ObjectExplorerColumn.Width = visible ? new GridLength(260) : new GridLength(0);
        ObjectExplorerSplitterColumn.Width = visible ? new GridLength(5) : new GridLength(0);
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_shutdownApproved) return;
        e.Cancel = true;
        if (_shutdownInProgress) return;
        _shutdownInProgress = true;
        _statusTimer.Stop();
        _recoveryTimer.Stop();
        _metadataCancellation?.Cancel();
        using var performance = new PerformanceOperation("ApplicationShutdown", _performanceDiagnostics);
        try
        {
            var openViews = QueryTabs.Items.OfType<TabItem>()
                .Select(item => item.Content).OfType<QueryTabView>().ToArray();
            if (openViews.Any(view => view.Document.IsDirty || view.IsExecuting))
            {
                foreach (var view in openViews)
                    if (!await CloseDocumentAsync(view))
                    {
                        _statusTimer.Start();
                        if (_pendingRecoverySnapshots.Count > 0)
                            _recoveryTimer.Start();
                        return;
                    }
                openViews = Array.Empty<QueryTabView>();
            }

            var cleanup = CleanupShellResourcesAsync(openViews);
            if (cleanup.IsCompletedSuccessfully)
            {
                _shutdownApproved = true;
                e.Cancel = false;
                return;
            }
            try
            {
                await cleanup.WaitAsync(PerformanceBudgets.InteractiveP95["Shutdown"]);
            }
            catch (TimeoutException)
            {
                performance.Fail("shutdown_timeout");
                _ = ObserveLateCleanupAsync(cleanup);
                System.Diagnostics.Trace.WriteLine(
                    "Shell cleanup exceeded the five-second shutdown budget; remaining disposal continues in the background.");
            }
            _shutdownApproved = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, SecretRedactor.Redact(ex.Message), "Shutdown cleanup failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _shutdownInProgress = false;
            if (!_shutdownApproved && !_statusTimer.IsEnabled) _statusTimer.Start();
        }
    }

    private async Task CleanupShellResourcesAsync(IReadOnlyList<QueryTabView> views)
    {
        foreach (var view in views) await view.Document.DisposeAsync();
        _pendingRecoverySnapshots.Clear();
        await _objectExplorer.DisposeAsync();
        await DisposeRecoverySessionsAsync();
    }

    private static async Task ObserveLateCleanupAsync(Task cleanup)
    {
        try { await cleanup.ConfigureAwait(false); }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine(
                $"Late shell cleanup failed: {SecretRedactor.Redact(ex.Message)}");
        }
    }

    private async Task DisposeRecoverySessionsAsync()
    {
        foreach (var session in _recoverySessions.ToArray())
        {
            session.StateChanged -= RecoverySession_StateChanged;
            await session.DisposeAsync();
        }
        _recoverySessions.Clear();
    }

    private async Task ObserveAsync(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex)
        {
            QueryStatusText.Text = "Command failed";
            MessageBox.Show(this, SecretRedactor.Redact(ex.Message), "Command failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();
    private void ToggleToolbars_Click(object sender, RoutedEventArgs e) => ShellToolbars.Visibility = ShellToolbars.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
    private void ToggleStatusBar_Click(object sender, RoutedEventArgs e) => ShellStatusBar.Visibility = ShellStatusBar.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
    private void ResetLayout_Click(object sender, RoutedEventArgs e) { SetObjectExplorerVisible(true); Width = 1100; Height = 768; }
    private void Documentation_Click(object sender, RoutedEventArgs e) => MessageBox.Show(this, "Documentation is available in README.md and the docs folder.", "Documentation");
    private void Diagnostics_Click(object sender, RoutedEventArgs e) => MessageBox.Show(this, $"Settings: {ProductionServices.DefaultSettingsPath}\nConnection strings and passwords are never included.", "Diagnostics");

    private TreeViewItem ToTreeItem(ObjectExplorerNode node, IReadOnlySet<PostgresObjectIdentity>? expanded = null)
    {
        var item = new TreeViewItem
        {
            Header = UntrustedText.ForDisplay(node.Name),
            ToolTip = UntrustedText.ForDisplay(node.Name, 4_096),
            Tag = node,
        };
        if (node.HasChildren)
            item.Items.Add(new TreeViewItem { Header = "Expand to load…", IsEnabled = false });
        item.Expanded += TreeItem_Expanded;
        item.Collapsed += TreeItem_Collapsed;
        if (expanded?.Contains(node.Identity) == true) item.IsExpanded = true;
        return item;
    }

    private void Populate(TreeViewItem item, ObjectExplorerNode node, IReadOnlySet<PostgresObjectIdentity>? expanded = null)
    {
        item.Items.Clear();
        if (!node.IsLoaded && node.HasChildren) { item.Items.Add(new TreeViewItem { Header = "Loading…" }); return; }
        foreach (var child in node.Children) item.Items.Add(ToTreeItem(child, expanded));
        if (node.Error is not null) item.Items.Add(new TreeViewItem { Header = node.Error.Message, IsEnabled = false });
    }

    private async void TreeItem_Expanded(object sender, RoutedEventArgs e)
    {
        if (sender is not TreeViewItem { Tag: ObjectExplorerNode node } item) return;
        e.Handled = true;
        if (node.IsLoaded)
        {
            if (item.Items.Count == 1 && item.Items[0] is TreeViewItem { Tag: null })
                Populate(item, node);
            return;
        }
        try
        {
            await _objectExplorer.ExpandAsync(
                node,
                cancellationToken: _metadataCancellation?.Token ?? default);
            if (item.IsExpanded) Populate(item, node);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { item.Items.Clear(); item.Items.Add(new TreeViewItem { Header = SecretRedactor.Redact(ex.Message), IsEnabled = false }); }
    }

    private static void TreeItem_Collapsed(object sender, RoutedEventArgs e)
    {
        if (sender is not TreeViewItem { Tag: ObjectExplorerNode node }) return;
        node.Cancel();
        e.Handled = true;
    }

    private static IEnumerable<PostgresObjectIdentity> ExpandedIdentities(ItemCollection items)
    {
        foreach (var value in items)
        {
            if (value is not TreeViewItem item) continue;
            if (item.IsExpanded && item.Tag is ObjectExplorerNode node) yield return node.Identity;
            foreach (var child in ExpandedIdentities(item.Items)) yield return child;
        }
    }

    private IReadOnlyList<PostgresObjectIdentity> SelectedIdentityPath()
    {
        var identities = new List<PostgresObjectIdentity>();
        var item = ObjectExplorerTree.SelectedItem as TreeViewItem;
        while (item is not null)
        {
            if (item.Tag is ObjectExplorerNode node) identities.Add(node.Identity);
            item = ItemsControl.ItemsControlFromItemContainer(item) as TreeViewItem;
        }
        return identities;
    }

    private static TreeViewItem? FindItem(ItemCollection items, PostgresObjectIdentity identity)
    {
        foreach (var value in items)
        {
            if (value is not TreeViewItem item) continue;
            if (item.Tag is ObjectExplorerNode node && node.Identity.Equals(identity)) return item;
            if (FindItem(item.Items, identity) is { } child) return child;
        }
        return null;
    }

    private readonly record struct ObjectExplorerContext(
        Guid LogicalSessionId,
        Guid GenerationId,
        string Database);
}
