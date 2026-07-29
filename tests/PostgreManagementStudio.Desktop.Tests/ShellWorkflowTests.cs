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
            Assert.Equal(new[] { "_File", "_Edit", "_View", "_Query", "_Tools", "_Window", "_Help" }, headings);
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
    public void SchemaCompare_IsAbsentFromTheReleaseCommandSurface()
    {
        Assert.DoesNotContain(typeof(ShellCommands).GetProperties(), property =>
            property.Name.Contains("Schema", StringComparison.OrdinalIgnoreCase));
    }

    private static void RunSta(Action<MainWindow, ServiceProvider> test)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            using var provider = ProductionServices.Build(Path.Combine(Path.GetTempPath(), $"pms-shell40-{Guid.NewGuid():N}.json"));
            MainWindow? window = null;
            try
            {
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
