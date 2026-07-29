using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;
using PostgreManagementStudio.Application;

namespace PostgreManagementStudio.Desktop;

public sealed class PlanExplorerWindow : Window
{
    private readonly ExecutionPlanDocument _plan;
    private readonly DataGrid _nodes = new() { IsReadOnly = true, AutoGenerateColumns = false, CanUserSortColumns = true };
    private readonly TextBox _details = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly TextBox _raw = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly TextBox _search = new() { Width = 220 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TabControl _tabs = new();
    private IReadOnlyList<PlanExplorerNodeRow> _rows = Array.Empty<PlanExplorerNodeRow>();

    public PlanExplorerWindow(ExecutionPlanDocument plan)
    {
        _plan = plan; Title = $"Execution plan — {plan.Type}"; Width = 1050; Height = 700; MinWidth = 760; MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; Content = BuildContent();
        AutomationProperties.SetName(_nodes, "Execution plan operators"); AutomationProperties.SetName(_details, "Execution plan operator details"); AutomationProperties.SetName(_raw, "Raw execution plan");
        AutomationProperties.SetName(_search, "Search execution plan");
        _rows = ExecutionPlanExplorerService.Flatten(plan); _nodes.ItemsSource = _rows; _raw.Text = plan.RawJson;
        _nodes.SelectionChanged += (_, _) => ShowDetails(); _search.TextChanged += (_, _) => ApplySearch(); ApplySearch();
    }

    private UIElement BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(12) }; var header = new DockPanel();
        var save = new Button { Content = "Save raw plan", Width = 100 }; save.Click += (_, _) => SaveRaw(); DockPanel.SetDock(save, Dock.Right); header.Children.Add(save);
        header.Children.Add(new TextBlock { Text = $"{_plan.Type} plan · captured {_plan.CapturedAt.LocalDateTime:g} · Server values are {( _plan.Type == PlanType.Actual ? "estimated and actual" : "estimated only")}", VerticalAlignment = VerticalAlignment.Center }); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var searchPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 8) }; searchPanel.Children.Add(new TextBlock { Text = "Find operator:", VerticalAlignment = VerticalAlignment.Center }); searchPanel.Children.Add(_search); searchPanel.Children.Add(_status); DockPanel.SetDock(searchPanel, Dock.Top); root.Children.Add(searchPanel);
        _nodes.Columns.Add(new DataGridTextColumn { Header = "#", Binding = new Binding(nameof(PlanExplorerNodeRow.Number)) }); _nodes.Columns.Add(new DataGridTextColumn { Header = "Operator", Binding = new Binding("Node.NodeType") }); _nodes.Columns.Add(new DataGridTextColumn { Header = "Relation", Binding = new Binding("Node.RelationName") }); _nodes.Columns.Add(new DataGridTextColumn { Header = "Estimated rows", Binding = new Binding("Node.PlanRows") }); _nodes.Columns.Add(new DataGridTextColumn { Header = "Actual rows", Binding = new Binding("Node.ActualRows") }); _nodes.Columns.Add(new DataGridTextColumn { Header = "Cost %", Binding = new Binding(nameof(PlanExplorerNodeRow.CostPercent)) });
        var explorer = new Grid(); explorer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) }); explorer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) }); Grid.SetColumn(_nodes, 0); Grid.SetColumn(_details, 1); explorer.Children.Add(_nodes); explorer.Children.Add(_details);
        _tabs.Items.Add(new TabItem { Header = "Operators", Content = explorer }); _tabs.Items.Add(new TabItem { Header = "Raw plan", Content = _raw }); var warnings = new ListBox { ItemsSource = ExecutionPlanExplorerService.Warnings(_plan).Select(x => $"[{x.Severity}] {x.Summary} — {x.Evidence}") }; _tabs.Items.Add(new TabItem { Header = "Warnings", Content = warnings }); root.Children.Add(_tabs); return root;
    }

    private void ApplySearch()
    {
        _rows = ExecutionPlanExplorerService.Search(_plan, _search.Text).Matches; _nodes.ItemsSource = _rows; _status.Text = $"{_rows.Count:N0} operator(s) · {ExecutionPlanExplorerService.Warnings(_plan).Count:N0} warning(s)";
    }
    private void ShowDetails()
    {
        if (_nodes.SelectedItem is not PlanExplorerNodeRow row) { _details.Text = "Select an operator to inspect its values."; return; }
        var node = row.Node; _details.Text = $"Operator: {node.NodeType}\nRelation: {node.Schema}.{node.RelationName}\nIndex: {node.IndexName ?? "n/a"}\nEstimated startup cost: {node.StartupCost?.ToString("N2") ?? "unavailable"}\nEstimated total cost: {node.TotalCost?.ToString("N2") ?? "unavailable"}\nEstimated rows: {node.PlanRows?.ToString("N0") ?? "unavailable"}\nActual rows: {node.ActualRows?.ToString("N0") ?? "unavailable"}\nActual time: {node.ActualTime?.ToString("N2") ?? "unavailable"} ms\nLoops: {node.Loops?.ToString("N0") ?? "unavailable"}\nInclusive time: {row.InclusiveTime?.ToString() ?? "unavailable"}\nExclusive time: {row.ExclusiveTime?.ToString() ?? "unavailable"}";
    }
    private void SaveRaw()
    {
        var dialog = new SaveFileDialog { Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*", FileName = "execution-plan.json" }; if (dialog.ShowDialog(this) == true) File.WriteAllText(dialog.FileName, ExecutionPlanFileService.Save(_plan, includeQueryText: false));
    }
}
