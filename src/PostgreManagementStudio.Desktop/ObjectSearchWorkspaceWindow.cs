using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using PostgreManagementStudio.Application;
using PostgreManagementStudio.Core;
using PostgreManagementStudio.Postgres;

namespace PostgreManagementStudio.Desktop;

public sealed class ObjectSearchWorkspaceWindow : Window
{
    private readonly NpgsqlObjectSearchService _search;
    private readonly string _connectionString;
    private readonly string _server;
    private readonly string _database;
    private readonly TextBox _text = new() { MinWidth = 300 };
    private readonly ComboBox _type = new() { Width = 150 };
    private readonly CheckBox _definitions = new() { Content = "Search definitions" };
    private readonly CheckBox _system = new() { Content = "Include system objects" };
    private readonly Button _searchButton = new() { Content = "Search", Width = 90 };
    private readonly Button _cancelButton = new() { Content = "Cancel", Width = 90, IsEnabled = false };
    private readonly Button _clearButton = new() { Content = "Clear", Width = 70 };
    private readonly Button _copyButton = new() { Content = "Copy qualified name", Width = 140, IsEnabled = false };
    private readonly DataGrid _results = new() { IsReadOnly = true, AutoGenerateColumns = false, CanUserSortColumns = true, SelectionMode = DataGridSelectionMode.Single };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private CancellationTokenSource? _cancellation;
    private int _generation;
    private bool _closing;

    public ObjectSearchWorkspaceWindow(NpgsqlObjectSearchService search, string connectionString, string server, string database)
    {
        _search = search;
        _connectionString = connectionString;
        _server = server;
        _database = database;
        Title = "Search database objects";
        Width = 900;
        Height = 600;
        MinWidth = 720;
        MinHeight = 450;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = BuildContent();
        AutomationProperties.SetName(_text, "Object search text");
        AutomationProperties.SetName(_type, "Object type filter");
        AutomationProperties.SetName(_results, "Object search results");
        AutomationProperties.SetName(_searchButton, "Search objects");
        AutomationProperties.SetName(_cancelButton, "Cancel object search");
        AutomationProperties.SetName(_clearButton, "Clear search");
        AutomationProperties.SetName(_copyButton, "Copy qualified object name");
        _type.ItemsSource = new object[] { "All", SearchObjectType.Table, SearchObjectType.View, SearchObjectType.MaterializedView, SearchObjectType.Sequence, SearchObjectType.Index };
        _type.SelectedIndex = 0;
        _searchButton.Click += async (_, _) => await SearchAsync();
        _cancelButton.Click += (_, _) => _cancellation?.Cancel();
        _clearButton.Click += (_, _) => Clear();
        _copyButton.Click += (_, _) => CopySelected();
        _results.SelectionChanged += (_, _) => _copyButton.IsEnabled = _results.SelectedItem is ObjectSearchResult;
        _text.KeyDown += async (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) { e.Handled = true; await SearchAsync(); } };
        Closed += (_, _) => CloseWorkspace();
        _status.Text = $"Ready. Scope: {_server}:{_database}";
    }

    private UIElement BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(14) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        actions.Children.Add(_copyButton); actions.Children.Add(_clearButton); actions.Children.Add(_searchButton); actions.Children.Add(_cancelButton);
        DockPanel.SetDock(actions, Dock.Bottom);
        root.Children.Add(actions);
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "Database object search", FontSize = 18, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = $"Server: {_server}\nDatabase: {_database}", Margin = new Thickness(0, 4, 0, 10) });
        var criteria = new WrapPanel();
        criteria.Children.Add(new TextBlock { Text = "Name or wildcard:", VerticalAlignment = VerticalAlignment.Center });
        criteria.Children.Add(_text);
        criteria.Children.Add(new TextBlock { Text = "Type:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 4, 0) });
        criteria.Children.Add(_type);
        criteria.Children.Add(new Border { Child = _definitions, Margin = new Thickness(12, 0, 0, 0) });
        criteria.Children.Add(new Border { Child = _system, Margin = new Thickness(12, 0, 0, 0) });
        panel.Children.Add(criteria);
        panel.Children.Add(_status);
        _results.Columns.Add(new DataGridTextColumn { Header = "Type", Binding = new Binding(nameof(ObjectSearchResult.ObjectType)) });
        _results.Columns.Add(new DataGridTextColumn { Header = "Database", Binding = new Binding(nameof(ObjectSearchResult.Database)) });
        _results.Columns.Add(new DataGridTextColumn { Header = "Schema", Binding = new Binding(nameof(ObjectSearchResult.Schema)) });
        _results.Columns.Add(new DataGridTextColumn { Header = "Object", Binding = new Binding(nameof(ObjectSearchResult.ObjectName)) });
        _results.Columns.Add(new DataGridTextColumn { Header = "Parent", Binding = new Binding(nameof(ObjectSearchResult.ParentObject)) });
        _results.Columns.Add(new DataGridTextColumn { Header = "Match", Binding = new Binding(nameof(ObjectSearchResult.MatchType)) });
        _results.Columns.Add(new DataGridTextColumn { Header = "Preview", Binding = new Binding(nameof(ObjectSearchResult.MatchPreview)) });
        panel.Children.Add(_results);
        root.Children.Add(panel);
        return root;
    }

    private async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(_text.Text)) { _status.Text = "Enter a name or wildcard."; return; }
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        var generation = Interlocked.Increment(ref _generation);
        SetBusy(true, "Searching…");
        try
        {
            var selected = _type.SelectedItem is SearchObjectType objectType ? new HashSet<SearchObjectType> { objectType } : null;
            var batch = await _search.SearchAsync(_connectionString, new ObjectSearchOptions(_text.Text, selected, _definitions.IsChecked == true, _system.IsChecked == true), cancellation.Token);
            if (_closing || generation != _generation) return;
            _results.ItemsSource = batch.Results;
            _status.Text = batch.Results.Count == 0
                ? "No matching objects. Adjust the criteria and search again."
                : $"{batch.Results.Count:N0} result(s) in {batch.Duration.TotalMilliseconds:N0} ms" + (batch.LimitReached ? " (limit reached)" : "");
            if (batch.Warnings.Count > 0) _status.Text += " " + string.Join(" ", batch.Warnings.Select(UntrustedText.ForDisplay));
        }
        catch (OperationCanceledException) { if (!_closing && generation == _generation) _status.Text = "Search cancelled."; }
        catch (Exception ex) { if (!_closing && generation == _generation) _status.Text = DesktopErrorPresentation.Failure("Object search", ex); }
        finally { if (!_closing && generation == _generation) SetBusy(false); }
    }

    private void Clear()
    {
        _cancellation?.Cancel();
        _text.Clear();
        _results.ItemsSource = null;
        _status.Text = $"Ready. Scope: {_server}:{_database}";
    }

    private void CopySelected()
    {
        if (_results.SelectedItem is not ObjectSearchResult result) return;
        try
        {
            Clipboard.SetText($"{result.Schema}.{result.ObjectName}");
            _status.Text = "Qualified name copied.";
        }
        catch (Exception ex)
        {
            _status.Text = DesktopErrorPresentation.Failure("Copy object name", ex);
        }
    }

    private void SetBusy(bool busy, string? status = null)
    {
        _searchButton.IsEnabled = !busy;
        _cancelButton.IsEnabled = busy;
        _text.IsEnabled = !busy;
        _type.IsEnabled = !busy;
        _definitions.IsEnabled = !busy;
        _system.IsEnabled = !busy;
        if (status is not null) _status.Text = status;
    }

    private void CloseWorkspace()
    {
        if (_closing) return;
        _closing = true;
        _generation++;
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = null;
    }
}
