using System.Text;
using System.ComponentModel;
using System.Collections.ObjectModel;
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
    private readonly QueryDocument _document;
    private readonly DestructiveOperationGuard _destructiveOperations;
    private readonly ApplicationSettings _settings;
    private readonly IPerformanceDiagnostics _performanceDiagnostics;
    private readonly IEditorObjectResolver _editorObjectResolver;
    private readonly ObjectDescriptionService _objectDescriptions;
    private readonly ObservableCollection<DescriptionColumnRow> _descriptionRows = [];
    private CancellationTokenSource? _descriptionCancellation;
    private ObjectDescription? _description;
    private EditorObjectReference? _descriptionReference;
    private DescriptionEditorBinding? _descriptionBinding;
    private readonly ResultDisplayPageService _resultPages = new();
    private readonly LatestRequestCoordinator<IReadOnlyList<CompletionItem>> _completionRequests = new();
    private readonly LatestRequestCoordinator<ObjectSearchBatch> _searchRequests = new();
    private IResultSession? _session;
    private ShellConnectionInfo? _connection;
    private CancellationTokenSource? _resultPageCancellation;
    private bool _initializing = true;
    private bool _isUnloaded;
    private long _documentVersion;
    public event EventHandler? DirtyChanged;
    public event EventHandler? WorkspaceStateChanged;
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
    private RestoreWorkspaceWindow? _restoreWorkspace;
    private ObjectSearchWorkspaceWindow? _objectSearchWorkspace;
    private MaintenanceWorkspaceWindow? _maintenanceWorkspace;
    private PlanExplorerWindow? _planWorkspace;
    private IndexWorkspaceWindow? _indexWorkspace;
    private SchemaComparisonWorkspaceWindow? _schemaWorkspace;
    private DataTransferWorkspaceWindow? _transferWorkspace;
    private MonitoringWorkspaceWindow? _monitoringWorkspace;
    private readonly BackupRestoreOperationController _backupController = new();
    private readonly DocumentFileService _fileService = new(); private SqlDocument _file = new() { DisplayName = "Query" };
    private Guid _recoveryId = Guid.NewGuid();
    public QueryTabView(QueryDocument document, DestructiveOperationGuard destructiveOperations,
        ApplicationSettings settings, BackupRestoreOperationService backupRestore,
        PostgreSqlToolDiscoveryService backupTools, BackupInspectionService backupInspection,
        NpgsqlObjectSearchService objectSearch,
        PostgresVersionService postgresVersion,
        NpgsqlIndexAnalysisService indexAnalysis,
        NpgsqlSchemaModelExtractor schemaExtractor,
        NpgsqlDataTransferService dataTransfer,
        IResultExportService resultExport,
        TransferHistoryService transferHistory,
        IPerformanceDiagnostics performanceDiagnostics,
        IEditorObjectResolver editorObjectResolver,
        ObjectDescriptionService objectDescriptions)
    {
        InitializeComponent();
        _document = document;
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
        _performanceDiagnostics = performanceDiagnostics;
        _editorObjectResolver = editorObjectResolver;
        _objectDescriptions = objectDescriptions;
        _document.ExecutionStateChanged += Document_ExecutionStateChanged;
        Unloaded += QueryTabView_Unloaded;
        SqlText.Text = document.SqlText;
        DatabaseText.Text = document.Database;
        _file = new SqlDocument { DisplayName = document.Title };
        _initializing = false;
        UpdateCommandState();
    }

    private async void QueryTabView_Unloaded(object sender, RoutedEventArgs e)
    {
        _isUnloaded = true;
        _restoreWorkspace?.Close();
        _objectSearchWorkspace?.Close();
        _maintenanceWorkspace?.Close();
        _planWorkspace?.Close();
        _indexWorkspace?.Close();
        _schemaWorkspace?.Close();
        _transferWorkspace?.Close();
        _monitoringWorkspace?.Close();
        _document.ExecutionStateChanged -= Document_ExecutionStateChanged;
        if (_connection is not null) _connection.Session.StateChanged -= RecoverySession_StateChanged;
        try
        {
            _resultPageCancellation?.Cancel();
            _resultPageCancellation?.Dispose();
            _descriptionCancellation?.Cancel();
            _descriptionCancellation?.Dispose();
            await DisposeResultTabStatesAsync();
            await _completionRequests.DisposeAsync();
            await _searchRequests.DisposeAsync();
            await _backupController.DisposeAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine(
                $"Query workspace cleanup failed: {SecretRedactor.Redact(ex.Message)}");
        }
    }

    public QueryDocument Document => _document;

    public void InsertDraggedColumn(ObjectExplorerNode node)
    {
        var text = node.QualifiedName ?? PostgreSqlIdentifierQuoter.Quote(node.RawName);
        var start = Math.Clamp(SqlText.SelectionStart, 0, SqlText.Text.Length);
        var length = Math.Clamp(SqlText.SelectionLength, 0, SqlText.Text.Length - start);
        SqlText.Select(start, length);
        SqlText.SelectedText = text;
        SqlText.CaretIndex = start + text.Length;
        SqlText.Focus();
    }

    private void SqlText_PreviewDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(ObjectExplorerNode)) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void SqlText_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(ObjectExplorerNode)) is ObjectExplorerNode { Kind: ObjectExplorerNodeKind.Column } node)
            InsertDraggedColumn(node);
        e.Handled = true;
    }
    public Guid RecoveryId => _recoveryId;
    public ShellConnectionInfo? Connection => _connection;
    public bool IncludeActualPlan { get; set; }
    public bool HasResults => ReadStatusSession(session => session.ResultSets.Count > 0, false);
    public bool IsExecuting => _document.IsExecuting || _backupController.CanCancel;
    private bool IsRecoveryConnected => _connection?.Session.Snapshot.State == RecoveryConnectionState.Connected;
    public bool CanExecute => _document.CanExecute && IsRecoveryConnected && !_backupController.CanCancel;
    public bool CanCancel => _document.CanCancel || _backupController.CanCancel;
    public string DisplayTitle => $"{(_file.FilePath is null ? _document.Title : Path.GetFileName(_file.FilePath))}{(_document.IsDirty ? "*" : string.Empty)}";
    public string SafeToolTip => $"{(_file.FilePath ?? "Unsaved query")}{Environment.NewLine}{SafeConnectionDisplay}";
    public string SafeConnectionDisplay
    {
        get
        {
            if (_connection is { } connection && connection.Session.Snapshot.State != RecoveryConnectionState.Connected)
                return $"{connection.Username}@{connection.Host}:{connection.Port}/{_document.Database} ({connection.Session.Snapshot.State})";
            if (string.IsNullOrWhiteSpace(_document.ConnectionString)) return "Disconnected";
            var value = DatabaseConnection.FromConnectionString(_document.ConnectionString);
            return $"{value.Username}@{value.Host}:{value.Port}/{_document.Database}";
        }
    }
    public string QueryStatus => _document.BackendStateMayBeStale && IsRecoveryConnected
        ? $"{_document.Message} Backend-session state may be stale."
        : _document.Message;
    public long RowsReceived => ReadStatusSession(session => session.ReceivedRowCount, 0L);
    public long RowsAffected => ReadStatusSession(session => session.RowsAffected, 0L);
    public TimeSpan? ExecutionElapsed => _document.LastExecutionContext is not { } context
        ? null
        : _document.IsExecuting
            ? DateTimeOffset.UtcNow - context.StartedAt
            : ReadStatusSession(session => session.Elapsed, null);
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

    private T ReadStatusSession<T>(Func<IResultSession, T> read, T fallback)
    {
        var documentSession = _document.Session;
        if (documentSession is not null)
        {
            try { return read(documentSession); }
            catch (ObjectDisposedResultStoreException) { }
        }
        if (_session is not null && !ReferenceEquals(_session, documentSession))
        {
            try { return read(_session); }
            catch (ObjectDisposedResultStoreException) { }
        }
        return fallback;
    }

    public void ApplyConnection(ShellConnectionInfo? connection, string? databaseOverride = null)
    {
        if (_document.IsExecuting) throw new InvalidOperationException("A running query retains its existing connection.");
        _restoreWorkspace?.Close();
        _objectSearchWorkspace?.Close();
        _maintenanceWorkspace?.Close();
        _planWorkspace?.Close();
        _indexWorkspace?.Close();
        _schemaWorkspace?.Close();
        _transferWorkspace?.Close();
        _monitoringWorkspace?.Close();
        if (_connection is not null) _connection.Session.StateChanged -= RecoverySession_StateChanged;
        _connection = connection;
        if (_connection is not null) _document.Database = databaseOverride ?? _connection.Database;
        if (_connection is not null) _connection.Session.StateChanged += RecoverySession_StateChanged;
        ApplyRecoverySnapshot(connection?.Session.Snapshot);
        DatabaseText.Text = _document.Database;
        UpdateCommandState();
    }

    private void RecoverySession_StateChanged(object? sender, EventArgs e)
    {
        if (_isUnloaded) return;
        if (!Dispatcher.CheckAccess()) { _ = Dispatcher.BeginInvoke(() => RecoverySession_StateChanged(sender, e)); return; }
        ApplyRecoverySnapshot(_connection?.Session.Snapshot);
        UpdateCommandState();
    }

    private void ApplyRecoverySnapshot(ConnectionRecoverySnapshot? snapshot)
    {
        if (_connection is null || snapshot is null)
        {
            var generation = _document.ConnectionGenerationId;
            if (generation != Guid.Empty)
                _document.InvalidateConnection(generation, "Disconnected from PostgreSQL.");
            else
            {
                _document.ConnectionProfileId = string.Empty;
                _document.ConnectionString = string.Empty;
            }
            return;
        }
        if (snapshot.State == RecoveryConnectionState.Connected)
        {
            var database = string.IsNullOrWhiteSpace(_document.Database) ? _connection.Database : _document.Database;
            _document.ReplaceConnection(
                _connection.Configuration.Profile.Id,
                _connection.ConnectionString,
                database,
                snapshot.GenerationId);
            DatabaseText.Text = database;
            return;
        }
        if (snapshot.State is RecoveryConnectionState.Degraded or RecoveryConnectionState.Failed
            or RecoveryConnectionState.Disconnected or RecoveryConnectionState.Reconnecting)
        {
            var message = snapshot.Failure?.Message ?? $"Connection state: {snapshot.State}.";
            _document.InvalidateConnection(snapshot.GenerationId, message);
        }
    }

    public void ChangeDatabase(string database)
    {
        if (_document.IsExecuting || string.IsNullOrWhiteSpace(database)) return;
        _document.Database = database.Trim();
        DatabaseText.Text = _document.Database;
        WorkspaceStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public RecoverySnapshot CreateRecoverySnapshot()
    {
        var caretOffset = Math.Clamp(SqlText.CaretIndex, 0, SqlText.Text.Length);
        return new(
            _recoveryId,
            _file.DisplayName,
            _file.FilePath,
            SqlText.Text,
            DateTimeOffset.UtcNow,
            _file.EncodingKind,
            _document.Database,
            caretOffset);
    }

    public void RestoreRecoverySnapshot(RecoverySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (_document.IsExecuting)
            throw new InvalidOperationException("A running query cannot be replaced by recovered content.");

        _initializing = true;
        try
        {
            _recoveryId = snapshot.Id;
            _file = SqlDocument.FromRecovery(snapshot);
            SqlText.Text = snapshot.Text;
            SqlText.CaretIndex = Math.Clamp(snapshot.CaretOffset, 0, snapshot.Text.Length);
            _document.SqlText = snapshot.Text;
            _document.Database = snapshot.Database;
            _document.MarkDirty();
            DatabaseText.Text = snapshot.Database;
            StatusText.Text = $"Recovered unsaved query from {snapshot.Timestamp.LocalDateTime:g}.";
        }
        finally
        {
            _initializing = false;
        }
    }

    public async Task ExecuteAsync()
    {
        if (!CanExecute) return;
        if (IncludeActualPlan) { await ShowPlanAsync(PlanType.Actual); return; }
        _document.SqlText = SqlText.Text; _document.Database = DatabaseText.Text; var selected = SqlText.SelectionLength > 0 ? SqlText.SelectedText : null; StatusText.Text = "Preparing"; MessagesText.Clear(); UpdateCommandState();
        try
        {
            var generationToken = _connection?.Session.GenerationToken ?? CancellationToken.None;
            var session = await _document.ExecuteAsync(selected, generationToken);
            var output = new StringBuilder(_document.Message);
            if (session is not null)
            {
                output.AppendLine();
                foreach (var notice in session.Notices) output.AppendLine($"NOTICE [{notice.Severity}]: {notice.Message}");
                if (session.Status == ResultSessionStatus.Completed)
                {
                    _session = session;
                    await DisposeResultTabStatesAsync();
                    ResultTabs.Items.Clear();
                    for (var resultIndex = 0; resultIndex < session.ResultSets.Count; resultIndex++)
                    {
                        var store = session.ResultSets[resultIndex];
                        ResultTabs.Items.Add(await CreateResultTabAsync(store, generationToken));
                    }
                    if (ResultTabs.Items.Count > 0) ResultTabs.SelectedIndex = 0;
                    if (session.WasTruncated) output.AppendLine($"Results truncated: displaying {session.RetainedRowCount:N0} of {session.ReceivedRowCount:N0} rows ({session.TruncationReason}).");
                    else if (session.ReceivedRowCount >= _settings.ResultWarningThreshold) output.AppendLine($"Large result warning: {session.ReceivedRowCount:N0} rows were loaded.");
                }
                HighlightErrorPosition(session.Error, selected);
                if (session.Status == ResultSessionStatus.Failed && session.Error?.Kind == DatabaseErrorKind.ConnectionLost)
                    _connection?.Session.ReportFailure(
                        DatabaseFailureClassifier.FromSqlState(session.Error.SqlState, session.Error.Message));
                if (session.Status != ResultSessionStatus.Completed)
                    await session.DisposeAsync();
            }
            MessagesText.Text = output.ToString();
            OutputTabs.SelectedIndex = session?.Status == ResultSessionStatus.Completed && session.ResultSets.Count > 0 ? 0 : 1;
            StatusText.Text = _document.LastExecutionContext is { } context ? $"{_document.State} · {context.ServerIdentity} / {context.Database}" : _document.State.ToString();
        }
        catch (Exception ex) { StatusText.Text = "Error"; MessagesText.Text = SecretRedactor.Redact(ex.Message); }
        finally { UpdateCommandState(); DirtyChanged?.Invoke(this, EventArgs.Empty); }
    }

    public async Task DescribeObjectAsync()
    {
        var binding = CaptureDescriptionBinding();
        var reference = _editorObjectResolver.Resolve(
            SqlText.Text, SqlText.CaretIndex, SqlText.SelectionStart, SqlText.SelectionLength);
        if (reference is null)
        {
            ShowDescriptionMessage("Place the caret on, or select, a PostgreSQL object name.");
            return;
        }
        _descriptionReference = reference;
        _descriptionBinding = binding;
        OutputTabs.SelectedItem = DescriptionTab;
        DescriptionSummary.Text = $"Resolving {reference.DisplayText}…";
        WorkspaceStateChanged?.Invoke(this, EventArgs.Empty);
        if (!IsRecoveryConnected)
        {
            ShowDescriptionMessage(
                $"Selected: {reference.DisplayText}. Live metadata requires a connected query editor; reconnect and retry Alt+F1.");
            return;
        }
        if (reference.IsEditorLocal)
        {
            ShowDescriptionMessage(
                $"{reference.DisplayText} is an editor-local CTE. Persistent catalogue metadata is not available.");
            return;
        }

        _descriptionCancellation?.Cancel();
        _descriptionCancellation?.Dispose();
        _descriptionCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _connection?.Session.GenerationToken ?? CancellationToken.None);
        try
        {
            var candidates = await _objectDescriptions.ResolveAsync(
                CurrentConnectionString(), DatabaseText.Text, reference, _descriptionCancellation.Token);
            if (candidates.Count == 0)
            {
                ShowDescriptionMessage(
                    $"{reference.DisplayText} was not found in the active database or search path. It may have been renamed or dropped.");
                return;
            }
            var candidate = SelectCandidate(candidates, reference.DisplayText);
            if (candidate is null)
            {
                SqlText.Focus();
                return;
            }
            DescriptionSummary.Text = $"Loading {candidate.QualifiedName}…";
            var targetColumn = reference.MemberName
                ?? (reference.NameParts.Count >= 3 ? reference.NameParts[^1] : null);
            var description = await _objectDescriptions.LoadAsync(
                CurrentConnectionString(), DatabaseText.Text, candidate, targetColumn,
                _descriptionCancellation.Token);
            BindDescription(reference, description, binding);
            try
            {
                var secondary = await _objectDescriptions.LoadSecondaryAsync(
                    CurrentConnectionString(), DatabaseText.Text, candidate,
                    _descriptionCancellation.Token);
                if (_description?.Candidate.Identity.Equals(candidate.Identity) == true)
                    PresentSecondaryDetails(secondary);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                DescriptionSummary.Text += $" · secondary details unavailable: {SecretRedactor.Redact(ex.Message)}";
            }
        }
        catch (OperationCanceledException)
        {
            ShowDescriptionMessage($"Description cancelled for {reference.DisplayText}.");
        }
        catch (Exception ex)
        {
            ShowDescriptionMessage(SecretRedactor.Redact(ex.Message));
        }
    }

    private ObjectDescriptionCandidate? SelectCandidate(
        IReadOnlyList<ObjectDescriptionCandidate> candidates, string target)
    {
        if (candidates.Count == 1) return candidates[0];
        var visible = candidates.Where(candidate => candidate.IsVisible).ToArray();
        if (visible.Length == 1) return visible[0];
        var dialog = new DescriptionCandidateDialog(target, visible.Length > 1 ? visible : candidates)
        {
            Owner = Window.GetWindow(this),
        };
        return dialog.ShowDialog() == true ? dialog.SelectedCandidate : null;
    }

    private void PresentDescription(ObjectDescription description)
    {
        _description = description;
        _descriptionRows.Clear();
        foreach (var column in description.Columns.OrderBy(column => column.Ordinal))
            _descriptionRows.Add(new(column));
        DescriptionColumns.ItemsSource = _descriptionRows;
        DescriptionPreset.SelectedIndex = 0;
        DescriptionFilter.Clear();
        var size = description.SizeBytes is null ? string.Empty : $" · {FormatBytes(description.SizeBytes.Value)}";
        var rows = description.EstimatedRows is null ? string.Empty : $" · ~{description.EstimatedRows:N0} rows";
        var target = description.TargetColumn is null ? string.Empty : $" · column {description.TargetColumn}";
        DescriptionSummary.Text =
            $"{description.Candidate.QualifiedName} · {description.Candidate.ObjectType} · owner {description.Candidate.Owner} · {description.Persistence}{rows}{size}{target}";
        DescriptionText.Text = BuildPlainText(description);
        DescriptionDefinition.Text = description.Definition ?? description.DetailsText;
        DescriptionModes.SelectedIndex = description.Columns.Count > 0 ? 0 : 1;
        OutputTabs.SelectedItem = DescriptionTab;
        if (description.TargetColumn is not null)
        {
            var row = _descriptionRows.FirstOrDefault(item =>
                item.Name.Equals(description.TargetColumn, StringComparison.Ordinal));
            if (row is not null)
            {
                DescriptionColumns.SelectedItem = row;
                DescriptionColumns.ScrollIntoView(row);
            }
        }
        WorkspaceStateChanged?.Invoke(this, EventArgs.Empty);
    }

    internal void BindDescription(
        EditorObjectReference reference,
        ObjectDescription description,
        DescriptionEditorBinding? binding = null)
    {
        _descriptionReference = reference;
        _descriptionBinding = binding ?? CaptureDescriptionBinding();
        PresentDescription(description);
    }

    private DescriptionEditorBinding CaptureDescriptionBinding() => new(
        _document.TabId,
        Interlocked.Read(ref _documentVersion),
        SqlText.CaretIndex,
        SqlText.Text,
        _document.ConnectionGenerationId,
        DatabaseText.Text);

    private void PresentSecondaryDetails(ObjectDescriptionSecondaryDetails secondary)
    {
        if (_description is null) return;
        _description = _description with
        {
            SizeBytes = secondary.SizeBytes,
            DetailsText = secondary.DetailsText,
        };
        var size = secondary.SizeBytes is null
            ? string.Empty : $" · {FormatBytes(secondary.SizeBytes.Value)}";
        if (!string.IsNullOrEmpty(size) && !DescriptionSummary.Text.Contains(size, StringComparison.Ordinal))
            DescriptionSummary.Text += size;
        DescriptionText.Text = BuildPlainText(_description);
        if (_description.Definition is null)
            DescriptionDefinition.Text = secondary.DetailsText;
    }

    private void ShowDescriptionMessage(string message)
    {
        _description = null;
        _descriptionBinding = null;
        _descriptionRows.Clear();
        DescriptionColumns.ItemsSource = _descriptionRows;
        DescriptionSummary.Text = message;
        DescriptionText.Text = message;
        DescriptionDefinition.Clear();
        DescriptionModes.SelectedIndex = 1;
        OutputTabs.SelectedItem = DescriptionTab;
        WorkspaceStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private static string BuildPlainText(ObjectDescription description)
    {
        var text = new StringBuilder()
            .AppendLine($"Object: {description.Candidate.QualifiedName}")
            .AppendLine($"Type: {description.Candidate.ObjectType}")
            .AppendLine($"Owner: {description.Candidate.Owner}")
            .AppendLine($"Persistence: {description.Persistence}");
        if (!string.IsNullOrWhiteSpace(description.Comment))
            text.AppendLine($"Comment: {description.Comment}");
        if (description.Columns.Count > 0)
        {
            text.AppendLine().AppendLine("Columns").AppendLine("-------");
            foreach (var column in description.Columns.OrderBy(column => column.Ordinal))
            {
                var keys = string.Join(" ", new[]
                {
                    column.IsPrimaryKey ? "PK" : null,
                    column.IsForeignKey ? "FK" : null,
                    column.IsUnique ? "UQ" : null,
                }.Where(value => value is not null));
                var generated = column.GeneratedExpression is null
                    ? string.Empty : $" generated {column.GeneratedExpression}";
                var identity = string.IsNullOrWhiteSpace(column.IdentityMode)
                    ? string.Empty : $" identity {column.IdentityMode}";
                var defaultText = column.DefaultExpression is null
                    ? string.Empty : $" default {column.DefaultExpression}";
                text.Append(column.Ordinal.ToString().PadLeft(3)).Append("  ")
                    .Append(column.Name.PadRight(24)).Append(' ')
                    .Append(column.DataType.PadRight(30)).Append(' ')
                    .Append(column.IsNullable ? "NULL     " : "NOT NULL ")
                    .Append(keys).Append(defaultText).Append(identity).Append(generated).AppendLine();
            }
        }
        if (!string.IsNullOrWhiteSpace(description.DetailsText))
            text.AppendLine().AppendLine("Details").AppendLine("-------").AppendLine(description.DetailsText);
        return text.ToString();
    }

    private void DescriptionPreset_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_descriptionRows.Count == 0 || DescriptionPreset.SelectedIndex < 0) return;
        var preset = (ColumnListPreset)DescriptionPreset.SelectedIndex;
        var selected = RelationColumnListService.ApplyPreset(
            _descriptionRows.Select(row => row.Column), preset);
        foreach (var row in _descriptionRows) row.IsIncluded = selected.Contains(row.Ordinal);
        DescriptionColumns.Items.Refresh();
    }

    private void DescriptionSelectAll_Click(object sender, RoutedEventArgs e) =>
        SetDescriptionInclusion(_ => true);
    private void DescriptionClear_Click(object sender, RoutedEventArgs e) =>
        SetDescriptionInclusion(_ => false);
    private void DescriptionInvert_Click(object sender, RoutedEventArgs e) =>
        SetDescriptionInclusion(row => !row.IsIncluded);

    private void SetDescriptionInclusion(Func<DescriptionColumnRow, bool> selector)
    {
        foreach (var row in _descriptionRows) row.IsIncluded = selector(row);
        DescriptionColumns.Items.Refresh();
    }

    private void DescriptionFilter_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (DescriptionColumns is null) return;
        var filter = DescriptionFilter.Text.Trim();
        DescriptionColumns.ItemsSource = string.IsNullOrWhiteSpace(filter)
            ? _descriptionRows
            : _descriptionRows.Where(row =>
                row.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToArray();
    }

    private void DescriptionCopy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(SelectedColumnList());
            DescriptionSummary.Text = $"{_description?.Candidate.QualifiedName} · column list copied.";
        }
        catch (Exception ex) { ShowDescriptionMessage(SecretRedactor.Redact(ex.Message)); }
    }

    private void DescriptionInsert_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var edit = ColumnListInsertionService.Insert(
                SqlText.Text, SqlText.SelectionStart, SqlText.SelectionLength,
                SqlText.CaretIndex, SelectedColumnList());
            ApplyEditorEdit(edit);
        }
        catch (Exception ex) { ShowDescriptionMessage(SecretRedactor.Redact(ex.Message)); }
    }

    private void DescriptionReplaceWildcard_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ReplaceDescriptionWildcard();
        }
        catch (Exception ex) { ShowDescriptionMessage(SecretRedactor.Redact(ex.Message)); }
    }

    internal void ReplaceDescriptionWildcard()
    {
        var binding = _descriptionBinding
            ?? throw new InvalidOperationException("Describe the relation again before replacing a wildcard.");
        if (binding.QueryTabId != _document.TabId)
            throw new InvalidOperationException(
                "The description belongs to a different query tab. Run Alt+F1 in this tab and retry.");
        if (binding.DocumentVersion != Interlocked.Read(ref _documentVersion)
            || !string.Equals(binding.SqlSnapshot, SqlText.Text, StringComparison.Ordinal))
            throw new InvalidOperationException(
                "The query changed after Alt+F1. Describe the relation again before replacing a wildcard.");
        var qualified = _descriptionReference?.RelationAlias is not null;
        var format = qualified ? ColumnListFormat.QualifiedSelectList : ColumnListFormat.SelectList;
        var formatted = FormatSelected(format, binding.CaretIndex);
        var edit = ColumnListInsertionService.ReplaceWildcard(
            SqlText.Text, binding.CaretIndex, formatted, _descriptionReference?.RelationAlias);
        ApplyEditorEdit(edit);
    }

    private string SelectedColumnList()
    {
        if (_description is null || _descriptionRows.All(row => !row.IsIncluded))
            throw new InvalidOperationException("Select at least one described column.");
        return FormatSelected((ColumnListFormat)Math.Max(0, DescriptionFormat.SelectedIndex));
    }

    private string FormatSelected(ColumnListFormat format, int? caretIndex = null)
    {
        var lineEnding = SqlText.Text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var indentation = DetectIndentation(SqlText.Text, caretIndex ?? SqlText.CaretIndex);
        return ColumnListFormatter.Format(
            _descriptionRows.Where(row => row.IsIncluded).Select(row => row.Column),
            format, _descriptionReference?.RelationAlias, lineEnding, indentation);
    }

    private void ApplyEditorEdit(EditorTextEdit edit)
    {
        SqlText.BeginChange();
        try
        {
            SqlText.Select(edit.Start, edit.Length);
            SqlText.SelectedText = edit.Replacement;
            SqlText.CaretIndex = edit.CaretIndex;
        }
        finally { SqlText.EndChange(); }
        SqlText.Focus();
    }

    private static string DetectIndentation(string sql, int caret)
    {
        var lineStart = sql.LastIndexOf('\n', Math.Max(0, caret - 1)) + 1;
        var count = 0;
        while (lineStart + count < sql.Length && sql[lineStart + count] is ' ' or '\t') count++;
        var existing = sql.Substring(lineStart, count);
        return existing + "    ";
    }

    private void DescriptionColumns_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Space
            && DescriptionColumns.SelectedItems.Cast<DescriptionColumnRow>().ToArray() is { Length: > 0 } rows)
        {
            foreach (var row in rows) row.IsIncluded = !row.IsIncluded;
            DescriptionColumns.Items.Refresh();
            e.Handled = true;
        }
        else if (e.Key == System.Windows.Input.Key.Escape)
        {
            SqlText.Focus();
            e.Handled = true;
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] suffixes = ["B", "KiB", "MiB", "GiB", "TiB"];
        var value = (double)bytes;
        var index = 0;
        while (value >= 1024 && index < suffixes.Length - 1) { value /= 1024; index++; }
        return $"{value:N1} {suffixes[index]}";
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
        ExecuteButton.IsEnabled = CanExecute;
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
    private async Task<TabItem> CreateResultTabAsync(
        IResultSetStore store,
        CancellationToken cancellationToken)
    {
        var view = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserResizeColumns = true,
            SelectionUnit = DataGridSelectionUnit.CellOrRowHeader,
            HeadersVisibility = DataGridHeadersVisibility.All,
            EnableRowVirtualization = true,
            EnableColumnVirtualization = true,
        };
        VirtualizingPanel.SetIsVirtualizing(view, true);
        VirtualizingPanel.SetVirtualizationMode(view, VirtualizationMode.Recycling);
        ScrollViewer.SetIsDeferredScrollingEnabled(view, true);
        var state = new ResultTabState(store, view);
        view.Sorting += async (_, e) =>
        {
            e.Handled = true;
            var ordinal = view.Columns.IndexOf(e.Column) - 1;
            if (ordinal < 0) return;
            var direction = e.Column.SortDirection == ListSortDirection.Ascending
                ? SortDirection.Descending
                : SortDirection.Ascending;
            state.ViewState = state.ViewState with
            {
                Sorts = new[] { new SortDescriptor(ordinal, direction, NullPlacement.Last, 0) },
            };
            await ApplyResultViewAsync(state, TimeSpan.Zero);
            e.Column.SortDirection = direction == SortDirection.Ascending
                ? ListSortDirection.Ascending
                : ListSortDirection.Descending;
        };
        view.Columns.Add(new DataGridTextColumn { Header = "#", Binding = new System.Windows.Data.Binding("RowIndex"), Width = 55 });
        for (var column = 0; column < store.Schema.Columns.Count; column++)
            view.Columns.Add(new DataGridTextColumn
            {
                Header = $"{store.Schema.Columns[column].Name}\n{store.Schema.Columns[column].PostgreSqlTypeName}",
                Binding = new System.Windows.Data.Binding($"Values[{column}]"),
                Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                MinWidth = 80,
                MaxWidth = 420,
            });
        await LoadResultPageAsync(state, 0, cancellationToken);
        return new TabItem
        {
            Header = $"Results {store.ResultSetIndex + 1}",
            Content = view,
            Tag = state,
        };
    }
    private async void ResultSearch_Click(object sender, RoutedEventArgs e)
    {
        if (ResultTabs.SelectedItem is not TabItem { Tag: ResultTabState state }) return;
        try
        {
            state.ViewState = state.ViewState with { Search = new(ResultSearchText.Text) };
            await ApplyResultViewAsync(state, TimeSpan.FromMilliseconds(200));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            MessagesText.Text = DesktopErrorPresentation.Failure("Result search", ex);
            OutputTabs.SelectedIndex = 1;
        }
    }
    public void ShowResultSearch() { ResultSearchPanel.Visibility = Visibility.Visible; ResultSearchText.Focus(); }
    public void ShowOutput(int index) { OutputTabs.SelectedIndex = Math.Clamp(index, 0, 2); }
    public void ClearResultView()
    {
        ResultSearchText.Clear();
        if (ResultTabs.SelectedItem is not TabItem { Tag: ResultTabState state }) return;
        state.ViewState = ResultViewState.Empty;
        state.Grid.ItemsSource = state.DisplayRows;
        foreach (var column in state.Grid.Columns) column.SortDirection = null;
        UpdateResultPageSummary(state);
    }

    private async Task ApplyResultViewAsync(ResultTabState state, TimeSpan debounce)
    {
        var pageVersion = state.PageVersion;
        var result = await state.TransformRequests.RunAsync(
            pageVersion,
            debounce,
            token => new ResultViewTransformationService().TransformAsync(
                state.Store.Schema,
                state.SourceRows,
                state.ViewState,
                token));
        if (!result.Applied || result.ContextVersion != state.PageVersion || result.Value is null) return;
        if (result.Value.Error is not null)
        {
            MessagesText.Text = result.Value.Error;
            return;
        }
        state.Grid.ItemsSource = result.Value.VisibleRowIndexes.Select(index => state.DisplayRows[index]).ToArray();
        ResultSummary.Text =
            $"Page matches: {result.Value.VisibleRowIndexes.Count:N0} / {state.SourceRows.Count:N0} · " +
            PageRange(state);
    }

    private async Task LoadResultPageAsync(
        ResultTabState state,
        long startRowIndex,
        CancellationToken cancellationToken)
    {
        _resultPageCancellation?.Cancel();
        _resultPageCancellation?.Dispose();
        _resultPageCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var operation = new PerformanceOperation(
            startRowIndex == 0 ? "ResultGrid.FirstPage" : "ResultGrid.Page",
            _performanceDiagnostics,
            _connection?.Session.LogicalSessionId,
            _connection?.Session.Snapshot.GenerationId);
        try
        {
            var page = await _resultPages.LoadAsync(
                state.Store,
                startRowIndex,
                ResultDisplayPageService.DefaultPageSize,
                _settings.CellDisplayLimit,
                _resultPageCancellation.Token);
            state.Apply(page);
            operation.RowsRead = page.SourceRows.Count;
            operation.RowsDisplayed = page.DisplayRows.Count;
            operation.BytesProcessed = state.Store.EstimatedMemoryBytes;
            UpdateResultPageSummary(state);
        }
        catch (OperationCanceledException)
        {
            operation.Cancel();
        }
        catch
        {
            operation.Fail("result_rendering");
            throw;
        }
    }

    private async void PreviousResultPage_Click(object sender, RoutedEventArgs e)
    {
        if (ResultTabs.SelectedItem is not TabItem { Tag: ResultTabState state } || state.Page is null) return;
        await NavigateResultPageAsync(state, Math.Max(0, state.Page.StartRowIndex - state.Page.PageSize));
    }

    private async void NextResultPage_Click(object sender, RoutedEventArgs e)
    {
        if (ResultTabs.SelectedItem is not TabItem { Tag: ResultTabState state } || state.Page is null) return;
        await NavigateResultPageAsync(state, state.Page.StartRowIndex + state.Page.PageSize);
    }

    private async Task NavigateResultPageAsync(ResultTabState state, long startRowIndex)
    {
        try
        {
            await LoadResultPageAsync(
                state,
                startRowIndex,
                _connection?.Session.GenerationToken ?? CancellationToken.None);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            MessagesText.Text = DesktopErrorPresentation.Failure("Result page loading", ex);
            OutputTabs.SelectedIndex = 1;
        }
    }

    private void UpdateResultPageSummary(ResultTabState state)
    {
        if (state.Page is null) return;
        ResultSummary.Text =
            $"{PageRange(state)} · {state.Store.ReceivedRowCount:N0} read · " +
            $"{state.Store.LoadedRowCount:N0} retained" +
            (state.Store.WasTruncated ? $" · display truncated ({state.Store.TruncationReason})" : string.Empty) +
            (state.Page.IncompletePreviewCount > 0
                ? $" · {state.Page.IncompletePreviewCount:N0} bounded cell previews"
                : string.Empty);
        PreviousResultPageButton.IsEnabled = state.Page.HasPrevious;
        NextResultPageButton.IsEnabled = state.Page.HasNext;
    }

    private static string PageRange(ResultTabState state) =>
        state.Page is null || state.Page.SourceRows.Count == 0
            ? "Rows 0 / 0"
            : $"Rows {state.Page.StartRowIndex + 1:N0}–{state.Page.EndRowIndex + 1:N0} / {state.Page.RetainedRowCount:N0}";
    public void CopyResults(bool includeHeaders) => CopyGrid(includeHeaders);
    public Task OpenExportWorkspaceAsync()
    {
        if (_session is null || ResultTabs.SelectedIndex < 0 || ResultTabs.SelectedIndex >= _session.ResultSets.Count) { MessagesText.Text = "Execute a query with a result set before exporting."; OutputTabs.SelectedIndex = 1; return Task.CompletedTask; }
        if (_transferWorkspace is { IsVisible: true }) { _transferWorkspace.Activate(); return Task.CompletedTask; }
        var state = (ResultTabs.Items[ResultTabs.SelectedIndex] as TabItem)?.Tag as ResultTabState;
        var selection = state is null ? null : CurrentResultSelection(state);
        _transferWorkspace = new DataTransferWorkspaceWindow(DataTransferWorkspaceMode.Export, _transferHistory, CurrentConnectionString(), exportService: _resultExport, resultSet: _session.ResultSets[ResultTabs.SelectedIndex], resultSelection: selection) { Owner = Window.GetWindow(this) };
        _transferWorkspace.Closed += (_, _) => _transferWorkspace = null; _transferWorkspace.Show(); return Task.CompletedTask;
    }
    public Task OpenRelationExportWorkspaceAsync(TransferRelationSource source)
    {
        if (!IsRecoveryConnected) { MessagesText.Text = "Reconnect before exporting relation data."; OutputTabs.SelectedIndex = 1; return Task.CompletedTask; }
        if (_transferWorkspace is { IsVisible: true }) { _transferWorkspace.Activate(); return Task.CompletedTask; }
        _transferWorkspace = new DataTransferWorkspaceWindow(DataTransferWorkspaceMode.Export,
            _transferHistory, CurrentConnectionString(), relationSource: source)
            { Owner = Window.GetWindow(this) };
        _transferWorkspace.Closed += (_, _) => _transferWorkspace = null;
        _transferWorkspace.Show();
        return Task.CompletedTask;
    }
    private static ResultSelection? CurrentResultSelection(ResultTabState state)
    {
        var cells = state.Grid.SelectedCells
            .Where(cell => cell.Item is FormattedResultRow && cell.Column.DisplayIndex > 0)
            .Select(cell => (Row: ((FormattedResultRow)cell.Item).RowIndex,
                Column: cell.Column.DisplayIndex - 1)).ToArray();
        if (cells.Length == 0) return null;
        return new(cells.Min(cell => cell.Row), cells.Max(cell => cell.Row),
            cells.Min(cell => cell.Column), cells.Max(cell => cell.Column));
    }
    public async Task ExportResultsAsync()
    {
        if (_session is null || ResultTabs.SelectedIndex < 0 || ResultTabs.SelectedIndex >= _session.ResultSets.Count) return;
        var dialog = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv|TSV (*.tsv)|*.tsv|JSON (*.json)|*.json|SQL inserts (*.sql)|*.sql", DefaultExt = ".csv", AddExtension = true, FileName = "query-results" }; if (dialog.ShowDialog() != true) return; var format = dialog.FilterIndex switch { 2 => ResultExportFormat.Tsv, 3 => ResultExportFormat.Json, 4 => ResultExportFormat.SqlInsert, _ => ResultExportFormat.Csv }; try { var outcome = await new ResultExportService().ExportAsync(new ResultExportRequest(_session.ResultSets[ResultTabs.SelectedIndex], null, format, ResultExportScope.EntireResult, dialog.FileName, new()), new Progress<ResultExportProgress>(p => StatusText.Text = $"{p.Phase}: {p.RowsWritten:N0}")); StatusText.Text = outcome.Completed ? $"Exported {outcome.RowsWritten:N0} rows to {outcome.Path}" : "Export cancelled."; } catch (OperationCanceledException) { StatusText.Text = "Export cancelled."; } catch (Exception ex) { MessagesText.Text = DesktopErrorPresentation.Failure("Export", ex); OutputTabs.SelectedIndex = 1; }
    }
    private void CopyGrid(bool headers) { if (ResultTabs.SelectedItem is not TabItem { Tag: ResultTabState state }) return; var grid = state.Grid; var lines = new List<string>(); if (headers) lines.Add(string.Join("\t", grid.Columns.Skip(1).Select(c => c.Header?.ToString()?.Split('\n')[0]))); foreach (var item in grid.SelectedItems.Cast<FormattedResultRow>()) lines.Add(string.Join("\t", item.Values)); if (lines.Count == 0) return; try { Clipboard.SetText(string.Join(Environment.NewLine, lines)); StatusText.Text = $"Copied {lines.Count - (headers ? 1 : 0):N0} rows."; } catch (Exception ex) { MessagesText.Text = DesktopErrorPresentation.Failure("Copy", ex); OutputTabs.SelectedIndex = 1; } }
    public async Task OpenFileAsync() { var dialog = new OpenFileDialog { Filter = "SQL files (*.sql)|*.sql|All files (*.*)|*.*", Multiselect = false }; if (dialog.ShowDialog() != true) return; try { var loaded = await _fileService.LoadAsync(dialog.FileName); _initializing = true; _file = SqlDocument.FromLoaded(loaded); SqlText.Text = _file.Text; _document.SqlText = _file.Text; _document.MarkDirty(false); _initializing = false; StatusText.Text = $"Opened {dialog.FileName}"; DirtyChanged?.Invoke(this, EventArgs.Empty); } catch (OperationCanceledException) { _initializing = false; } catch (Exception ex) { _initializing = false; MessagesText.Text = DesktopErrorPresentation.Failure("Open", ex); OutputTabs.SelectedIndex = 1; } }
    public async Task<bool> SaveAsync() => _file.FilePath is null ? await SaveAsAsync() : await SaveToAsync(_file.FilePath);
    public async Task<bool> SaveAsAsync() { var dialog = new SaveFileDialog { Filter = "SQL files (*.sql)|*.sql|All files (*.*)|*.*", DefaultExt = ".sql", AddExtension = true, FileName = Path.GetFileName(_file.FilePath ?? _document.Title) }; return dialog.ShowDialog() == true && await SaveToAsync(dialog.FileName); }
    private async Task<bool> SaveToAsync(string path) { try { _file.SetText(SqlText.Text); await _fileService.SaveAsync(_file, path); _document.MarkDirty(false); StatusText.Text = $"Saved {path}"; DirtyChanged?.Invoke(this, EventArgs.Empty); WorkspaceStateChanged?.Invoke(this, EventArgs.Empty); return true; } catch (OperationCanceledException) { StatusText.Text = "Save cancelled."; return false; } catch (Exception ex) { MessagesText.Text = DesktopErrorPresentation.Failure("Save", ex); OutputTabs.SelectedIndex = 1; return false; } }
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
    private void SqlText_TextChanged(object sender, TextChangedEventArgs e)
    {
        _document.SqlText = SqlText.Text;
        Interlocked.Increment(ref _documentVersion);
        if (_initializing) return;
        _document.MarkDirty();
        DirtyChanged?.Invoke(this, EventArgs.Empty);
        WorkspaceStateChanged?.Invoke(this, EventArgs.Empty);
    }
    private void SqlText_SelectionChanged(object sender, RoutedEventArgs e) => WorkspaceStateChanged?.Invoke(this, EventArgs.Empty);

    private void SqlText_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Tab || SqlText.SelectionLength == 0) return;
        var start = SqlText.SelectionStart;
        var end = start + SqlText.SelectionLength;
        // A selection contained on one line should use normal TextBox Tab behavior.
        // Only a selection spanning lines is treated as a block indentation command.
        if (!SqlText.Text[start..end].Contains('\n')) return;
        var lineStart = start == 0 ? 0 : SqlText.Text.LastIndexOf('\n', start - 1) + 1;
        var lineEnd = end >= SqlText.Text.Length ? SqlText.Text.Length : SqlText.Text.IndexOf('\n', end);
        if (lineEnd < 0) lineEnd = SqlText.Text.Length;
        var selected = SqlText.Text[lineStart..lineEnd];
        var unindent = System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift);
        var replacement = string.Join("\n", selected.Split('\n').Select(line =>
            unindent
                ? line.StartsWith("\t", StringComparison.Ordinal) ? line[1..]
                    : line.StartsWith("    ", StringComparison.Ordinal) ? line[4..]
                    : line.TrimStart().Length < line.Length ? line[1..] : line
                : "\t" + line));
        SqlText.Select(lineStart, lineEnd - lineStart);
        SqlText.SelectedText = replacement;
        SqlText.Select(lineStart, replacement.Length);
        e.Handled = true;
    }

    private async void UserControl_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Space
            || System.Windows.Input.Keyboard.Modifiers != System.Windows.Input.ModifierKeys.Control)
            return;
        var version = Interlocked.Read(ref _documentVersion);
        var sql = SqlText.Text;
        var caret = SqlText.CaretIndex;
        LatestRequestResult<IReadOnlyList<CompletionItem>> result;
        try
        {
            result = await _completionRequests.RunAsync(
                version,
                TimeSpan.Zero,
                token => new SqlCompletionEngine().GetCompletionsAsync(sql, caret, null, token));
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            StatusText.Text = DesktopErrorPresentation.Failure("Completion", ex);
            return;
        }
        if (!result.Applied || result.ContextVersion != Interlocked.Read(ref _documentVersion)
            || result.Value is null)
            return;
        var menu = new ContextMenu();
        foreach (var item in result.Value.Take(30))
        {
            var entry = new MenuItem { Header = $"{item.DisplayText} [{item.Kind}]" };
            entry.Click += (_, _) =>
            {
                var start = SqlText.CaretIndex;
                while (start > 0 && (char.IsLetterOrDigit(SqlText.Text[start - 1])
                    || SqlText.Text[start - 1] == '_')) start--;
                SqlText.Select(start, SqlText.CaretIndex - start);
                SqlText.SelectedText = item.InsertionText;
            };
            menu.Items.Add(entry);
        }
        menu.IsOpen = true;
        e.Handled = true;
    }
    public async Task BackupDatabaseAsync()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "PostgreSQL custom backup (*.backup)|*.backup|Plain SQL (*.sql)|*.sql|Tar archive (*.tar)|*.tar",
            DefaultExt = ".backup",
            AddExtension = true,
            FileName = "database.backup",
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

    private async Task LegacyRestoreDatabaseAsync()
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
                "Create and verify a current backup before continuing.",
                connection.Host, connection.Database, connection.Database,
                Connection?.Configuration.Profile.EnvironmentDisplayName,
                Connection?.Session.Snapshot.State == RecoveryConnectionState.Connected))) return;
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

    public Task OpenRestoreWorkspaceAsync()
    {
        if (!IsRecoveryConnected) { MessagesText.Text = "Reconnect before opening the restore workspace."; OutputTabs.SelectedIndex = 1; return Task.CompletedTask; }
        if (_restoreWorkspace is { IsVisible: true }) { _restoreWorkspace.Activate(); return Task.CompletedTask; }
        var connection = DatabaseConnection.FromConnectionString(CurrentConnectionString()) with { Database = DatabaseText.Text };
        _restoreWorkspace = new RestoreWorkspaceWindow(_backupRestore, _backupTools, _backupInspection, _destructiveOperations,
            connection, CurrentConnectionString(), _document.ConnectionProfileId, $"{connection.Host}:{connection.Port}", Connection?.Configuration.Profile.EnvironmentDisplayName)
        { Owner = Window.GetWindow(this) };
        _restoreWorkspace.Closed += (_, _) => _restoreWorkspace = null;
        _restoreWorkspace.Show();
        return Task.CompletedTask;
    }

    public async Task ShowSecurityRolesAsync() { try { var roles = await new NpgsqlSecurityService().LoadRolesAsync(CurrentConnectionString()); MessagesText.Text = string.Join(Environment.NewLine, roles.Select(r => $"{UntrustedText.ForDisplay(r.Name)} {(r.CanLogin ? "LOGIN" : "GROUP")} {(r.IsSuperuser ? "SUPERUSER" : "")}")); StatusText.Text = $"Loaded {roles.Count:N0} roles."; OutputTabs.SelectedIndex = 1; } catch (Exception ex) { MessagesText.Text = SecretRedactor.Redact(ex.Message); StatusText.Text = "Security metadata unavailable."; OutputTabs.SelectedIndex = 1; } }
    public async Task ShowActivityMonitorAsync() { try { var snapshot = await new NpgsqlActivityService().LoadSnapshotAsync(CurrentConnectionString(), DateTime.UtcNow.Ticks); ResultSummary.Text = $"Sessions {snapshot.Summary.TotalSessions:N0} · Active {snapshot.Summary.ActiveSessions:N0} · Idle {snapshot.Summary.IdleSessions:N0} · Blocked {snapshot.Summary.BlockedSessions:N0}"; MessagesText.Text = string.Join(Environment.NewLine, snapshot.Sessions.Select(s => UntrustedText.ForDisplay($"{s.ProcessId} {s.ClassifiedState} {s.Database} {s.User} {s.Duration:g} {s.Query}", 2_048))); StatusText.Text = $"Activity snapshot {snapshot.ServerTime:O}"; OutputTabs.SelectedIndex = 1; } catch (Exception ex) { MessagesText.Text = SecretRedactor.Redact(ex.Message); StatusText.Text = "Activity monitor unavailable."; OutputTabs.SelectedIndex = 1; } }
    public async Task RunMaintenanceAsync() { try { var cs = CurrentConnectionString(); var connection = DatabaseConnection.FromConnectionString(cs) with { Database = DatabaseText.Text }; if (Connection?.Configuration.Profile.EffectiveReadOnly == true) throw new InvalidOperationException("Maintenance is disabled because this session is configured read-only."); var plan = new MaintenancePlan(MaintenanceOperation.Vacuum, new[] { new MaintenanceTarget(MaintenanceTargetKind.Database, connection.Database) }, new(Analyze: true, Verbose: true), new(18)); var sql = string.Join(Environment.NewLine, plan.Statements); if (!_destructiveOperations.Confirm(new(DestructiveOperationKind.Maintenance, "Maintenance confirmation", connection.Database, $"Run maintenance on a dedicated connection? This may take time and hold locks.{Environment.NewLine}{Environment.NewLine}{sql}", "Cancel before confirmation or wait for PostgreSQL to finish safely.", connection.Host, connection.Database, connection.Database, Connection?.Configuration.Profile.EnvironmentDisplayName, Connection?.Session.Snapshot.State == RecoveryConnectionState.Connected))) return; MessagesText.Text = sql; OutputTabs.SelectedIndex = 1; var result = await new NpgsqlMaintenanceService().ExecuteAsync(cs, plan, new Progress<string>(x => MessagesText.AppendText(x + Environment.NewLine))); StatusText.Text = result.Status; } catch (Exception ex) { MessagesText.Text = SecretRedactor.Redact(ex.Message); StatusText.Text = "Maintenance unavailable."; OutputTabs.SelectedIndex = 1; } }
    public Task OpenImportWorkspaceAsync(TransferRelationSource? target = null)
    {
        if (!IsRecoveryConnected) { MessagesText.Text = "Reconnect before opening the import workspace."; OutputTabs.SelectedIndex = 1; return Task.CompletedTask; }
        if (_transferWorkspace is { IsVisible: true }) { _transferWorkspace.Activate(); return Task.CompletedTask; }
        _transferWorkspace = new DataTransferWorkspaceWindow(DataTransferWorkspaceMode.Import, _transferHistory, CurrentConnectionString(), importService: _dataTransfer, relationSource: target) { Owner = Window.GetWindow(this) };
        _transferWorkspace.Closed += (_, _) => _transferWorkspace = null; _transferWorkspace.Show(); return Task.CompletedTask;
    }

    public Task OpenSearchWorkspaceAsync()
    {
        if (!IsRecoveryConnected) { MessagesText.Text = "Reconnect before opening the database search workspace."; OutputTabs.SelectedIndex = 1; return Task.CompletedTask; }
        if (_objectSearchWorkspace is { IsVisible: true }) { _objectSearchWorkspace.Activate(); return Task.CompletedTask; }
        var connection = DatabaseConnection.FromConnectionString(CurrentConnectionString()) with { Database = DatabaseText.Text };
        _objectSearchWorkspace = new ObjectSearchWorkspaceWindow(_objectSearch, CurrentConnectionString(), $"{connection.Host}:{connection.Port}", connection.Database)
        { Owner = Window.GetWindow(this) };
        _objectSearchWorkspace.Closed += (_, _) => _objectSearchWorkspace = null;
        _objectSearchWorkspace.Show();
        return Task.CompletedTask;
    }

    public Task OpenMonitoringWorkspaceAsync()
    {
        if (!IsRecoveryConnected) { MessagesText.Text = "Reconnect before opening the performance dashboard."; OutputTabs.SelectedIndex = 1; return Task.CompletedTask; }
        if (_monitoringWorkspace is { IsVisible: true }) { _monitoringWorkspace.Activate(); return Task.CompletedTask; }
        var connection = DatabaseConnection.FromConnectionString(CurrentConnectionString()) with { Database = DatabaseText.Text };
        _monitoringWorkspace = new MonitoringWorkspaceWindow(new NpgsqlActivityService(), CurrentConnectionString(), $"{connection.Host}:{connection.Port}/{connection.Database}") { Owner = Window.GetWindow(this) };
        _monitoringWorkspace.Closed += (_, _) => _monitoringWorkspace = null;
        _monitoringWorkspace.Show();
        return Task.CompletedTask;
    }

    private async Task LegacySearchObjectsAsync()
    {
        var dialog = new InputDialog("Search database objects", "Name or wildcard:");
        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.Value)) return;
        var generation = _connection?.Session.Snapshot.GenerationId ?? Guid.Empty;
        var contextVersion = Interlocked.Increment(ref _documentVersion);
        var connectionString = CurrentConnectionString();
        var searchText = dialog.Value;
        var result = await _searchRequests.RunAsync(
            contextVersion,
            TimeSpan.FromMilliseconds(200),
            token => new NpgsqlObjectSearchService().SearchAsync(
                connectionString,
                new ObjectSearchOptions(searchText),
                token),
            _connection?.Session.GenerationToken ?? CancellationToken.None);
        if (result.State is LatestRequestState.Cancelled or LatestRequestState.Superseded) return;
        if (result.State == LatestRequestState.Failed)
        {
            MessagesText.Text = SecretRedactor.Redact(result.Error?.Message ?? "Search failed.");
            StatusText.Text = "Search unavailable.";
            OutputTabs.SelectedIndex = 1;
            return;
        }
        if (result.Value is not { } batch || generation != _connection?.Session.Snapshot.GenerationId) return;
        ResultSummary.Text = $"Found {batch.Results.Count:N0} objects in {batch.Duration.TotalMilliseconds:N0} ms";
        MessagesText.Text = string.Join(Environment.NewLine,
            batch.Results.Select(item => $"{item.ObjectType} {item.Schema}.{item.ObjectName}"));
        if (batch.Warnings.Count > 0)
            MessagesText.AppendText(Environment.NewLine + string.Join(Environment.NewLine, batch.Warnings));
        StatusText.Text = batch.LimitReached ? "Search limit reached." : "Search complete.";
        OutputTabs.SelectedIndex = 1;
    }
    public Task ShowEstimatedPlanAsync() => ShowPlanAsync(PlanType.Estimated);
    public void FocusPlanWorkspace() { if (_planWorkspace is { IsVisible: true }) _planWorkspace.Activate(); else ShowOutput(2); }
    public Task OpenMaintenanceWorkspaceAsync()
    {
        if (!IsRecoveryConnected) { MessagesText.Text = "Reconnect before opening the maintenance workspace."; OutputTabs.SelectedIndex = 1; return Task.CompletedTask; }
        if (_maintenanceWorkspace is { IsVisible: true }) { _maintenanceWorkspace.Activate(); return Task.CompletedTask; }
        var connection = DatabaseConnection.FromConnectionString(CurrentConnectionString()) with { Database = DatabaseText.Text };
        _maintenanceWorkspace = new MaintenanceWorkspaceWindow(new NpgsqlMaintenanceService(), _postgresVersion, _destructiveOperations,
            CurrentConnectionString(), connection, Connection?.Configuration.Profile.EnvironmentDisplayName ?? "Unknown") { Owner = Window.GetWindow(this) };
        _maintenanceWorkspace.Closed += (_, _) => _maintenanceWorkspace = null; _maintenanceWorkspace.Show(); return Task.CompletedTask;
    }
    public Task OpenIndexWorkspaceAsync()
    {
        if (!IsRecoveryConnected) { MessagesText.Text = "Reconnect before opening index management."; OutputTabs.SelectedIndex = 1; return Task.CompletedTask; }
        if (_indexWorkspace is { IsVisible: true }) { _indexWorkspace.Activate(); return Task.CompletedTask; }
        var connection = DatabaseConnection.FromConnectionString(CurrentConnectionString()) with { Database = DatabaseText.Text };
        _indexWorkspace = new IndexWorkspaceWindow(_indexAnalysis, new NpgsqlMaintenanceService(), _destructiveOperations, _postgresVersion, CurrentConnectionString(), $"{connection.Host}:{connection.Port}", connection.Database) { Owner = Window.GetWindow(this) };
        _indexWorkspace.Closed += (_, _) => _indexWorkspace = null; _indexWorkspace.Show(); return Task.CompletedTask;
    }
    public Task OpenSchemaComparisonWorkspaceAsync()
    {
        if (!IsRecoveryConnected) { MessagesText.Text = "Reconnect before opening schema comparison."; OutputTabs.SelectedIndex = 1; return Task.CompletedTask; }
        if (_schemaWorkspace is { IsVisible: true }) { _schemaWorkspace.Activate(); return Task.CompletedTask; }
        var connection = DatabaseConnection.FromConnectionString(CurrentConnectionString()) with { Database = DatabaseText.Text };
        _schemaWorkspace = new SchemaComparisonWorkspaceWindow(_schemaExtractor, CurrentConnectionString(), connection) { Owner = Window.GetWindow(this) };
        _schemaWorkspace.Closed += (_, _) => _schemaWorkspace = null; _schemaWorkspace.Show(); return Task.CompletedTask;
    }
    private void OpenPlanWorkspace(ExecutionPlanDocument plan)
    {
        _planWorkspace?.Close(); _planWorkspace = new PlanExplorerWindow(plan) { Owner = Window.GetWindow(this) };
        _planWorkspace.Closed += (_, _) => _planWorkspace = null; _planWorkspace.Show();
    }
    private async Task ShowPlanAsync(PlanType type) { var sql = SqlText.SelectionLength > 0 ? SqlText.SelectedText : SqlText.Text; if (type == PlanType.Actual && !_destructiveOperations.Confirm(new(DestructiveOperationKind.ActualExecutionPlan, "Confirm actual execution plan", DatabaseText.Text, "Actual plan analysis executes the selected SQL; data changes, locks, triggers, and external side effects are possible.", "Use read-only SQL or an explicit transaction with rollback when possible.", Connection?.Host, DatabaseText.Text, "selected SQL", Connection?.Configuration.Profile.EnvironmentDisplayName, Connection?.Session.Snapshot.State == RecoveryConnectionState.Connected))) return; try { var request = new ExplainRequest(sql, new(type, Buffers: type == PlanType.Actual, StatementTimeout: type == PlanType.Actual ? TimeSpan.FromSeconds(30) : null)); var plan = await new NpgsqlExecutionPlanService().ExplainAsync(CurrentConnectionString(), request); var summary = PlanMetricsService.Summarize(plan); ResultSummary.Text = $"{type} plan: {summary.NodeCount} nodes · Cost {summary.TotalCost} · Rows {summary.RootRows} · Actual {summary.ActualRows}"; MessagesText.Text = plan.RawJson.Length <= 65_536 ? plan.RawJson : plan.RawJson[..65_536] + Environment.NewLine + "… Raw-plan preview limited to 64 KiB."; PlanTabs.Items.Clear(); PlanTabs.Items.Add(CreatePlanTab(plan)); OutputTabs.SelectedIndex = 2; StatusText.Text = "Execution plan complete."; } catch (Exception ex) { MessagesText.Text = SecretRedactor.Redact(ex.Message); OutputTabs.SelectedIndex = 1; StatusText.Text = "Execution plan unavailable."; } finally { WorkspaceStateChanged?.Invoke(this, EventArgs.Empty); } }
    private string CurrentConnectionString() => IsRecoveryConnected && !string.IsNullOrWhiteSpace(_document.ConnectionString)
        ? _document.ConnectionString
        : throw new InvalidOperationException("This query is disconnected or degraded. Reconnect before running database operations.");
    private TabItem CreatePlanTab(ExecutionPlanDocument plan)
    {
        OpenPlanWorkspace(plan);
        const int maximumRawPreview = 64 * 1024;
        var rawIncomplete = plan.RawJson.Length > maximumRawPreview;
        var raw = new TextBox
        {
            Text = rawIncomplete
                ? plan.RawJson[..maximumRawPreview] + Environment.NewLine +
                    "… Raw-plan preview limited to 64 KiB."
                : plan.RawJson,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Height = 180,
        };
        var panel = new DockPanel();
        DockPanel.SetDock(raw, Dock.Bottom);
        panel.Children.Add(raw);
        var tree = new TreeView { Margin = new Thickness(4) };
        var remainingNodes = 500;
        tree.Items.Add(CreatePlanTreeNode(plan.Root, "root", 0, ref remainingNodes));
        panel.Children.Add(tree);
        if (rawIncomplete || remainingNodes == 0)
            ResultSummary.Text += " · plan preview bounded";
        return new TabItem { Header = "Execution Plan", Content = panel, Tag = plan };
    }

    private static TreeViewItem CreatePlanTreeNode(
        ExecutionPlanNode node,
        string path,
        int depth,
        ref int remainingNodes)
    {
        remainingNodes--;
        var title =
            $"{node.NodeType}{(node.RelationName is null ? "" : " — " + node.RelationName)} · " +
            $"cost {node.TotalCost?.ToString("N2") ?? "n/a"} · rows {node.PlanRows?.ToString("N0") ?? "n/a"}" +
            $"{(node.ActualTime is null ? "" : $" · actual {node.ActualTime:N2} ms")}";
        var item = new TreeViewItem
        {
            Header = title,
            ToolTip =
                $"Node {path}; actual rows {node.ActualRows?.ToString("N0") ?? "unavailable"}; " +
                $"loops {node.Loops?.ToString("N0") ?? "unavailable"}",
        };
        if (depth >= 24 || remainingNodes <= 0)
        {
            if (node.Children.Count > 0)
                item.Items.Add(new TreeViewItem
                {
                    Header = "Additional plan nodes omitted from preview.",
                    IsEnabled = false,
                });
            return item;
        }
        for (var index = 0; index < node.Children.Count && remainingNodes > 0; index++)
            item.Items.Add(CreatePlanTreeNode(
                node.Children[index],
                path + "." + index,
                depth + 1,
                ref remainingNodes));
        return item;
    }
    private async Task DisposeResultTabStatesAsync()
    {
        foreach (var state in ResultTabs.Items.OfType<TabItem>()
            .Select(item => item.Tag).OfType<ResultTabState>())
            await state.TransformRequests.DisposeAsync();
    }

    private sealed class ResultTabState(IResultSetStore store, DataGrid grid)
    {
        public IResultSetStore Store { get; } = store;
        public DataGrid Grid { get; } = grid;
        public ResultDisplayPage? Page { get; private set; }
        public IReadOnlyList<ResultRow> SourceRows => Page?.SourceRows ?? Array.Empty<ResultRow>();
        public IReadOnlyList<FormattedResultRow> DisplayRows =>
            Page?.DisplayRows ?? Array.Empty<FormattedResultRow>();
        public ResultViewState ViewState { get; set; } = ResultViewState.Empty;
        public LatestRequestCoordinator<ResultViewResult> TransformRequests { get; } = new();
        public long PageVersion { get; private set; }

        public void Apply(ResultDisplayPage page)
        {
            Page = page;
            ViewState = ResultViewState.Empty;
            PageVersion++;
            Grid.ItemsSource = page.DisplayRows;
        }
    }
    private sealed class DescriptionColumnRow(ObjectDescriptionColumn column) : INotifyPropertyChanged
    {
        private bool _isIncluded = true;
        public ObjectDescriptionColumn Column { get; } = column;
        public bool IsIncluded
        {
            get => _isIncluded;
            set
            {
                if (_isIncluded == value) return;
                _isIncluded = value;
                PropertyChanged?.Invoke(this, new(nameof(IsIncluded)));
            }
        }
        public int Ordinal => Column.Ordinal;
        public string Name => Column.Name;
        public string DataType => Column.DataType;
        public string NullableText => Column.IsNullable ? "Yes" : "No";
        public string DefaultText => Column.GeneratedExpression is not null
            ? $"generated: {Column.GeneratedExpression}"
            : !string.IsNullOrWhiteSpace(Column.IdentityMode)
                ? $"identity {Column.IdentityMode}"
                : Column.DefaultExpression ?? string.Empty;
        public string KeyText => string.Join("/", new[]
        {
            Column.IsPrimaryKey ? "PK" : null,
            Column.IsForeignKey ? "FK" : null,
            Column.IsUnique ? "UQ" : null,
        }.Where(value => value is not null));
        public string? Comment => Column.Comment;
        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private sealed class DescriptionCandidateDialog : Window
    {
        private readonly ListBox _list;
        public ObjectDescriptionCandidate? SelectedCandidate => _list.SelectedItem as ObjectDescriptionCandidate;

        public DescriptionCandidateDialog(
            string target, IReadOnlyList<ObjectDescriptionCandidate> candidates)
        {
            Title = $"Choose object — {target}";
            Width = 570;
            Height = 300;
            MinWidth = 420;
            MinHeight = 220;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            var panel = new DockPanel { Margin = new Thickness(8) };
            var prompt = new TextBlock
            {
                Text = "More than one PostgreSQL object matches. Choose the intended object:",
                Margin = new Thickness(0, 0, 0, 7),
            };
            DockPanel.SetDock(prompt, Dock.Top);
            panel.Children.Add(prompt);
            _list = new ListBox
            {
                ItemsSource = candidates,
                DisplayMemberPath = nameof(ObjectDescriptionCandidate.QualifiedName),
                SelectedIndex = 0,
            };
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 7, 0, 0),
            };
            var open = new Button { Content = "Describe", IsDefault = true, MinWidth = 78 };
            open.Click += (_, _) => { if (_list.SelectedItem is not null) DialogResult = true; };
            var cancel = new Button
            {
                Content = "Cancel", IsCancel = true, MinWidth = 70, Margin = new Thickness(7, 0, 0, 0),
            };
            buttons.Children.Add(open);
            buttons.Children.Add(cancel);
            DockPanel.SetDock(buttons, Dock.Bottom);
            panel.Children.Add(buttons);
            _list.MouseDoubleClick += (_, _) => DialogResult = true;
            panel.Children.Add(_list);
            Content = panel;
            Loaded += (_, _) => _list.Focus();
        }
    }
    private sealed class InputDialog : Window { public string Value => Box.Text; private readonly TextBox Box = new(); public InputDialog(string title, string prompt) { Title = title; Width = 300; Height = 130; WindowStartupLocation = WindowStartupLocation.CenterOwner; var panel = new StackPanel { Margin = new Thickness(10) }; panel.Children.Add(new TextBlock { Text = prompt }); panel.Children.Add(Box); var button = new Button { Content = "OK", IsDefault = true, Margin = new Thickness(0, 8, 0, 0) }; button.Click += (_, _) => DialogResult = true; panel.Children.Add(button); Content = panel; } }
}
