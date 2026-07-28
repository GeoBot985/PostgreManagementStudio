using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using PostgreManagementStudio.Application;
using PostgreManagementStudio.Postgres;

namespace PostgreManagementStudio.Desktop;

public partial class MainWindow : Window
{
    private readonly QueryTabManager _tabs;
    public MainWindow() { InitializeComponent(); _tabs = new QueryTabManager(new ResultExecutionService(new NpgsqlQueryExecutor())); AddTab(); }
    private void AddTab() { var doc = _tabs.Open(Environment.GetEnvironmentVariable("PMS_CONNECTION_STRING")); var view = new QueryTabView(doc); var tab = new TabItem { Header = doc.Title, Content = view, Tag = doc }; view.DirtyChanged += (_, _) => tab.Header = doc.Title + (doc.IsDirty ? "*" : string.Empty); QueryTabs.Items.Add(tab); QueryTabs.SelectedItem = tab; }
    private void NewQuery_Click(object sender, RoutedEventArgs e) => AddTab();
    private void QueryTabs_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (QueryTabs.SelectedItem is TabItem tab && tab.Tag is QueryDocument doc) _tabs.Activate(doc); }
    private void Window_Closing(object? sender, CancelEventArgs e) { foreach (TabItem tab in QueryTabs.Items) if (tab.Tag is QueryDocument doc && doc.IsDirty) { var result = MessageBox.Show($"Discard changes in {doc.Title}?", "Unsaved query", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning); if (result != MessageBoxResult.Yes) { e.Cancel = true; return; } } }
}
