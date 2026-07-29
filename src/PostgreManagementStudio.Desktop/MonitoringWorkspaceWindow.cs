using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using PostgreManagementStudio.Application;
using PostgreManagementStudio.Core;
using PostgreManagementStudio.Postgres;

namespace PostgreManagementStudio.Desktop;

public sealed class MonitoringWorkspaceWindow : Window
{
    private readonly NpgsqlActivityService _activity;
    private readonly string _connectionString;
    private readonly string _serverIdentity;
    private readonly DataGrid _sessions = Grid();
    private readonly DataGrid _blocking = Grid();
    private readonly DataGrid _locks = Grid();
    private readonly TextBox _diagnostics = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button _refresh = new() { Content = "Refresh", Width = 90 };
    private readonly Button _pause = new() { Content = "Pause", Width = 90 };
    private readonly Button _cancelSession = new() { Content = "Cancel query", Width = 105, IsEnabled = false };
    private readonly Button _terminateSession = new() { Content = "Terminate session", Width = 125, IsEnabled = false };
    private readonly Button _snapshot = new() { Content = "Save snapshot", Width = 105 };
    private readonly CheckBox _automatic = new() { Content = "Automatic refresh", IsChecked = false };
    private readonly CheckBox _includeQuery = new() { Content = "Include bounded query previews", IsChecked = false };
    private readonly ComboBox _interval = new() { Width = 100 };
    private readonly DispatcherTimer _timer;
    private readonly ActivityMonitorRefreshCoordinator _refreshCoordinator = new();
    private readonly CancellationTokenSource _lifetime = new();
    private ActivitySnapshot? _current;
    private bool _paused;
    private bool _closing;

    public MonitoringWorkspaceWindow(NpgsqlActivityService activity, string connectionString, string serverIdentity)
    {
        _activity = activity;
        _connectionString = connectionString;
        _serverIdentity = string.IsNullOrWhiteSpace(serverIdentity) ? "Connected PostgreSQL server" : serverIdentity;
        Title = $"Performance dashboard — {_serverIdentity}";
        Width = 1120;
        Height = 760;
        MinWidth = 850;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _interval.ItemsSource = new[] { "5 seconds", "10 seconds", "30 seconds", "60 seconds" };
        _interval.SelectedIndex = 0;
        AddColumns(_sessions, ("PID", nameof(SessionRow.ProcessId)), ("Database", nameof(SessionRow.Database)), ("User", nameof(SessionRow.User)), ("State", nameof(SessionRow.State)), ("Query duration", nameof(SessionRow.Duration)), ("Transaction duration", nameof(SessionRow.TransactionDuration)), ("Wait", nameof(SessionRow.Wait)), ("Blocked", nameof(SessionRow.Blocked)), ("Query", nameof(SessionRow.Query)));
        AddColumns(_blocking, ("Blocked PID", nameof(BlockingRow.BlockedPid)), ("Blocking PID", nameof(BlockingRow.BlockingPid)), ("Database", nameof(BlockingRow.Database)), ("User", nameof(BlockingRow.User)), ("Wait event", nameof(BlockingRow.WaitEvent)), ("Blocked query", nameof(BlockingRow.BlockedQuery)), ("Blocking query", nameof(BlockingRow.BlockingQuery)));
        AddColumns(_locks, ("PID", nameof(LockRow.ProcessId)), ("Lock type", nameof(LockRow.LockType)), ("Database", nameof(LockRow.Database)), ("Relation", nameof(LockRow.Relation)), ("Mode", nameof(LockRow.Mode)), ("Granted", nameof(LockRow.Granted)), ("Wait start", nameof(LockRow.WaitStart)));
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _timer.Tick += async (_, _) => { if (!_paused && _automatic.IsChecked == true) await RefreshAsync(); };
        _refresh.Click += async (_, _) => await RefreshAsync();
        _pause.Click += (_, _) => TogglePause();
        _automatic.Checked += (_, _) => StartTimer();
        _automatic.Unchecked += (_, _) => StopTimer();
        _interval.SelectionChanged += (_, _) => UpdateInterval();
        _snapshot.Click += (_, _) => SaveSnapshot();
        _cancelSession.Click += async (_, _) => await ActOnSelectedAsync(false);
        _terminateSession.Click += async (_, _) => await ActOnSelectedAsync(true);
        _sessions.SelectionChanged += (_, _) => UpdateActionState();
        Closed += (_, _) => CloseWorkspace();
        Content = BuildContent();
        AutomationProperties.SetName(_sessions, "Activity sessions");
        AutomationProperties.SetName(_blocking, "Blocking relationships");
        AutomationProperties.SetName(_locks, "Lock inventory");
        AutomationProperties.SetName(_snapshot, "Save diagnostic snapshot");
    }

    private UIElement BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(12) };
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        actions.Children.Add(_refresh);
        actions.Children.Add(_pause);
        actions.Children.Add(_automatic);
        actions.Children.Add(new TextBlock { Text = "Interval:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 3, 0) });
        actions.Children.Add(_interval);
        actions.Children.Add(_snapshot);
        DockPanel.SetDock(actions, Dock.Bottom);
        root.Children.Add(actions);

        var header = new StackPanel();
        header.Children.Add(new TextBlock { Text = "Server performance dashboard", FontSize = 18, FontWeight = FontWeights.SemiBold });
        header.Children.Add(new TextBlock { Text = $"Server context: {_serverIdentity}. This workspace never switches connections automatically.", TextWrapping = TextWrapping.Wrap });
        header.Children.Add(_summary);
        header.Children.Add(_status);
        root.Children.Add(header);

        var tabs = new TabControl();
        tabs.Items.Add(new TabItem { Header = "Activity", Content = ActivityPage() });
        tabs.Items.Add(new TabItem { Header = "Blocking and waits", Content = BlockingPage() });
        tabs.Items.Add(new TabItem { Header = "Locks", Content = new ScrollViewer { Content = _locks, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
        tabs.Items.Add(new TabItem { Header = "Diagnostic output", Content = DiagnosticPage() });
        root.Children.Add(tabs);
        return root;
    }

    private UIElement ActivityPage()
    {
        var panel = new DockPanel();
        var actions = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        actions.Children.Add(_cancelSession);
        actions.Children.Add(_terminateSession);
        DockPanel.SetDock(actions, Dock.Top);
        panel.Children.Add(actions);
        panel.Children.Add(_sessions);
        return panel;
    }

    private UIElement BlockingPage()
    {
        var panel = new DockPanel();
        panel.Children.Add(new TextBlock { Text = "Blocking relationships are from the latest PostgreSQL snapshot; refresh before acting on a changed selection.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });
        panel.Children.Add(_blocking);
        return panel;
    }

    private UIElement DiagnosticPage()
    {
        var panel = new DockPanel();
        var options = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        options.Children.Add(_includeQuery);
        options.Children.Add(new TextBlock { Text = "Query text is omitted by default; snapshot output never contains credentials or connection strings.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 0, 0, 0) });
        DockPanel.SetDock(options, Dock.Top);
        panel.Children.Add(options);
        panel.Children.Add(_diagnostics);
        return panel;
    }

    private async Task RefreshAsync()
    {
        if (_closing || _paused) return;
        try
        {
            SetBusy(true);
            var started = DateTimeOffset.UtcNow;
            _status.Text = "Refreshing activity snapshot…";
            var snapshot = await _refreshCoordinator.RefreshAsync(
                (sequence, token) => _activity.LoadSnapshotAsync(_connectionString, sequence, token), _lifetime.Token);
            if (snapshot is null || _closing) return;
            _current = snapshot;
            _sessions.ItemsSource = snapshot.Sessions.Select(ToSessionRow).ToArray();
            _blocking.ItemsSource = snapshot.Blocking.Select(x => new BlockingRow(x)).ToArray();
            _locks.ItemsSource = snapshot.Locks.Select(x => new LockRow(x)).ToArray();
            _summary.Text = $"Collected {snapshot.ServerTime:O} · sessions {snapshot.Summary.TotalSessions:N0} · active {snapshot.Summary.ActiveSessions:N0} · idle {snapshot.Summary.IdleSessions:N0} · idle in transaction {snapshot.Summary.IdleInTransactionSessions:N0} · waiting {snapshot.Summary.WaitingSessions:N0} · blocked {snapshot.Summary.BlockedSessions:N0} · long-running {snapshot.Summary.LongRunningQueries:N0}";
            _diagnostics.Text = BuildDiagnosticText(snapshot);
            _status.Text = $"Last successful refresh {snapshot.ServerTime:O}; duration {(DateTimeOffset.UtcNow - started):g}. Query statistics: unavailable in this build; pg_stat_statements is not silently treated as empty.";
        }
        catch (OperationCanceledException) when (_closing) { }
        catch (Exception ex) { _status.Text = DesktopErrorPresentation.Failure("Activity refresh", ex) + " Previous snapshot remains visible."; }
        finally { if (!_closing) SetBusy(false); }
    }

    private async Task ActOnSelectedAsync(bool terminate)
    {
        if (_sessions.SelectedItem is not SessionRow selected || _current is null) return;
        var target = _current.Sessions.FirstOrDefault(x => x.ProcessId == selected.ProcessId);
        if (target is null) { _status.Text = "The selected session disappeared; refresh before retrying."; return; }
        var identity = ActivityMonitorPresentationService.Identity(target);
        var confirm = ActivityMonitorPresentationService.Confirmation(terminate ? "Terminate session" : "Cancel query", target, QueryTextDisplayMode.Hide);
        var answer = MessageBox.Show(this, $"{confirm.Warning}\n\nPID {confirm.ProcessId} on {confirm.Database ?? "unknown database"}.\n{confirm.QueryPreview}\n\nRefresh before acting if this snapshot is old.", confirm.Action, MessageBoxButton.YesNo, terminate ? MessageBoxImage.Warning : MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;
        try
        {
            SetBusy(true);
            var fresh = await _activity.LoadSnapshotAsync(_connectionString, _current.Sequence + 1, _lifetime.Token);
            var current = fresh.Sessions.FirstOrDefault(x => x.ProcessId == identity.ProcessId);
            if (current is null || !ActivityMonitorPresentationService.SelectionStillMatches(identity, current))
            { _status.Text = "The selected session changed or disappeared; no action was sent."; return; }
            var result = terminate
                ? await _activity.TerminateAsync(_connectionString, current, Environment.ProcessId, null, null, _lifetime.Token)
                : await _activity.CancelAsync(_connectionString, current.ProcessId, _lifetime.Token);
            _status.Text = result.Message;
            await RefreshAsync();
        }
        catch (OperationCanceledException) when (_closing) { }
        catch (Exception ex) { _status.Text = DesktopErrorPresentation.Failure(terminate ? "Terminate session" : "Cancel query", ex); }
        finally { if (!_closing) SetBusy(false); }
    }

    private void SaveSnapshot()
    {
        if (_current is null) { _status.Text = "Refresh the dashboard before saving a snapshot."; return; }
        var dialog = new SaveFileDialog { Filter = "JSON diagnostic snapshot (*.json)|*.json|CSV activity snapshot (*.csv)|*.csv", DefaultExt = ".json", AddExtension = true, FileName = "postgresql-diagnostic-snapshot.json", OverwritePrompt = true };
        if (dialog.ShowDialog(this) != true) return;
        string? temp = null;
        try
        {
            var json = Path.GetExtension(dialog.FileName).Equals(".csv", StringComparison.OrdinalIgnoreCase)
                ? ActivityExportService.ToCsv(_current, _includeQuery.IsChecked == true)
                : BuildSnapshotJson(_current, _includeQuery.IsChecked == true);
            var full = Path.GetFullPath(dialog.FileName);
            temp = full + ".pms-snapshot-" + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, full, true);
            _status.Text = $"Diagnostic snapshot saved to {full}.";
        }
        catch (Exception ex) { _status.Text = DesktopErrorPresentation.Failure("Diagnostic snapshot", ex); }
        finally { if (temp is not null && File.Exists(temp)) { try { File.Delete(temp); } catch { } } }
    }

    private string BuildSnapshotJson(ActivitySnapshot snapshot, bool includeQuery)
    {
        var sessions = snapshot.Sessions.Select(x => new { x.ProcessId, x.Database, x.User, x.ApplicationName, x.BackendType, x.State, x.Duration, x.TransactionDuration, x.WaitEventType, x.WaitEvent, x.Blocked, x.BlockingCount, Query = SessionMonitorService.QueryPreview(x.Query, includeQuery ? QueryTextDisplayMode.Show : QueryTextDisplayMode.Hide, 500), x.QueryUnavailable, x.BackendStart }).ToArray();
        return JsonSerializer.Serialize(new { SchemaVersion = 1, SnapshotStarted = snapshot.ServerTime, SnapshotCompleted = DateTimeOffset.UtcNow, Server = _serverIdentity, Sections = new { Activity = true, Blocking = true, Locks = true, QueryText = includeQuery }, snapshot.Summary, Sessions = sessions, snapshot.Blocking, snapshot.Locks, Omitted = new[] { "credentials", "connection strings", "raw table data", "pg_stat_statements query statistics (unavailable)" } }, new JsonSerializerOptions { WriteIndented = true });
    }

    private string BuildDiagnosticText(ActivitySnapshot snapshot) => string.Join(Environment.NewLine, new[]
    {
        $"Snapshot {snapshot.Sequence} at {snapshot.ServerTime:O}",
        $"Blocking relationships: {snapshot.Blocking.Count:N0}; locks returned: {snapshot.Locks.Count:N0}.",
        "Query-level statistics: unavailable; no pg_stat_statements adapter is composed in this release.",
        snapshot.Blocking.Count == 0 ? "No blocking relationships were reported by PostgreSQL." : string.Join(Environment.NewLine, BlockingGraphService.Build(snapshot.Blocking).Warnings),
    });

    private void TogglePause() { _paused = !_paused; _pause.Content = _paused ? "Resume" : "Pause"; _status.Text = _paused ? "Sampling paused; the last snapshot remains visible." : "Sampling resumed."; if (!_paused && _automatic.IsChecked == true) _ = RefreshAsync(); }
    private void StartTimer() { UpdateInterval(); _timer.Start(); }
    private void StopTimer() => _timer.Stop();
    private void UpdateInterval() { _timer.Interval = TimeSpan.FromSeconds(_interval.SelectedIndex switch { 1 => 10, 2 => 30, 3 => 60, _ => 5 }); }
    private void SetBusy(bool busy) { _refresh.IsEnabled = !busy; _snapshot.IsEnabled = !busy; _cancelSession.IsEnabled = !busy && _sessions.SelectedItem is SessionRow; _terminateSession.IsEnabled = !busy && _sessions.SelectedItem is SessionRow; }
    private void UpdateActionState() => SetBusy(false);
    private void CloseWorkspace() { if (_closing) return; _closing = true; _timer.Stop(); _lifetime.Cancel(); _lifetime.Dispose(); }
    private static DataGrid Grid() => new() { IsReadOnly = true, AutoGenerateColumns = false, CanUserSortColumns = true };
    private static SessionRow ToSessionRow(BackendSession x) => new(x.ProcessId, x.Database, x.User, x.ClassifiedState.ToString(), x.Duration.ToString("g"), x.TransactionDuration?.ToString("g") ?? "—", x.WaitEventType is null ? x.WaitEvent ?? "—" : $"{x.WaitEventType}/{x.WaitEvent}", x.Blocked ? "Yes" : "No", SessionMonitorService.QueryPreview(x.Query, QueryTextDisplayMode.Hide));
    private sealed record SessionRow(int ProcessId, string? Database, string? User, string State, string Duration, string TransactionDuration, string Wait, string Blocked, string Query);
    private sealed record BlockingRow(BlockingRelationship Relationship) { public int BlockedPid => Relationship.BlockedProcessId; public int BlockingPid => Relationship.BlockingProcessId; public string Database => Relationship.Database ?? "—"; public string User => Relationship.User ?? "—"; public string WaitEvent => Relationship.WaitEvent ?? "—"; public string BlockedQuery => SessionMonitorService.QueryPreview(Relationship.BlockedQuery, QueryTextDisplayMode.Hide); public string BlockingQuery => SessionMonitorService.QueryPreview(Relationship.BlockingQuery, QueryTextDisplayMode.Hide); }
    private sealed record LockRow(BackendLock Lock) { public int ProcessId => Lock.ProcessId; public string LockType => Lock.LockType ?? "—"; public string Database => Lock.Database ?? "—"; public string Relation => Lock.Relation ?? "—"; public string Mode => Lock.Mode ?? "—"; public string Granted => Lock.Granted ? "Yes" : "No"; public string WaitStart => Lock.WaitStart?.ToString("O") ?? "—"; }
    private static void AddColumns(DataGrid grid, params (string Header, string Path)[] columns) { foreach (var (header, path) in columns) grid.Columns.Add(new DataGridTextColumn { Header = header, Binding = new System.Windows.Data.Binding(path) }); }
}
