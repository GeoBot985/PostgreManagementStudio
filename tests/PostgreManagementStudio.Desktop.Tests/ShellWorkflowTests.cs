using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using PostgreManagementStudio.Application;
using PostgreManagementStudio.Core;
using PostgreManagementStudio.Postgres;
using PostgreManagementStudio.Results;

namespace PostgreManagementStudio.Desktop.Tests;

public sealed class ShellWorkflowTests
{
    [Theory]
    [InlineData(ShellCommandId.Execute, false)]
    [InlineData(ShellCommandId.Cancel, false)]
    [InlineData(ShellCommandId.ResultAction, false)]
    [InlineData(ShellCommandId.ChangeConnection, true)]
    public void DisconnectedDocument_ExposesAccurateCommandState(ShellCommandId command, bool expected)
    {
        var state = new ShellCommandState(HasDocument: true, IsConnected: false, IsExecuting: false, HasResults: false, IsDirty: false);
        Assert.Equal(expected, state.CanExecute(command));
    }

    [Fact]
    public void ExecutingDocument_OnlyEnablesCancellationAmongExecutionCommands()
    {
        var state = new ShellCommandState(true, true, true, false, true);
        Assert.True(state.CanExecute(ShellCommandId.Cancel));
        Assert.False(state.CanExecute(ShellCommandId.Execute));
        Assert.False(state.CanExecute(ShellCommandId.ChangeConnection));
        Assert.False(state.CanExecute(ShellCommandId.Save));
    }

    [Fact]
    public void ResultsCommands_RequireAnActualResultSet()
    {
        Assert.False(new ShellCommandState(true, true, false, false, false).CanExecute(ShellCommandId.ResultAction));
        Assert.True(new ShellCommandState(true, true, false, true, false).CanExecute(ShellCommandId.ResultAction));
    }

    [Fact]
    public void DesktopErrorPresentation_RedactsSecretsAndHandlesCancellation()
    {
        var failure = DesktopErrorPresentation.Failure(
            "Connection", new InvalidOperationException("password=secret; host=localhost"));

        Assert.Contains("Connection failed", failure);
        Assert.DoesNotContain("secret", failure, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Connection cancelled.",
            DesktopErrorPresentation.Failure("Connection", new OperationCanceledException()));
    }

    [Fact]
    public void KeyboardBaseline_UsesTheSharedRoutedCommands()
    {
        Assert.Contains(ShellCommands.NewQuery.InputGestures.Cast<InputGesture>(), x => x is KeyGesture { Key: Key.N, Modifiers: ModifierKeys.Control });
        Assert.Contains(ShellCommands.CloseDocument.InputGestures.Cast<InputGesture>(), x => x is KeyGesture { Key: Key.W, Modifiers: ModifierKeys.Control });
        Assert.Contains(ShellCommands.Execute.InputGestures.Cast<InputGesture>(), x => x is KeyGesture { Key: Key.F5 });
        Assert.Contains(ShellCommands.Execute.InputGestures.Cast<InputGesture>(), x => x is KeyGesture { Key: Key.Enter, Modifiers: ModifierKeys.Control });
        Assert.Contains(ShellCommands.Cancel.InputGestures.Cast<InputGesture>(), x => x is KeyGesture { Key: Key.Escape });
        Assert.Contains(ShellCommands.DescribeObject.InputGestures.Cast<InputGesture>(),
            x => x is KeyGesture { Key: Key.F1, Modifiers: ModifierKeys.Alt });
        Assert.Contains(ShellCommands.NextDocument.InputGestures.Cast<InputGesture>(), x => x is KeyGesture { Key: Key.Tab, Modifiers: ModifierKeys.Control });
    }

    [Fact]
    [Trait("Category", "UiIntegration")]
    public void DescribeWorkflowIsReachableFromMenuEditorAndPersistentOutputTab()
    {
        RunSta((window, _) =>
        {
            window.Show();
            window.UpdateLayout();
            var sources = LogicalDescendants(window).OfType<ICommandSource>()
                .Where(source => ReferenceEquals(source.Command, ShellCommands.DescribeObject))
                .ToArray();
            Assert.NotEmpty(sources);
            var editor = LogicalDescendants(window).OfType<SqlEditorControl>()
                .Single(box => AutomationProperties.GetName(box) == "SQL editor");
            Assert.Contains(editor.ContextMenu!.Items.OfType<MenuItem>(),
                item => ReferenceEquals(item.Command, ShellCommands.DescribeObject));
            var outputTabs = LogicalDescendants(window).OfType<TabControl>()
                .Single(control => AutomationProperties.GetName(control) == "Query output");
            Assert.Contains(outputTabs.Items.OfType<TabItem>(),
                tab => Equals(tab.Header, "Object Description"));
            Assert.True(ShellCommands.DescribeObject.CanExecute(null, window));
        });
    }

    [Fact]
    [Trait("Category", "UiIntegration")]
    [Trait("Priority", "P0")]
    public void Sprint62_ComposedDescriptionReplacesSimpleWildcardAsOneUndoableEdit()
    {
        RunSta((window, _) =>
        {
            window.Show();
            window.UpdateLayout();
            var view = LogicalDescendants(window).OfType<QueryTabView>().Single();
            var editor = LogicalDescendants(view).OfType<SqlEditorControl>()
                .Single(box => AutomationProperties.GetName(box) == "SQL editor");
            const string original = "SELECT * FROM public.orders;";
            editor.Text = original;
            editor.CaretIndex = original.IndexOf("orders", StringComparison.Ordinal);
            var reference = new EditorObjectResolver().Resolve(
                editor.Text, editor.CaretIndex, 0, 0)!;
            Assert.Null(reference.RelationAlias);
            var identity = new PostgresObjectIdentity
            {
                ConnectionProfileId = "test",
                ConfigurationIdentity = "config",
                ServerFingerprint = "server",
                DatabaseOid = 1,
                ObjectOid = 2,
                ObjectClass = PostgresObjectClass.Table,
                NameSnapshot = "orders",
            };
            var candidate = new ObjectDescriptionCandidate(
                identity, "\"public\".\"orders\"", "Table", "owner", null, false, true);
            var columns = new[]
            {
                new ObjectDescriptionColumn(1, "order_id", "bigint", false, null, "", null,
                    null, true, true, false, null, null),
                new ObjectDescriptionColumn(2, "status", "text", false, null, "", null,
                    null, false, false, false, null, null),
            };
            view.BindDescription(reference, new(candidate, "Permanent", null, null, null,
                null, null, columns, "", null));

            view.ReplaceDescriptionWildcard();

            Assert.Equal(
                "SELECT \n    order_id,\n    status FROM public.orders;",
                editor.Text);
            editor.Undo();
            Assert.Equal(original, editor.Text);
            editor.Redo();
            Assert.Equal(
                "SELECT \n    order_id,\n    status FROM public.orders;",
                editor.Text);

            view.BindDescription(reference, new(candidate, "Permanent", null, null, null,
                null, null, columns, "", null));
            editor.AppendText(" ");
            var changed = editor.Text;
            Assert.Throws<InvalidOperationException>(() => view.ReplaceDescriptionWildcard());
            Assert.Equal(changed, editor.Text);
            view.Document.MarkDirty(false);
        });
    }

    [Fact]
    [Trait("Category", "UiIntegration")]
    [Trait("Priority", "P0")]
    public void Sprint62_ShellStateIgnoresDisposedResultDuringSessionHandoff()
    {
        RunSta((window, _) =>
        {
            var view = LogicalDescendants(window).OfType<QueryTabView>().Single();
            typeof(QueryTabView).GetField("_session", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(view, new DisposedResultSession());
            typeof(QueryDocument).GetProperty(nameof(QueryDocument.LastExecutionContext))!
                .SetValue(view.Document, new QueryExecutionContextSnapshot(
                    Guid.NewGuid(), view.Document.TabId, "test", "localhost:5432",
                    "postgres", "tester", "Prefer", QueryTransactionMode.Implicit,
                    "select 1", DateTimeOffset.UtcNow));

            Assert.False(view.HasResults);
            Assert.Equal(0, view.RowsReceived);
            Assert.Equal(0, view.RowsAffected);
            Assert.Null(view.ExecutionElapsed);
            var update = typeof(MainWindow).GetMethod(
                "UpdateShellState", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var failure = Record.Exception(() => update.Invoke(window, null));
            Assert.Null(failure);
        });
    }

    [Fact]
    [Trait("Category", "UiIntegration")]
    public void TraditionalShell_IsReachableAtSupportedWindowSizes()
    {
        RunSta((window, provider) =>
        {
            window.Width = 1024;
            window.Height = 768;
            window.Show();
            window.UpdateLayout();

            var menu = Assert.IsType<Menu>(window.FindName("MainMenu"));
            var headings = menu.Items.OfType<MenuItem>().Select(x => x.Header?.ToString()).ToArray();
            Assert.Equal(new[] { "_File", "_Edit", "_View", "_Query", "_Database", "_Tools", "_Window", "_Help" }, headings);
            Assert.True(window.MinWidth <= 1024);
            Assert.True(window.MinHeight <= 768);
            Assert.IsType<ToolBarTray>(window.FindName("ShellToolbars"));
            Assert.IsType<StatusBar>(window.FindName("ShellStatusBar"));
            Assert.IsType<TreeView>(window.FindName("ObjectExplorerTree"));
            Assert.IsType<TabControl>(window.FindName("QueryTabs"));

            window.Width = 1600;
            window.Height = 900;
            window.UpdateLayout();
            Assert.True(window.ActualWidth >= 1024);
            Assert.DoesNotContain("Password=", LogicalDescendants(window).OfType<TextBlock>().Select(x => x.Text));
        });
    }

    [Fact]
    [Trait("Category", "UiIntegration")]
    public void MenuToolbarAndEditor_UseTheSameExecuteCommand()
    {
        RunSta((window, _) =>
        {
            window.Show();
            window.UpdateLayout();
            var commandSources = LogicalDescendants(window).OfType<ICommandSource>()
                .Where(x => ReferenceEquals(x.Command, ShellCommands.Execute))
                .ToArray();
            Assert.True(commandSources.Length >= 2, "Execute must be available through both menu and toolbar/editor surfaces.");
            Assert.All(commandSources, source => Assert.Same(ShellCommands.Execute, source.Command));
            var developmentFallbackConfigured =
                !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PMS_CONNECTION_STRING"));
            if (developmentFallbackConfigured)
                PumpDispatcherUntil(
                    () => ShellCommands.Execute.CanExecute(null, window),
                    TimeSpan.FromSeconds(5));
            Assert.Equal(developmentFallbackConfigured, ShellCommands.Execute.CanExecute(null, window));
            Assert.False(ShellCommands.CopyResults.CanExecute(null, window));
        });
    }

    [Fact]
    [Trait("Category", "UiIntegration")]
    public void QueryTabs_HaveMeaningfulTitlesAndCanOpenMultipleDocuments()
    {
        RunSta((window, provider) =>
        {
            window.Show();
            Assert.Equal("SQLQuery1.sql", provider.GetRequiredService<QueryTabManager>().Documents.Single().Title);
            ShellCommands.NewQuery.Execute(null, window);
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
            var documents = provider.GetRequiredService<QueryTabManager>().Documents;
            Assert.Equal(2, documents.Count);
            Assert.Equal("SQLQuery2.sql", documents[1].Title);
        });
    }

    [Fact]
    [Trait("Category", "UiIntegration")]
    [Trait("Priority", "P0")]
    public void RecoveredWorkspace_RestoresUnsavedSqlWithoutConnectionOrExecution()
    {
        var snapshot = new RecoverySnapshot(
            Guid.NewGuid(),
            "Recovered Query.sql",
            null,
            "SELECT 'recovered';",
            DateTimeOffset.UtcNow,
            EncodingKind.Utf8,
            "recovery_database",
            8);

        RunSta((window, provider) =>
        {
            window.Show();
            PumpDispatcherUntil(
                () => provider.GetRequiredService<QueryTabManager>().Documents.Single().SqlText == snapshot.Text,
                TimeSpan.FromSeconds(5));

            var document = provider.GetRequiredService<QueryTabManager>().Documents.Single();
            var tabs = Assert.IsType<TabControl>(window.FindName("QueryTabs"));
            var view = Assert.IsType<QueryTabView>(Assert.IsType<TabItem>(tabs.SelectedItem).Content);
            var editor = Assert.IsType<SqlEditorControl>(view.FindName("SqlText"));
            Assert.Equal(snapshot.Text, editor.Text);
            Assert.Equal(snapshot.CaretOffset, editor.CaretIndex);
            Assert.Equal(snapshot.Database, document.Database);
            Assert.True(document.IsDirty);
            Assert.Empty(document.ConnectionString);
            Assert.Empty(document.ConnectionProfileId);
            Assert.False(ShellCommands.Execute.CanExecute(null, window));

            document.MarkDirty(false);
            provider.GetRequiredService<RecoverySnapshotService>().Remove(snapshot.Id);
        }, provider =>
        {
            provider.GetRequiredService<RecoverySnapshotService>()
                .WriteAsync(snapshot).GetAwaiter().GetResult();
        });
    }

    [Fact]
    [Trait("Category", "UiIntegration")]
    public void StartupAndCleanShutdownStayBoundedAndStopTheOwnedTimer()
    {
        RunSta((window, _) =>
        {
            var stopwatch = Stopwatch.StartNew();
            window.Show();
            window.UpdateLayout();
            stopwatch.Stop();
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5),
                $"Main window became interactive in {stopwatch.Elapsed}.");

            window.Close();
            PumpDispatcherUntil(() => !window.IsVisible, TimeSpan.FromSeconds(6));
            var timer = Assert.IsType<DispatcherTimer>(
                typeof(MainWindow).GetField("_statusTimer",
                    BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window));
            Assert.False(timer.IsEnabled);
        });
    }

    [Fact]
    public void SchemaCompare_IsNowReachableThroughTheReleaseCommandSurface()
    {
        Assert.Contains(typeof(ShellCommands).GetProperties(), property =>
            property.Name == nameof(ShellCommands.SchemaCompare));
    }

    [Fact]
    public void Sprint50_CommandSurfaceUsesSharedRestoreAndSearchRoutes()
    {
        Assert.Contains(typeof(ShellCommands).GetProperties(), property => property.Name == nameof(ShellCommands.Restore));
        Assert.Contains(typeof(ShellCommands).GetProperties(), property => property.Name == nameof(ShellCommands.SearchObjects));
        Assert.Contains(ShellCommands.SearchObjects.InputGestures.Cast<InputGesture>(), gesture =>
            gesture is KeyGesture { Key: Key.F, Modifiers: ModifierKeys.Control | ModifierKeys.Shift });
        Assert.DoesNotContain(typeof(ShellCommands).GetProperties(), property => property.Name == "ActivityMonitor");
    }

    [Fact]
    [Trait("Category", "UiIntegration")]
    public void Sprint50_WorkspacesExposeDurableControlsAndSafeTargeting()
    {
        RunSta((window, provider) =>
        {
            var target = new DatabaseConnection("localhost", 5432, "postgres", "postgres");
            var restore = new RestoreWorkspaceWindow(
                provider.GetRequiredService<BackupRestoreOperationService>(),
                provider.GetRequiredService<PostgreSqlToolDiscoveryService>(),
                provider.GetRequiredService<BackupInspectionService>(),
                provider.GetRequiredService<DestructiveOperationGuard>(), target,
                "Host=localhost;Database=postgres;Username=postgres", "test-profile", "localhost:5432", "Test");
            var search = new ObjectSearchWorkspaceWindow(
                provider.GetRequiredService<NpgsqlObjectSearchService>(),
                "Host=localhost;Database=postgres;Username=postgres", "localhost:5432", "postgres");

            Assert.Contains(LogicalDescendants(restore).OfType<TextBlock>(), text => text.Text == "Restore workspace");
            Assert.Contains(LogicalDescendants(restore).OfType<CheckBox>(), box => box.Content?.ToString() == "Clean existing objects");
            Assert.Contains(LogicalDescendants(search).OfType<TextBlock>(), text => text.Text == "Database object search");
            Assert.Contains(LogicalDescendants(search).OfType<DataGrid>(), grid => grid.Columns.Count >= 6);
            Assert.Equal("Restore PostgreSQL database", restore.Title);
            Assert.Equal("Search database objects", search.Title);
            restore.Close();
            search.Close();
        });
    }

    [Fact]
    public void Sprint52_CommandSurfaceUsesSharedIndexAndSchemaRoutes()
    {
        Assert.Contains(ShellCommands.IndexManagement.InputGestures.Cast<InputGesture>(), gesture =>
            gesture is KeyGesture { Key: Key.I, Modifiers: ModifierKeys.Control | ModifierKeys.Shift });
        Assert.Contains(ShellCommands.SchemaCompare.InputGestures.Cast<InputGesture>(), gesture =>
            gesture is KeyGesture { Key: Key.C, Modifiers: ModifierKeys.Control | ModifierKeys.Shift });
    }

    [Fact]
    [Trait("Category", "UiIntegration")]
    public void Sprint52_WorkspacesExposeExplicitTargetsAndSafePreviewControls()
    {
        RunSta((window, provider) =>
        {
            var target = new DatabaseConnection("localhost", 5432, "postgres", "postgres");
            var indexes = new IndexWorkspaceWindow(provider.GetRequiredService<NpgsqlIndexAnalysisService>(), new NpgsqlMaintenanceService(),
                provider.GetRequiredService<DestructiveOperationGuard>(), provider.GetRequiredService<PostgresVersionService>(),
                "Host=localhost;Database=postgres;Username=postgres", "localhost:5432", "postgres");
            var compare = new SchemaComparisonWorkspaceWindow(provider.GetRequiredService<NpgsqlSchemaModelExtractor>(),
                "Host=localhost;Database=postgres;Username=postgres", target);
            Assert.Equal("Index management", indexes.Title);
            Assert.Contains(LogicalDescendants(indexes).OfType<DataGrid>(), grid => grid.Columns.Count >= 6);
            Assert.Equal("Schema comparison and synchronisation preview", compare.Title);
            Assert.Contains(LogicalDescendants(compare).OfType<PasswordBox>(), _ => true);
            Assert.Contains(LogicalDescendants(compare).OfType<DataGrid>(), grid => grid.Columns.Count >= 6);
            indexes.Close();
            compare.Close();
        });
    }

    [Fact]
    [Trait("Category", "UiIntegration")]
    public void Sprint51_MaintenanceAndPlanWorkspacesExposeStructuredControls()
    {
        RunSta((window, provider) =>
        {
            var target = new DatabaseConnection("localhost", 5432, "postgres", "postgres");
            var maintenance = new MaintenanceWorkspaceWindow(
                new NpgsqlMaintenanceService(), provider.GetRequiredService<PostgresVersionService>(),
                provider.GetRequiredService<DestructiveOperationGuard>(),
                "Host=localhost;Database=postgres;Username=postgres", target, "Test");
            var plan = ExecutionPlanParser.Parse(
                "SELECT 1",
                "[{\"Plan\":{\"Node Type\":\"Seq Scan\",\"Relation Name\":\"orders\",\"Plan Rows\":10,\"Total Cost\":4,\"Plans\":[]}}]",
                PlanType.Estimated);
            var explorer = new PlanExplorerWindow(plan);
            Assert.Equal("PostgreSQL maintenance", maintenance.Title);
            Assert.Contains(LogicalDescendants(maintenance).OfType<TextBlock>(), text => text.Text == "Database maintenance");
            Assert.Contains(LogicalDescendants(maintenance).OfType<TextBox>(), box => box.IsReadOnly && box.AcceptsReturn);
            Assert.Equal("Execution plan — Estimated", explorer.Title);
            Assert.Contains(LogicalDescendants(explorer).OfType<DataGrid>(), grid => grid.Columns.Count >= 5);
            Assert.Contains(LogicalDescendants(explorer).OfType<TabControl>(), _ => true);
            maintenance.Close();
            explorer.Close();
        });
    }

    [Fact]
    [Trait("Category", "UiIntegration")]
    public void Sprint60_TransferWorkspacesUseTraditionalAccessibleWizardNavigation()
    {
        RunSta((window, provider) =>
        {
            var history = provider.GetRequiredService<TransferHistoryService>();
            var import = new DataTransferWorkspaceWindow(DataTransferWorkspaceMode.Import, history,
                "Host=localhost;Database=postgres;Username=postgres",
                provider.GetRequiredService<NpgsqlDataTransferService>());
            var export = new DataTransferWorkspaceWindow(DataTransferWorkspaceMode.Export, history,
                "Host=localhost;Database=postgres;Username=postgres",
                exportService: provider.GetRequiredService<IResultExportService>());

            Assert.Equal("Import data into PostgreSQL", import.Title);
            Assert.Contains(LogicalDescendants(import).OfType<ListBox>(),
                list => AutomationProperties.GetName(list) == "Wizard steps" && list.Items.Count == 9);
            Assert.Contains(LogicalDescendants(import).OfType<Button>(), button => button.Content?.ToString() == "_Back");
            Assert.Contains(LogicalDescendants(import).OfType<Button>(), button => button.Content?.ToString() == "_Next");
            Assert.Contains(LogicalDescendants(import).OfType<Button>(), button => button.Content?.ToString() == "_Finish");
            Assert.Contains(LogicalDescendants(import).OfType<Button>(), button => button.Content?.ToString() == "Cancel");
            Assert.Equal("Export query result", export.Title);
            Assert.Contains(LogicalDescendants(export).OfType<ListBox>(),
                list => AutomationProperties.GetName(list) == "Wizard steps" && list.Items.Count == 8);
            Assert.Contains(LogicalDescendants(export).OfType<TextBlock>(),
                text => text.Text.Contains("streams data through the active connection"));
            Assert.Contains(LogicalDescendants(import).OfType<TextBlock>(),
                text => AutomationProperties.GetName(text) == "Validation summary");
            import.Close();
            export.Close();
        });
    }

    [Fact]
    [Trait("Category", "UiIntegration")]
    public void Sprint54_MonitoringWorkspaceExposesActivityBlockingLocksAndPrivacyControls()
    {
        RunSta((window, provider) =>
        {
            var monitoring = new MonitoringWorkspaceWindow(
                provider.GetRequiredService<NpgsqlActivityService>(),
                "Host=localhost;Database=postgres;Username=postgres",
                "localhost:5432/postgres");

            Assert.Equal("Performance dashboard — localhost:5432/postgres", monitoring.Title);
            Assert.Contains(LogicalDescendants(monitoring).OfType<TabControl>(), tabs => tabs.Items.Count >= 4);
            Assert.Contains(LogicalDescendants(monitoring).OfType<DataGrid>(), grid => grid.Columns.Count >= 9);
            Assert.Contains(LogicalDescendants(monitoring).OfType<DataGrid>(), grid => grid.Columns.Count >= 7);
            Assert.Contains(LogicalDescendants(monitoring).OfType<CheckBox>(), check => check.Content?.ToString() == "Include bounded query previews");
            Assert.Contains(LogicalDescendants(monitoring).OfType<Button>(), button => button.Content?.ToString() == "Save snapshot");
            Assert.Contains(ShellCommands.PerformanceDashboard.InputGestures.Cast<InputGesture>(), gesture =>
                gesture is KeyGesture { Key: Key.P, Modifiers: ModifierKeys.Control | ModifierKeys.Shift });
            Assert.Contains(ShellCommands.BlockingDiagnostics.InputGestures.Cast<InputGesture>(), gesture =>
                gesture is KeyGesture { Key: Key.B, Modifiers: ModifierKeys.Control | ModifierKeys.Shift });
            monitoring.Close();
        });
    }

    [Fact]
    public void Sprint55_CanonicalShellShortcutsHaveNoCollisions()
    {
        var gestures = typeof(ShellCommands).GetProperties()
            .SelectMany(property => (property.GetValue(null) as RoutedUICommand)?.InputGestures.OfType<KeyGesture>()
                .Select(gesture => (Command: property.Name, gesture.Key, gesture.Modifiers)) ?? Enumerable.Empty<(string Command, Key Key, ModifierKeys Modifiers)>())
            .GroupBy(x => (x.Key, x.Modifiers))
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key.Key}+{group.Key.Modifiers}: {string.Join(", ", group.Select(x => x.Command))}")
            .ToArray();

        Assert.Empty(gestures);
    }

    [Fact]
    [Trait("Category", "UiIntegration")]
    public void Sprint58_ObjectExplorerContextMenuIsTraditionalTypeAwareAndDisconnectedSafe()
    {
        RunSta((window, _) =>
        {
            var identity = new PostgresObjectIdentity
            {
                ConnectionProfileId = "test", ConfigurationIdentity = "config", ServerFingerprint = "server",
                DatabaseOid = 1, ObjectOid = 2, ObjectClass = PostgresObjectClass.Table, NameSnapshot = "orders",
            };
            var constructor = typeof(ObjectExplorerNode).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single();
            var node = (ObjectExplorerNode)constructor.Invoke([
                ObjectExplorerNodeKind.Table, "orders", "\"public\".\"orders\"", identity,
                true, false, null, null
            ]);
            var tree = Assert.IsType<TreeView>(window.FindName("ObjectExplorerTree"));
            var prior = new TreeViewItem { Header = "prior", Tag = node, IsSelected = true };
            tree.Items.Add(prior);
            var item = new TreeViewItem { Header = "orders", Tag = node };
            tree.Items.Add(item);
            window.Show();
            window.UpdateLayout();
            var rightClick = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Right)
            {
                RoutedEvent = UIElement.PreviewMouseRightButtonDownEvent,
                Source = item,
            };
            typeof(MainWindow).GetMethod("ObjectExplorerTree_PreviewMouseRightButtonDown",
                    BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(window, [tree, rightClick]);
            Assert.True(item.IsSelected);
            Assert.False(prior.IsSelected);
            typeof(MainWindow).GetMethod("BuildObjectExplorerContextMenu", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(window, null);
            var menu = Assert.IsType<ContextMenu>(tree.ContextMenu);
            Assert.Contains(menu.Items.OfType<MenuItem>(), x => Equals(x.Header, "Script Object as"));
            Assert.Contains(menu.Items.OfType<MenuItem>(), x => x.Header?.ToString()?.StartsWith("Select Top") == true);
            Assert.Contains(menu.Items.OfType<MenuItem>(), x => Equals(x.Header, "Tasks"));
            var tasks = menu.Items.OfType<MenuItem>().Single(x => Equals(x.Header, "Tasks"));
            Assert.Contains(tasks.Items.OfType<MenuItem>(), x => Equals(x.Header, "Export Data…"));
            Assert.Contains(menu.Items.OfType<MenuItem>(), x => Equals(x.Header, "Copy Qualified Name"));
            Assert.False(menu.Items.OfType<MenuItem>().Single(x => Equals(x.Header, "Script Object as")).IsEnabled);
        });
    }

    [Fact]
    [Trait("Category", "UiIntegration")]
    public void Sprint58_GeneratedTabPreservesSelectedDatabaseAndUnsavedScript()
    {
        RunSta((window, provider) =>
        {
            var parse = typeof(MainWindow).GetMethod("ParseConnection",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            var connection = Assert.IsType<ShellConnectionInfo>(parse.Invoke(window,
            [
                "Host=localhost;Port=5432;Database=profile_database;Username=test;Password=test",
                true,
            ]));
            typeof(MainWindow).GetMethod("AddTab", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(window, [null, "Create public.orders", "CREATE TABLE public.orders(id integer);",
                    connection, "selected_database"]);

            var document = provider.GetRequiredService<QueryTabManager>().Documents.Last();
            Assert.Equal("selected_database", document.Database);
            Assert.Equal("CREATE TABLE public.orders(id integer);", document.SqlText);
            Assert.True(document.IsDirty);
            document.MarkDirty(false);
        });
    }

    private static void RunSta(
        Action<MainWindow, ServiceProvider> test,
        Action<ServiceProvider>? arrange = null)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            using var provider = ProductionServices.Build(Path.Combine(Path.GetTempPath(), $"pms-shell40-{Guid.NewGuid():N}.json"));
            MainWindow? window = null;
            try
            {
                arrange?.Invoke(provider);
                window = provider.GetRequiredService<MainWindow>();
                test(window, provider);
            }
            catch (Exception ex) { failure = ex; }
            finally
            {
                window?.Close();
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "WPF shell test timed out.");
        Assert.Null(failure);
    }

    private static void PumpDispatcherUntil(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
            Thread.Sleep(10);
        }
    }

    private sealed class DisposedResultSession : IResultSession
    {
        private static ObjectDisposedResultStoreException Disposed() =>
            new("Synthetic disposed result session.");

        public Guid Id { get; } = Guid.NewGuid();
        public ResultSessionStatus Status => ResultSessionStatus.Disposed;
        public IReadOnlyList<IResultSetStore> ResultSets => throw Disposed();
        public IReadOnlyList<DatabaseNotice> Notices => throw Disposed();
        public DatabaseError? Error => throw Disposed();
        public TimeSpan? Elapsed => throw Disposed();
        public long EstimatedMemoryBytes => throw Disposed();
        public long ReceivedRowCount => throw Disposed();
        public long RetainedRowCount => throw Disposed();
        public long RowsAffected => throw Disposed();
        public bool WasTruncated => throw Disposed();
        public ResultTruncationReason? TruncationReason => throw Disposed();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static IEnumerable<DependencyObject> LogicalDescendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var nested in LogicalDescendants(child)) yield return nested;
        }
    }
}
