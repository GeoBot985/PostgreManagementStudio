using System.Text;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PostgreManagementStudio.Application;
using PostgreManagementStudio.Core;
using PostgreManagementStudio.Results;
using PostgreManagementStudio.Postgres;

namespace PostgreManagementStudio.Desktop;

public partial class QueryTabView : UserControl
{
    private readonly QueryDocument _document; private readonly DestructiveOperationGuard _destructiveOperations; private readonly ApplicationSettings _settings; private IResultSession? _session; private bool _initializing = true; private bool _isUnloaded; public event EventHandler? DirtyChanged; public event EventHandler? WorkspaceStateChanged;
    private readonly BackupRestoreOperationService _backupRestore;
    private readonly PostgreSqlToolDiscoveryService _backupTools;
    private readonly BackupInspectionService _backupInspection;
    private readonly BackupRestoreOperationController _backupController = new();
    private readonly DocumentFileService _fileService = new(); private SqlDocument _file = new() { DisplayName = "Query" };
    public QueryTabView(QueryDocument document, DestructiveOperationGuard destructiveOperations,
        ApplicationSettings settings, BackupRestoreOperationService backupRestore,
        PostgreSqlToolDiscoveryService backupTools, BackupInspectionService backupInspection)
    { InitializeComponent(); _document = document; _destructiveOperations = destructiveOperations; _settings = settings; _backupRestore = backupRestore; _backupTools = backupTools; _backupInspection = backupInspection; _document.ExecutionStateChanged += Document_ExecutionStateChanged; Unloaded += async (_, _) => { _isUnloaded = true; _document.ExecutionStateChanged -= Document_ExecutionStateChanged; try { await _backupController.DisposeAsync(); } catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"Query workspace cleanup failed: {SecretRedactor.Redact(ex.Message)}"); } }; SqlText.Text = document.SqlText; DatabaseText.Text = document.Database; _file = new SqlDocument { DisplayName = document.Title }; _initializing = false; UpdateCommandState(); }

    public QueryDocument Document => _document;
    public bool IncludeActualPlan { get; set; }
    public bool HasResults => _session?.ResultSets.Count > 0;
    public bool IsExecuting => _document.IsExecuting || _backupController.CanCancel;
    public bool CanExecute => _document.CanExecute && !_backupController.CanCancel;
    public bool CanCancel => _document.CanCancel || _backupController.CanCancel;
    public string DisplayTitle => $"{(_file.FilePath is null ? _document.Title : Path.GetFileName(_file.FilePath))}{(_document.IsDirty ? "*" : string.Empty)}";
    public string SafeToolTip => $"{(_file.FilePath ?? "Unsaved query")}{Environment.NewLine}{SafeConnectionDisplay}";
    public string SafeConnectionDisplay
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_document.ConnectionString)) return "Disconnected";
            var value = DatabaseConnection.FromConnectionString(_document.ConnectionString);
            return $"{value.Username}@{value.Host}:{value.Port}/{_document.Database}";
        }
    }
    public string QueryStatus => _document.Message;
    public long RowsReceived => _session?.ReceivedRowCount ?? 0;
    public long RowsAffected => _session?.RowsAffected ?? 0;
    public TimeSpan? ExecutionElapsed => _document.LastExecutionContext is not { } context
        ? null
        : _document.IsExecuting ? DateTimeOffset.UtcNow - context.StartedAt : _session?.Elapsed;
    public (int Line, int Column) CaretPosition
    {
        get
        {
            var length = Math.Min(SqlText.CaretIndex, SqlText.Text.Length);
            var line = 1;
            var lastBreak = -1;
            for (var index = 0; index < length; index++)
                if (SqlText.Text[index] == '\n') { line++; lastBreak = index; }
            return (line, length - lastBreak);
        }
    }

    public void ApplyConnection(ShellConnectionInfo? connection)
    {
        if (_document.IsExecuting) throw new InvalidOperationException("A running query retains its existing connection.");
        _document.ConnectionProfileId = connection is null ? string.Empty : connection.IsDevelopmentFallback ? "environment:PMS_CONNECTION_STRING" : "interactive";
        _document.ConnectionString = connection?.ConnectionString ?? string.Empty;
        _document.Database = connection?.Database ?? "postgres";
        DatabaseText.Text = _document.Database;
        UpdateCommandState();
    }

    public void ChangeDatabase(string database)
    {
        if (_document.IsExecuting || string.IsNullOrWhiteSpace(database)) return;
        _document.Database = database.Trim();
        DatabaseText.Text = _document.Database;
        WorkspaceStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task ExecuteAsync()
    {
        if (!_document.CanExecute) return;
        if (IncludeActualPlan) { await ShowPlanAsync(PlanType.Actual); return; }
        _document.SqlText = SqlText.Text; _document.Database = DatabaseText.Text; var selected = SqlText.SelectionLength > 0 ? SqlText.SelectedText : null; StatusText.Text = "Preparing"; MessagesText.Clear(); UpdateCommandState();
        try
        {
            var session = await _document.ExecuteAsync(selected); _session = session; var output = new StringBuilder(_document.Message); ResultTabs.Items.Clear();
            if (session is not null)
            {
                output.AppendLine();
                foreach (var notice in session.Notices) output.AppendLine($"NOTICE [{notice.Severity}]: {notice.Message}");
                for (var resultIndex = 0; resultIndex < session.ResultSets.Count; resultIndex++)
                {
                    var store = session.ResultSets[resultIndex];
                    var rows = await store.GetRowsAsync(0, checked((int)store.LoadedRowCount), CancellationToken.None);
                    ResultTabs.Items.Add(CreateResultTab(store, rows));
                }
                if (session.WasTruncated) output.AppendLine($"Results truncated: displaying {session.RetainedRowCount:N0} of {session.ReceivedRowCount:N0} rows ({session.TruncationReason}).");
                else if (session.ReceivedRowCount >= _settings.ResultWarningThreshold) output.AppendLine($"Large result warning: {session.ReceivedRowCount:N0} rows were loaded.");
                if (session.ResultSets.Count > 0) ResultSummary.Text = string.Join(" | ", session.ResultSets.Select((s, i) => $"Results {i + 1}: {s.LoadedRowCount:N0} displayed / {s.FinalRowCount:N0} received · {s.Schema.Columns.Count} columns"));
                HighlightErrorPosition(session.Error, selected);
            }
            MessagesText.Text = output.ToString();
            OutputTabs.SelectedIndex = session?.ResultSets.Count > 0 ? 0 : 1;
            StatusText.Text = _document.LastExecutionContext is { } context ? $"{_document.State} · {context.ServerIdentity} / {context.Database}" : _document.State.ToString();
        }
        catch (Exception ex) { StatusText.Text = "Error"; MessagesText.Text = SecretRedactor.Redact(ex.Message); }
        finally { UpdateCommandState(); DirtyChanged?.Invoke(this, EventArgs.Empty); }
    }
    public async Task CancelAsync()
    {
        if (_backupController.CanCancel) _backupController.Cancel();
        if (_document.CanCancel) await _document.CancelAsync();
        UpdateCommandState();
    }
    private void Document_ExecutionStateChanged(object? sender, EventArgs e)
    {
        if (_isUnloaded) return;
        if (!Dispatcher.CheckAccess()) { _ = Dispatcher.BeginInvoke(UpdateCommandState); return; }
        UpdateCommandState();
    }
    private void UpdateCommandState()
    {
        ExecuteButton.IsEnabled = _document.CanExecute;
        CancelButton.IsEnabled = _document.CanCancel || _backupController.CanCancel;
        DatabaseText.IsEnabled = !_document.IsExecuting && !_backupController.CanCancel;
        if (_document.IsExecuting) StatusText.Text = _document.Message;
        WorkspaceStateChanged?.Invoke(this, EventArgs.Empty);
    }
    private void HighlightErrorPosition(DatabaseError? error, string? selectedSql)
    {
        if (error?.Position is not > 0) return;
        var offset = error.Position.Value - 1;
        var sourceLength = selectedSql?.Length ?? SqlText.Text.Length;
        if (offset >= sourceLength || selectedSql is not null) return;
        SqlText.Select(offset, Math.Min(1, SqlText.Text.Length - offset));
        SqlText.Focus();
    }
    private TabItem CreateResultTab(IResultSetStore store, IReadOnlyList<ResultRow> rows)
    {
        var view = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, CanUserResizeColumns = true, SelectionUnit = DataGridSelectionUnit.CellOrRowHeader, HeadersVisibility = DataGridHeadersVisibility.All, EnableRowVirtualization = true, EnableColumnVirtualization = true };
        var state = new ResultTabState(store.Schema, rows, view);
        view.Sorting += (_, e) => { e.Handled = true; var ordinal = view.Columns.IndexOf(e.Column) - 1; if (ordinal >= 0) { var direction = e.Column.SortDirection == ListSortDirection.Ascending ? SortDirection.Descending : SortDirection.Ascending; state.ViewState = state.ViewState with { Sorts = new[] { new SortDescriptor(ordinal, direction, NullPlacement.Last, 0) } }; ApplyResultView(state); e.Column.SortDirection = direction == SortDirection.Ascending ? ListSortDirection.Ascending : ListSortDirection.Descending; } };
        view.Columns.Add(new DataGridTextColumn { Header = "#", Binding = new System.Windows.Data.Binding("RowIndex"), Width = 55 });
        for (var column = 0; column < store.Schema.Columns.Count; column++) view.Columns.Add(new DataGridTextColumn { Header = $"{store.Schema.Columns[column].Name}\n{store.Schema.Columns[column].PostgreSqlTypeName}", Binding = new System.Windows.Data.Binding($"Values[{column}]"), Width = new DataGridLength(1, DataGridLengthUnitType.Star), MinWidth = 80, MaxWidth = 420 });
        state.Apply(rows.Select((row, index) => new GridRow(index, row.Cells.Select((cell, i) => new DefaultResultValueFormatter().FormatForDisplay(cell, store.Schema.Columns[i], new(_settings.CellDisplayLimit))).ToArray())).ToArray()); return new TabItem { Header = $"Results {store.ResultSetIndex + 1}", Content = view, Tag = state };
    }
    private void ResultSearch_Click(object sender, RoutedEventArgs e) { if (ResultTabs.SelectedItem is TabItem { Tag: ResultTabState state }) { state.ViewState = state.ViewState with { Search = new(ResultSearchText.Text) }; ApplyResultView(state); } }
    public void ShowResultSearch() { ResultSearchPanel.Visibility = Visibility.Visible; ResultSearchText.Focus(); }
    public void ShowOutput(int index) { OutputTabs.SelectedIndex = Math.Clamp(index, 0, 2); }
    public void ClearResultView() { ResultSearchText.Clear(); if (ResultTabs.SelectedItem is TabItem { Tag: ResultTabState state }) { state.ViewState = ResultViewState.Empty; ApplyResultView(state); foreach (var c in state.Grid.Columns) c.SortDirection = null; } }
    private void ApplyResultView(ResultTabState state) { var result = new ResultViewTransformationService().Transform(state.Schema, state.Rows, state.ViewState); if (result.Error is not null) { MessagesText.Text = result.Error; return; } state.Grid.ItemsSource = result.VisibleRowIndexes.Select(i => state.DisplayRows[i]).ToArray(); ResultSummary.Text = $"Visible: {result.VisibleRowIndexes.Count:N0} / {state.Rows.Count:N0}"; }
    public void CopyResults(bool includeHeaders) => CopyGrid(includeHeaders);
    public async Task ExportResultsAsync()
    {
        if (_session is null || ResultTabs.SelectedIndex < 0 || ResultTabs.SelectedIndex >= _session.ResultSets.Count) return;
        var dialog = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv|TSV (*.tsv)|*.tsv|JSON (*.json)|*.json|SQL inserts (*.sql)|*.sql", DefaultExt = ".csv", AddExtension = true, FileName = "query-results" }; if (dialog.ShowDialog() != true) return; var format = dialog.FilterIndex switch { 2 => ResultExportFormat.Tsv, 3 => ResultExportFormat.Json, 4 => ResultExportFormat.SqlInsert, _ => ResultExportFormat.Csv }; try { var outcome = await new ResultExportService().ExportAsync(new ResultExportRequest(_session.ResultSets[ResultTabs.SelectedIndex], null, format, ResultExportScope.EntireResult, dialog.FileName, new()), new Progress<ResultExportProgress>(p => StatusText.Text = $"{p.Phase}: {p.RowsWritten:N0}")); StatusText.Text = outcome.Completed ? $"Exported {outcome.RowsWritten:N0} rows to {outcome.Path}" : "Export cancelled."; } catch (Exception ex) { MessagesText.Text = $"Export failed: {ex.Message}"; }
    }
    private void CopyGrid(bool headers) { if (ResultTabs.SelectedItem is not TabItem { Tag: ResultTabState state }) return; var grid = state.Grid; var lines = new List<string>(); if (headers) lines.Add(string.Join("\t", grid.Columns.Skip(1).Select(c => c.Header?.ToString()?.Split('\n')[0]))); foreach (var item in grid.SelectedItems.Cast<GridRow>()) lines.Add(string.Join("\t", item.Values)); if (lines.Count > 0) Clipboard.SetText(string.Join(Environment.NewLine, lines)); }
    public async Task OpenFileAsync() { var dialog = new OpenFileDialog { Filter = "SQL files (*.sql)|*.sql|All files (*.*)|*.*", Multiselect = false }; if (dialog.ShowDialog() != true) return; try { var loaded = await _fileService.LoadAsync(dialog.FileName); _initializing = true; _file = SqlDocument.FromLoaded(loaded); SqlText.Text = _file.Text; _document.SqlText = _file.Text; _document.MarkDirty(false); _initializing = false; StatusText.Text = $"Opened {dialog.FileName}"; DirtyChanged?.Invoke(this, EventArgs.Empty); } catch (Exception ex) { _initializing = false; MessagesText.Text = ex.Message; OutputTabs.SelectedIndex = 1; } }
    public async Task<bool> SaveAsync() => _file.FilePath is null ? await SaveAsAsync() : await SaveToAsync(_file.FilePath);
    public async Task<bool> SaveAsAsync() { var dialog = new SaveFileDialog { Filter = "SQL files (*.sql)|*.sql|All files (*.*)|*.*", DefaultExt = ".sql", AddExtension = true, FileName = Path.GetFileName(_file.FilePath ?? _document.Title) }; return dialog.ShowDialog() == true && await SaveToAsync(dialog.FileName); }
    private async Task<bool> SaveToAsync(string path) { try { _file.SetText(SqlText.Text); await _fileService.SaveAsync(_file, path); _document.MarkDirty(false); StatusText.Text = $"Saved {path}"; DirtyChanged?.Invoke(this, EventArgs.Empty); WorkspaceStateChanged?.Invoke(this, EventArgs.Empty); return true; } catch (Exception ex) { MessagesText.Text = $"Save failed: {ex.Message}"; OutputTabs.SelectedIndex = 1; return false; } }
    public void FindNext() { if (string.IsNullOrEmpty(FindText.Text)) return; var index = new FindReplaceService().FindNext(SqlText.Text, FindText.Text, SqlText.SelectionStart + SqlText.SelectionLength, new()); if (index >= 0) { SqlText.Select(index, FindText.Text.Length); SqlText.Focus(); } else StatusText.Text = "No match."; }
    public void ShowFind(bool includeReplace)
    {
        FindPanel.Visibility = Visibility.Visible;
        ReplaceLabel.Visibility = includeReplace ? Visibility.Visible : Visibility.Collapsed;
        ReplaceText.Visibility = includeReplace ? Visibility.Visible : Visibility.Collapsed;
        ReplaceAllButton.Visibility = includeReplace ? Visibility.Visible : Visibility.Collapsed;
        FindText.Focus();
        FindText.SelectAll();
    }
    private void CloseFind_Click(object sender, RoutedEventArgs e) { FindPanel.Visibility = Visibility.Collapsed; SqlText.Focus(); }
    private void ReplaceAll_Click(object sender, RoutedEventArgs e) { if (string.IsNullOrEmpty(FindText.Text)) return; var service = new FindReplaceService(); var result = service.ReplaceAll(SqlText.Text, FindText.Text, ReplaceText.Text, new(), out var count); if (count > 0) SqlText.Text = result; StatusText.Text = $"{count} replacements made."; }
    public void GoToLine() { var dialog = new InputDialog("Go to line", "Line number:"); if (dialog.ShowDialog() != true || !int.TryParse(dialog.Value, out var line) || line < 1) { StatusText.Text = "Enter a positive line number."; return; } var index = 0; for (var i = 1; i < line && index < SqlText.Text.Length; i++) index = SqlText.Text.IndexOf('\n', index) + 1; if (index <= 0 && line > 1) { StatusText.Text = "Line is beyond the document."; return; } SqlText.Focus(); SqlText.CaretIndex = index; }
    private void SqlText_TextChanged(object sender, TextChangedEventArgs e) { _document.SqlText = SqlText.Text; if (_initializing) return; _document.MarkDirty(); DirtyChanged?.Invoke(this, EventArgs.Empty); WorkspaceStateChanged?.Invoke(this, EventArgs.Empty); }
    private void SqlText_SelectionChanged(object sender, RoutedEventArgs e) => WorkspaceStateChanged?.Invoke(this, EventArgs.Empty);
    private async void UserControl_KeyDown(object sender, System.Windows.Input.KeyEventArgs e) { if (e.Key == System.Windows.Input.Key.Space && System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control) { var items = await new SqlCompletionEngine().GetCompletionsAsync(SqlText.Text, SqlText.CaretIndex, null); var menu = new ContextMenu(); foreach (var item in items.Take(30)) { var entry = new MenuItem { Header = $"{item.DisplayText} [{item.Kind}]" }; entry.Click += (_, _) => { var start = SqlText.CaretIndex; while (start > 0 && (char.IsLetterOrDigit(SqlText.Text[start - 1]) || SqlText.Text[start - 1] == '_')) start--; SqlText.Select(start, SqlText.CaretIndex - start); SqlText.SelectedText = item.InsertionText; }; menu.Items.Add(entry); } menu.IsOpen = true; e.Handled = true; } }
    public async Task BackupDatabaseAsync()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "PostgreSQL custom backup (*.backup)|*.backup|Plain SQL (*.sql)|*.sql|Tar archive (*.tar)|*.tar",
            DefaultExt = ".backup", AddExtension = true, FileName = "database.backup",
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            MessagesText.Clear();
            StatusText.Text = "Validating backup…";
            var cs = CurrentConnectionString();
            var connection = DatabaseConnection.FromConnectionString(cs) with { Database = DatabaseText.Text };
            var tools = await _backupTools.DiscoverAsync();
            var format = dialog.FilterIndex switch
            {
                2 => BackupFormat.PlainSql,
                3 => BackupFormat.Tar,
                _ => BackupFormat.Custom,
            };
            var plan = BackupOperationPlanFactory.CreateBackup(_document.ConnectionProfileId, connection.Host,
                new(connection, dialog.FileName, format), tools, null);
            UpdateCommandState();
            var result = await _backupRestore.ExecuteBackupAsync(plan, _backupController,
                new Progress<ProcessOutputEntry>(AppendBackupOutput));
            ShowBackupRestoreResult(result, "Backup");
        }
        catch (Exception ex)
        {
            MessagesText.Text = BackupSecretRedactor.Redact(ex.Message);
            StatusText.Text = "Backup unavailable.";
        }
        finally { UpdateCommandState(); }
    }

    public async Task RestoreDatabaseAsync()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "PostgreSQL backups (*.backup;*.sql;*.tar)|*.backup;*.sql;*.tar|All files (*.*)|*.*",
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            MessagesText.Clear();
            StatusText.Text = "Inspecting backup…";
            var cs = CurrentConnectionString();
            var connection = DatabaseConnection.FromConnectionString(cs) with { Database = DatabaseText.Text };
            var tools = await _backupTools.DiscoverAsync();
            var detected = BackupInspectionService.DetectFormat(dialog.FileName)
                ?? throw new BackupRestoreException(BackupRestoreFailureCategory.InvalidBackup,
                    "The selected file is not a recognised PostgreSQL backup.");
            var inspection = await _backupInspection.InspectAsync(dialog.FileName, detected, tools.Paths);
            var options = new RestoreOptions(connection, dialog.FileName, detected, SingleTransaction: detected != BackupFormat.Directory);
            var plan = BackupOperationPlanFactory.CreateRestore(_document.ConnectionProfileId, connection.Host,
                options, inspection, tools, null);
            if (!_destructiveOperations.Confirm(new(DestructiveOperationKind.Restore,
                "Confirm restore", connection.Database, RestoreConfirmation.Summary(plan),
                "Create and verify a current backup before continuing."))) return;
            var token = RestoreConfirmation.Create(plan);
            UpdateCommandState();
            var result = await _backupRestore.ExecuteRestoreAsync(plan, token, _backupController,
                new Progress<ProcessOutputEntry>(AppendBackupOutput));
            ShowBackupRestoreResult(result, "Restore");
        }
        catch (Exception ex)
        {
            MessagesText.Text = BackupSecretRedactor.Redact(ex.Message);
            StatusText.Text = "Restore unavailable.";
        }
        finally { UpdateCommandState(); }
    }

    private void AppendBackupOutput(ProcessOutputEntry entry) =>
        MessagesText.AppendText($"{entry.Timestamp:HH:mm:ss} {(entry.IsError ? "ERR" : "OUT")} {entry.Line}{Environment.NewLine}");

    private void ShowBackupRestoreResult(BackupRestoreExecutionResult? result, string operation)
    {
        if (result is null) { StatusText.Text = $"{operation} result superseded."; return; }
        StatusText.Text = $"{operation}: {result.State} ({result.CompletedAt - result.StartedAt:g})";
        MessagesText.AppendText($"{Environment.NewLine}{result.Message}");
        if (result.TargetMayBePartiallyModified)
            MessagesText.AppendText($"{Environment.NewLine}WARNING: the target may contain partial changes.");
        foreach (var warning in result.Warnings)
            MessagesText.AppendText($"{Environment.NewLine}WARNING: {warning}");
    }
    public async Task ShowSecurityRolesAsync() { try { var roles = await new NpgsqlSecurityService().LoadRolesAsync(CurrentConnectionString()); MessagesText.Text = string.Join(Environment.NewLine, roles.Select(r => $"{r.Name} {(r.CanLogin ? "LOGIN" : "GROUP")} {(r.IsSuperuser ? "SUPERUSER" : "")}")); StatusText.Text = $"Loaded {roles.Count:N0} roles."; OutputTabs.SelectedIndex = 1; } catch (Exception ex) { MessagesText.Text = SecretRedactor.Redact(ex.Message); StatusText.Text = "Security metadata unavailable."; OutputTabs.SelectedIndex = 1; } }
    public async Task ShowActivityMonitorAsync() { try { var snapshot = await new NpgsqlActivityService().LoadSnapshotAsync(CurrentConnectionString(), DateTime.UtcNow.Ticks); ResultSummary.Text = $"Sessions {snapshot.Summary.TotalSessions:N0} · Active {snapshot.Summary.ActiveSessions:N0} · Idle {snapshot.Summary.IdleSessions:N0} · Blocked {snapshot.Summary.BlockedSessions:N0}"; MessagesText.Text = string.Join(Environment.NewLine, snapshot.Sessions.Select(s => $"{s.ProcessId} {s.ClassifiedState} {s.Database} {s.User} {s.Duration:g} {s.Query}")); StatusText.Text = $"Activity snapshot {snapshot.ServerTime:O}"; OutputTabs.SelectedIndex = 1; } catch (Exception ex) { MessagesText.Text = SecretRedactor.Redact(ex.Message); StatusText.Text = "Activity monitor unavailable."; OutputTabs.SelectedIndex = 1; } }
    public async Task RunMaintenanceAsync() { try { var cs = CurrentConnectionString(); var connection = DatabaseConnection.FromConnectionString(cs) with { Database = DatabaseText.Text }; var plan = new MaintenancePlan(MaintenanceOperation.Vacuum, new[] { new MaintenanceTarget(MaintenanceTargetKind.Database, connection.Database) }, new(Analyze: true, Verbose: true), new(18)); var sql = string.Join(Environment.NewLine, plan.Statements); if (!_destructiveOperations.Confirm(new(DestructiveOperationKind.Maintenance, "Maintenance confirmation", connection.Database, $"Run maintenance on a dedicated connection? This may take time and hold locks.{Environment.NewLine}{Environment.NewLine}{sql}", "Cancel before confirmation or wait for PostgreSQL to finish safely."))) return; MessagesText.Text = sql; OutputTabs.SelectedIndex = 1; var result = await new NpgsqlMaintenanceService().ExecuteAsync(cs, plan, new Progress<string>(x => MessagesText.AppendText(x + Environment.NewLine))); StatusText.Text = result.Status; } catch (Exception ex) { MessagesText.Text = SecretRedactor.Redact(ex.Message); StatusText.Text = "Maintenance unavailable."; OutputTabs.SelectedIndex = 1; } }
    public async Task ImportDataAsync() { var dialog = new OpenFileDialog { Filter = "Delimited files (*.csv;*.tsv;*.txt)|*.csv;*.tsv;*.txt|All files (*.*)|*.*" }; if (dialog.ShowDialog() != true) return; var tableDialog = new InputDialog("Import data", "Destination table (public schema):"); if (tableDialog.ShowDialog() != true || string.IsNullOrWhiteSpace(tableDialog.Value)) return; try { var settings = DelimitedFileDetector.Detect(dialog.FileName) with { HasHeader = true }; var header = new DelimitedFileReader().Read(dialog.FileName, settings).FirstOrDefault(); if (header is null) throw new InvalidOperationException("The source file contains no data rows."); var request = new ImportRequest(dialog.FileName, "public", tableDialog.Value, header.Select((name, i) => new ColumnMapping(i, name)).ToArray(), settings, new(ImportStrategy.BatchInsert, Transaction: TransactionMode.AllRows), header.Select(name => new DestinationColumn(name, "text", true)).ToArray()); var result = await new NpgsqlDataTransferService().ImportAsync(CurrentConnectionString(), request, new Progress<ImportProgress>(p => StatusText.Text = $"{p.Phase}: {p.RowsWritten:N0} rows")); StatusText.Text = result.Status; MessagesText.Text = string.Join(Environment.NewLine, result.Errors); OutputTabs.SelectedIndex = 1; } catch (Exception ex) { MessagesText.Text = SecretRedactor.Redact(ex.Message); StatusText.Text = "Import unavailable."; OutputTabs.SelectedIndex = 1; } }
    public async Task SearchObjectsAsync() { var dialog = new InputDialog("Search database objects", "Name or wildcard:"); if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.Value)) return; try { var batch = await new NpgsqlObjectSearchService().SearchAsync(CurrentConnectionString(), new ObjectSearchOptions(dialog.Value)); ResultSummary.Text = $"Found {batch.Results.Count:N0} objects in {batch.Duration.TotalMilliseconds:N0} ms"; MessagesText.Text = string.Join(Environment.NewLine, batch.Results.Select(x => $"{x.ObjectType} {x.Schema}.{x.ObjectName}")); if (batch.Warnings.Count > 0) MessagesText.AppendText(Environment.NewLine + string.Join(Environment.NewLine, batch.Warnings)); StatusText.Text = batch.LimitReached ? "Search limit reached." : "Search complete."; OutputTabs.SelectedIndex = 1; } catch (OperationCanceledException) { StatusText.Text = "Search cancelled."; } catch (Exception ex) { MessagesText.Text = SecretRedactor.Redact(ex.Message); StatusText.Text = "Search unavailable."; OutputTabs.SelectedIndex = 1; } }
    public Task ShowEstimatedPlanAsync() => ShowPlanAsync(PlanType.Estimated);
    private async Task ShowPlanAsync(PlanType type) { var sql = SqlText.SelectionLength > 0 ? SqlText.SelectedText : SqlText.Text; if (type == PlanType.Actual && !_destructiveOperations.Confirm(new(DestructiveOperationKind.ActualExecutionPlan, "Confirm actual execution plan", DatabaseText.Text, "Actual plan analysis executes the selected SQL; data changes, locks, triggers, and external side effects are possible.", "Use read-only SQL or an explicit transaction with rollback when possible."))) return; try { var request = new ExplainRequest(sql, new(type, Buffers: type == PlanType.Actual, StatementTimeout: type == PlanType.Actual ? TimeSpan.FromSeconds(30) : null)); var plan = await new NpgsqlExecutionPlanService().ExplainAsync(CurrentConnectionString(), request); var summary = PlanMetricsService.Summarize(plan); ResultSummary.Text = $"{type} plan: {summary.NodeCount} nodes · Cost {summary.TotalCost} · Rows {summary.RootRows} · Actual {summary.ActualRows}"; MessagesText.Text = plan.RawJson; PlanTabs.Items.Clear(); PlanTabs.Items.Add(CreatePlanTab(plan)); OutputTabs.SelectedIndex = 2; StatusText.Text = "Execution plan complete."; } catch (Exception ex) { MessagesText.Text = SecretRedactor.Redact(ex.Message); OutputTabs.SelectedIndex = 1; StatusText.Text = "Execution plan unavailable."; } finally { WorkspaceStateChanged?.Invoke(this, EventArgs.Empty); } }
    private string CurrentConnectionString() => !string.IsNullOrWhiteSpace(_document.ConnectionString)
        ? _document.ConnectionString
        : throw new InvalidOperationException("This query is disconnected. Use File > Connect or Query > Change Connection.");
    private TabItem CreatePlanTab(ExecutionPlanDocument plan)
    { var panel = new DockPanel(); var raw = new TextBox { Text = plan.RawJson, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, FontFamily = new System.Windows.Media.FontFamily("Consolas"), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, Height = 180 }; DockPanel.SetDock(raw, Dock.Bottom); panel.Children.Add(raw); var tree = new TreeView { Margin = new Thickness(4) }; tree.Items.Add(CreatePlanTreeNode(plan.Root, "root")); panel.Children.Add(tree); return new TabItem { Header = "Execution Plan", Content = panel, Tag = plan }; }
    private static TreeViewItem CreatePlanTreeNode(ExecutionPlanNode node, string path)
    { var title = $"{node.NodeType}{(node.RelationName is null ? "" : " — " + node.RelationName)} · cost {node.TotalCost?.ToString("N2") ?? "n/a"} · rows {node.PlanRows?.ToString("N0") ?? "n/a"}{(node.ActualTime is null ? "" : $" · actual {node.ActualTime:N2} ms")}"; var item = new TreeViewItem { Header = title, ToolTip = $"Node {path}; actual rows {node.ActualRows?.ToString("N0") ?? "unavailable"}; loops {node.Loops?.ToString("N0") ?? "unavailable"}" }; for (var i = 0; i < node.Children.Count; i++) item.Items.Add(CreatePlanTreeNode(node.Children[i], path + "." + i)); return item; }
    private sealed record GridRow(long RowIndex, IReadOnlyList<string> Values);
    private sealed class ResultTabState(ResultSetSchema schema, IReadOnlyList<ResultRow> rows, DataGrid grid)
    { public ResultSetSchema Schema { get; } = schema; public IReadOnlyList<ResultRow> Rows { get; } = rows; public DataGrid Grid { get; } = grid; public GridRow[] DisplayRows { get; private set; } = Array.Empty<GridRow>(); public ResultViewState ViewState { get; set; } = ResultViewState.Empty; public void Apply(GridRow[] rows) { DisplayRows = rows; Grid.ItemsSource = rows; } }
    private sealed class InputDialog : Window { public string Value => Box.Text; private readonly TextBox Box = new(); public InputDialog(string title, string prompt) { Title = title; Width = 300; Height = 130; WindowStartupLocation = WindowStartupLocation.CenterOwner; var panel = new StackPanel { Margin = new Thickness(10) }; panel.Children.Add(new TextBlock { Text = prompt }); panel.Children.Add(Box); var button = new Button { Content = "OK", IsDefault = true, Margin = new Thickness(0, 8, 0, 0) }; button.Click += (_, _) => DialogResult = true; panel.Children.Add(button); Content = panel; } }
}
