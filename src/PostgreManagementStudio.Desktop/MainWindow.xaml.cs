using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using PostgreManagementStudio.Application;
using PostgreManagementStudio.Core;

namespace PostgreManagementStudio.Desktop;

public partial class MainWindow : Window
{
    private readonly QueryTabManager _tabs;
    private readonly ObjectExplorerService _objectExplorer;
    private readonly ApplicationSettings _settings;
    private readonly DestructiveOperationGuard _destructiveOperations;
    private CancellationTokenSource? _metadataCancellation;
    private bool _activeShutdownApproved;

    public MainWindow(QueryTabManager tabs, ObjectExplorerService objectExplorer, DestructiveOperationGuard destructiveOperations, ApplicationSettings settings)
    {
        InitializeComponent();
        _tabs = tabs;
        _objectExplorer = objectExplorer;
        _destructiveOperations = destructiveOperations;
        _settings = settings;
        AddTab();
    }

    private void AddTab()
    {
        var doc = _tabs.Open(Environment.GetEnvironmentVariable("PMS_CONNECTION_STRING"), _settings.DefaultDatabase);
        doc.CommandTimeout = TimeSpan.FromSeconds(_settings.CommandTimeoutSeconds);
        doc.CancellationTimeout = TimeSpan.FromSeconds(_settings.CancellationTimeoutSeconds);
        var view = new QueryTabView(doc, _destructiveOperations, _settings);
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

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        _metadataCancellation?.Cancel();
        if (_activeShutdownApproved) return;
        foreach (TabItem tab in QueryTabs.Items)
        {
            if (tab.Tag is not QueryDocument { IsDirty: true } doc) continue;
            var result = MessageBox.Show($"Discard changes in {doc.Title}?", "Unsaved query", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes) continue;
            e.Cancel = true;
            return;
        }
        var active = _tabs.Documents.Where(document => document.IsExecuting).ToArray();
        if (active.Length == 0)
        {
            foreach (var document in _tabs.Documents) document.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _objectExplorer.DisposeAsync().AsTask().GetAwaiter().GetResult();
            return;
        }
        var confirmation = MessageBox.Show(
            $"Cancel {active.Length} active query execution(s) and close?",
            "Queries are still running",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes) { e.Cancel = true; return; }
        e.Cancel = true;
        try
        {
            await Task.WhenAll(active.Select(document => document.CancelAsync()));
            foreach (var document in _tabs.Documents) await document.DisposeAsync();
            await _objectExplorer.DisposeAsync();
            _activeShutdownApproved = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(SecretRedactor.Redact(ex.Message), "Shutdown cleanup failed", MessageBoxButton.OK, MessageBoxImage.Error);
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
            _metadataCancellation?.Cancel();
            _metadataCancellation?.Dispose();
            _metadataCancellation = new CancellationTokenSource();
            var expanded = ExpandedIdentities(ObjectExplorerTree.Items).ToHashSet();
            var selectionPath = SelectedIdentityPath();
            var root = await _objectExplorer.LoadRootAsync(
                connectionString, _settings.DefaultDatabase, refresh: true,
                cancellationToken: _metadataCancellation.Token);
            ObjectExplorerTree.ItemsSource = null;
            ObjectExplorerTree.Items.Clear();
            ObjectExplorerTree.Items.Add(ToTreeItem(root, expanded));
            ObjectExplorerTree.ToolTip = null;
            foreach (var identity in selectionPath)
                if (FindItem(ObjectExplorerTree.Items, identity) is { } selected)
                {
                    selected.IsSelected = true;
                    selected.BringIntoView();
                    break;
                }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            var message = $"Object Explorer unavailable: {SecretRedactor.Redact(ex.Message)}";
            if (ObjectExplorerTree.Items.Count == 0)
                ObjectExplorerTree.ItemsSource = new[] { message };
            else
                ObjectExplorerTree.ToolTip = message;
        }
    }

    private TreeViewItem ToTreeItem(ObjectExplorerNode node, IReadOnlySet<PostgresObjectIdentity>? expanded = null)
    {
        var item = new TreeViewItem { Header = node.Name, Tag = node };
        Populate(item, node, expanded);
        item.Expanded += TreeItem_Expanded;
        if (expanded?.Contains(node.Identity) == true) item.IsExpanded = true;
        return item;
    }

    private void Populate(
        TreeViewItem item,
        ObjectExplorerNode node,
        IReadOnlySet<PostgresObjectIdentity>? expanded = null)
    {
        item.Items.Clear();
        if (!node.IsLoaded && node.HasChildren)
        {
            item.Items.Add(new TreeViewItem { Header = "Loading…" });
            return;
        }
        foreach (var child in node.Children) item.Items.Add(ToTreeItem(child, expanded));
        if (node.Error is not null)
            item.Items.Add(new TreeViewItem { Header = node.Error.Message, IsEnabled = false });
    }

    private async void TreeItem_Expanded(object sender, RoutedEventArgs e)
    {
        if (sender is not TreeViewItem { Tag: ObjectExplorerNode node } item || node.IsLoaded) return;
        e.Handled = true;
        try
        {
            await _objectExplorer.ExpandAsync(node, cancellationToken: _metadataCancellation?.Token ?? default);
            Populate(item, node);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            item.Items.Clear();
            item.Items.Add(new TreeViewItem { Header = SecretRedactor.Redact(ex.Message), IsEnabled = false });
        }
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
}
