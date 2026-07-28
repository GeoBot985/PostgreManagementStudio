using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using PostgreManagementStudio.Application;

namespace PostgreManagementStudio.Desktop;

public partial class MainWindow : Window
{
    private readonly QueryTabManager _tabs;
    private readonly ObjectExplorerService _objectExplorer;
    private readonly ApplicationSettings _settings = new();
    private readonly DestructiveOperationGuard _destructiveOperations;

    public MainWindow(QueryTabManager tabs, ObjectExplorerService objectExplorer, DestructiveOperationGuard destructiveOperations)
    {
        InitializeComponent();
        _tabs = tabs;
        _objectExplorer = objectExplorer;
        _destructiveOperations = destructiveOperations;
        AddTab();
    }

    private void AddTab()
    {
        var doc = _tabs.Open(Environment.GetEnvironmentVariable("PMS_CONNECTION_STRING"), _settings.DefaultDatabase);
        var view = new QueryTabView(doc, _destructiveOperations);
        var tab = new TabItem { Header = doc.Title, Content = view, Tag = doc };
        view.DirtyChanged += (_, _) => tab.Header = doc.Title + (doc.IsDirty ? "*" : string.Empty);
        QueryTabs.Items.Add(tab);
        QueryTabs.SelectedItem = tab;
    }

    private void NewQuery_Click(object sender, RoutedEventArgs e) => AddTab();

    private void QueryTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (QueryTabs.SelectedItem is TabItem tab && tab.Tag is QueryDocument doc) _tabs.Activate(doc);
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        foreach (TabItem tab in QueryTabs.Items)
        {
            if (tab.Tag is not QueryDocument { IsDirty: true } doc) continue;
            var result = MessageBox.Show($"Discard changes in {doc.Title}?", "Unsaved query", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes) continue;
            e.Cancel = true;
            return;
        }
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e) => await RefreshObjectExplorerAsync();
    private async void RefreshObjectExplorer_Click(object sender, RoutedEventArgs e) => await RefreshObjectExplorerAsync();

    private async Task RefreshObjectExplorerAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("PMS_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            ObjectExplorerTree.ItemsSource = new[] { "Configure PMS_CONNECTION_STRING to load PostgreSQL objects." };
            return;
        }

        try
        {
            var root = await _objectExplorer.LoadDatabaseAsync(connectionString, _settings.DefaultDatabase);
            ObjectExplorerTree.ItemsSource = null;
            ObjectExplorerTree.Items.Clear();
            ObjectExplorerTree.Items.Add(ToTreeItem(root));
        }
        catch (Exception ex)
        {
            ObjectExplorerTree.Items.Clear();
            ObjectExplorerTree.ItemsSource = new[] { $"Object Explorer unavailable: {ex.Message}" };
        }
    }

    private static TreeViewItem ToTreeItem(ObjectExplorerNode node)
    {
        var item = new TreeViewItem { Header = node.Name, Tag = node };
        foreach (var child in node.Children) item.Items.Add(ToTreeItem(child));
        return item;
    }
}
