using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;
using Npgsql;
using PostgreManagementStudio.Application;
using PostgreManagementStudio.Postgres;

namespace PostgreManagementStudio.Desktop;

public sealed class SchemaComparisonWorkspaceWindow : Window
{
    private readonly NpgsqlSchemaModelExtractor _extractor;
    private readonly string _sourceConnectionString;
    private readonly DatabaseConnection _source;
    private readonly TextBox _targetHost = new() { Width = 140, Text = "localhost" };
    private readonly TextBox _targetPort = new() { Width = 60, Text = "5432" };
    private readonly TextBox _targetDatabase = new() { Width = 120, Text = "postgres" };
    private readonly TextBox _targetUser = new() { Width = 120, Text = "postgres" };
    private readonly PasswordBox _targetPassword = new() { Width = 120 };
    private readonly TextBox _scope = new() { Width = 150 };
    private readonly CheckBox _includeDestructive = new() { Content = "Include destructive preview" };
    private readonly Button _compare = new() { Content = "Compare", Width = 90 };
    private readonly Button _cancel = new() { Content = "Cancel", Width = 80, IsEnabled = false };
    private readonly Button _copy = new() { Content = "Copy script", Width = 90, IsEnabled = false };
    private readonly Button _save = new() { Content = "Save script", Width = 90, IsEnabled = false };
    private readonly DataGrid _differences = new() { IsReadOnly = true, AutoGenerateColumns = false, CanUserSortColumns = true, SelectionMode = DataGridSelectionMode.Single };
    private readonly TextBox _details = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly TextBox _script = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TabControl _tabs = new();
    private CancellationTokenSource? _cancellation;
    private SchemaComparisonResult? _comparison;
    private SchemaSynchronisationPreview? _preview;
    private bool _closing;

    public SchemaComparisonWorkspaceWindow(NpgsqlSchemaModelExtractor extractor, string sourceConnectionString, DatabaseConnection source)
    {
        _extractor = extractor; _sourceConnectionString = sourceConnectionString; _source = source;
        Title = "Schema comparison and synchronisation preview"; Width = 1100; Height = 720; MinWidth = 800; MinHeight = 540; WindowStartupLocation = WindowStartupLocation.CenterOwner; Content = BuildContent();
        AutomationProperties.SetName(_targetHost, "Target server"); AutomationProperties.SetName(_targetDatabase, "Target database"); AutomationProperties.SetName(_targetPassword, "Target password"); AutomationProperties.SetName(_differences, "Schema differences"); AutomationProperties.SetName(_script, "Synchronisation script"); AutomationProperties.SetName(_compare, "Compare schemas"); AutomationProperties.SetName(_cancel, "Cancel schema comparison");
        _compare.Click += async (_, _) => await CompareAsync(); _cancel.Click += (_, _) => _cancellation?.Cancel(); _includeDestructive.Checked += (_, _) => RebuildPreview(); _includeDestructive.Unchecked += (_, _) => RebuildPreview(); _differences.SelectionChanged += (_, _) => ShowDetails(); _copy.Click += (_, _) => CopyScript(); _save.Click += (_, _) => SaveScript(); Closed += (_, _) => CloseWorkspace();
        _status.Text = $"Source: {_source.Host}:{_source.Port}/{_source.Database}. Enter a distinct target and compare.";
    }

    private UIElement BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(12) }; var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; actions.Children.Add(_copy); actions.Children.Add(_save); actions.Children.Add(_compare); actions.Children.Add(_cancel); DockPanel.SetDock(actions, Dock.Bottom); root.Children.Add(actions);
        var panel = new StackPanel(); panel.Children.Add(new TextBlock { Text = "Schema comparison", FontSize = 18, FontWeight = FontWeights.SemiBold }); panel.Children.Add(new TextBlock { Text = $"Source: {_source.Host}:{_source.Port}/{_source.Database} (active connection)", Margin = new Thickness(0, 4, 0, 4) });
        var target = new WrapPanel(); target.Children.Add(new TextBlock { Text = "Target host:", VerticalAlignment = VerticalAlignment.Center }); target.Children.Add(_targetHost); target.Children.Add(new TextBlock { Text = "Port:", Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center }); target.Children.Add(_targetPort); target.Children.Add(new TextBlock { Text = "Database:", Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center }); target.Children.Add(_targetDatabase); target.Children.Add(new TextBlock { Text = "User:", Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center }); target.Children.Add(_targetUser); target.Children.Add(new TextBlock { Text = "Password:", Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center }); target.Children.Add(_targetPassword); panel.Children.Add(target);
        var options = new WrapPanel { Margin = new Thickness(0, 6, 0, 6) }; options.Children.Add(new TextBlock { Text = "Schema scope (extractor currently uses all permitted non-system schemas):", VerticalAlignment = VerticalAlignment.Center }); options.Children.Add(_scope); options.Children.Add(new Border { Child = _includeDestructive, Margin = new Thickness(12, 0, 0, 0) }); panel.Children.Add(options); panel.Children.Add(_status);
        foreach (var column in new[] { ("Kind", "Kind"), ("Schema", "Schema"), ("Name", "Name"), ("Source", "SourceState"), ("Target", "TargetState"), ("Change", "Change"), ("Risk", "Risk"), ("Action", "Action") }) _differences.Columns.Add(new DataGridTextColumn { Header = column.Item1, Binding = new Binding(column.Item2) });
        var diffGrid = new Grid(); diffGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) }); diffGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) }); Grid.SetColumn(_differences, 0); Grid.SetColumn(_details, 1); diffGrid.Children.Add(_differences); diffGrid.Children.Add(_details); _tabs.Items.Add(new TabItem { Header = "Differences", Content = diffGrid }); _tabs.Items.Add(new TabItem { Header = "Synchronisation script", Content = _script }); panel.Children.Add(_tabs); root.Children.Add(panel); return root;
    }

    private async Task CompareAsync()
    {
        if (_cancellation is not null) return; _cancellation = new CancellationTokenSource(); var token = _cancellation.Token;
        try
        {
            var target = BuildTarget(); if (SameSource(target)) throw new InvalidOperationException("Source and target resolve to the same database. Select a distinct target deliberately."); SetBusy(true); _status.Text = "Extracting source and target schemas…";
            var sourceModel = await _extractor.ExtractAsync(_sourceConnectionString, token); var targetModel = await _extractor.ExtractAsync(BuildConnectionString(target), token); if (_closing || token.IsCancellationRequested) return; _comparison = SchemaComparisonService.Compare(sourceModel, targetModel); RebuildPreview(); _status.Text = _comparison.IsPartial ? $"Comparison completed with warnings. {_comparison.Differences.Count:N0} difference record(s)." : $"Comparison complete. {_comparison.Differences.Count(x => x.Action != SchemaAction.None):N0} actionable difference(s).";
        }
        catch (OperationCanceledException) { if (!_closing) _status.Text = "Comparison cancelled."; }
        catch (Exception ex) { if (!_closing) _status.Text = DesktopErrorPresentation.Failure("Schema comparison", ex); }
        finally { _cancellation?.Dispose(); _cancellation = null; if (!_closing) SetBusy(false); }
    }
    private void RebuildPreview()
    {
        if (_comparison is null) return; _preview = SchemaSynchronisationPreviewBuilder.Build(_comparison, Array.Empty<SchemaDependency>(), includeDestructive: _includeDestructive.IsChecked == true); _differences.ItemsSource = _preview.Items.Select(x => new DifferenceRow(x)).ToArray(); _script.Text = _preview.Script; _copy.IsEnabled = _preview.IncludedSteps.Count > 0; _save.IsEnabled = _preview.IncludedSteps.Count > 0; _status.Text = $"{_preview.Items.Count:N0} differences · {_preview.IncludedSteps.Count:N0} included · {_preview.WarningCount:N0} warnings · {_preview.DeletionCount:N0} deletions.";
    }
    private void ShowDetails() { if (_differences.SelectedItem is not DifferenceRow row) { _details.Text = "Select a difference to inspect source/target definitions and safety."; return; } var d = row.Item.Difference; var s = d.Source; var t = d.Target; _details.Text = $"{d.Kind} · {d.Action} · {d.Risk}\nReason: {d.Reason}\nSource: {s?.Schema}.{s?.Name ?? "missing"}\n{s?.Definition ?? ""}\n\nTarget: {t?.Schema}.{t?.Name ?? "missing"}\n{t?.Definition ?? ""}\n\nIncluded: {row.Item.Included}; Blocked: {row.Item.IsBlocked}"; }
    private void CopyScript() { try { Clipboard.SetText(_script.Text); _status.Text = "Synchronisation script copied."; } catch (Exception ex) { _status.Text = DesktopErrorPresentation.Failure("Copy script", ex); } }
    private void SaveScript() { var dialog = new SaveFileDialog { Filter = "SQL files (*.sql)|*.sql|All files (*.*)|*.*", FileName = "schema-synchronisation-preview.sql" }; if (dialog.ShowDialog(this) == true) File.WriteAllText(dialog.FileName, _script.Text); }
    private DatabaseConnection BuildTarget() => new(_targetHost.Text.Trim(), int.TryParse(_targetPort.Text, out var port) ? port : 5432, _targetDatabase.Text.Trim(), _targetUser.Text.Trim(), _targetPassword.Password);
    private bool SameSource(DatabaseConnection target) => string.Equals(target.Host, _source.Host, StringComparison.OrdinalIgnoreCase) && target.Port == _source.Port && string.Equals(target.Database, _source.Database, StringComparison.OrdinalIgnoreCase) && string.Equals(target.Username, _source.Username, StringComparison.OrdinalIgnoreCase);
    private static string BuildConnectionString(DatabaseConnection value) { var builder = new NpgsqlConnectionStringBuilder { Host = value.Host, Port = value.Port, Database = value.Database, Username = value.Username, Password = value.Password }; return builder.ConnectionString; }
    private void SetBusy(bool busy) { _compare.IsEnabled = !busy; _cancel.IsEnabled = busy; _targetHost.IsEnabled = !busy; _targetPort.IsEnabled = !busy; _targetDatabase.IsEnabled = !busy; _targetUser.IsEnabled = !busy; _targetPassword.IsEnabled = !busy; }
    private void CloseWorkspace() { if (_closing) return; _closing = true; _cancellation?.Cancel(); }
    private sealed class DifferenceRow
    {
        public DifferenceRow(SchemaPreviewItem item) => Item = item;
        public SchemaPreviewItem Item { get; }
        public string Kind => (Item.Difference.Source ?? Item.Difference.Target)!.Kind.ToString();
        public string Schema => (Item.Difference.Source ?? Item.Difference.Target)!.Schema;
        public string Name => (Item.Difference.Source ?? Item.Difference.Target)!.Name;
        public string SourceState => Item.Difference.Source is null ? "Missing" : "Present";
        public string TargetState => Item.Difference.Target is null ? "Missing" : "Present";
        public string Change => Item.Difference.Kind.ToString();
        public string Risk => Item.Difference.Risk.ToString();
        public string Action => Item.Difference.Action.ToString();
    }
}
