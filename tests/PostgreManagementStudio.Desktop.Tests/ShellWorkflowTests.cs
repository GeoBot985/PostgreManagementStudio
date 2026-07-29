using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using PostgreManagementStudio.Application;
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
        Assert.Contains(ShellCommands.NextDocument.InputGestures.Cast<InputGesture>(), x => x is KeyGesture { Key: Key.Tab, Modifiers: ModifierKeys.Control });
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
            var editor = Assert.IsType<TextBox>(view.FindName("SqlText"));
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
    public void Sprint53_TransferWorkspacesExposeImportMappingAndExportReviewSurfaces()
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
            Assert.Contains(LogicalDescendants(import).OfType<DataGrid>(), grid => grid.Columns.Count >= 4);
            Assert.Contains(LogicalDescendants(import).OfType<Button>(), button => button.Content?.ToString() == "Validate plan");
            Assert.Contains(LogicalDescendants(import).OfType<Button>(), button => button.Content?.ToString() == "Cancel");
            Assert.Equal("Export query result", export.Title);
            Assert.Contains(LogicalDescendants(export).OfType<ComboBox>(), combo => combo.Items.Count >= 4);
            Assert.Contains(LogicalDescendants(export).OfType<CheckBox>(), check => check.Content?.ToString() == "Include headers");
            Assert.Contains(LogicalDescendants(export).OfType<DataGrid>(), grid => grid.Columns.Count >= 5);
            import.Close();
            export.Close();
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

    private static IEnumerable<DependencyObject> LogicalDescendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var nested in LogicalDescendants(child)) yield return nested;
        }
    }
}
