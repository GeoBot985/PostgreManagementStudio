using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Win32;
using Npgsql;
using PostgreManagementStudio.Application;
using PostgreManagementStudio.Postgres;
using PostgreManagementStudio.Results;
using PostgreManagementStudio.Core;

namespace PostgreManagementStudio.Desktop;

public enum DataTransferWorkspaceMode { Import, Export }

public sealed class DataTransferWorkspaceWindow : Window
{
    private readonly DataTransferWorkspaceMode _mode;
    private readonly TransferHistoryService _history;
    private readonly string _connectionString;
    private readonly string _database;
    private readonly NpgsqlDataTransferService? _importService;
    private readonly IResultExportService? _resultExport;
    private readonly IResultSetStore? _resultSet;
    private readonly ITransferMetadataProvider _metadata;
    private readonly IRelationExportService _relationExport;
    private readonly ProductionDelimitedFileInspector _inspector;
    private readonly TransferRelationSource? _relationSource;
    private readonly ResultSelection? _resultSelection;
    private readonly string[] _steps;
    private readonly ListBox _stepList = new() { Width = 190, IsHitTestVisible = false };
    private readonly TextBlock _stepTitle = new()
        { FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) };
    private readonly TextBlock _validation = new()
        { TextWrapping = TextWrapping.Wrap, Foreground = System.Windows.Media.Brushes.DarkRed };
    private readonly ContentControl _page = new();
    private readonly Button _back = new() { Content = "_Back", Width = 85 };
    private readonly Button _next = new() { Content = "_Next", Width = 85, IsDefault = true };
    private readonly Button _finish = new() { Content = "_Finish", Width = 85, Visibility = Visibility.Collapsed };
    private readonly Button _cancel = new() { Content = "Cancel", Width = 85, IsCancel = true };
    private readonly ProgressBar _progress = new() { Minimum = 0, Maximum = 100, Height = 18 };
    private readonly TextBlock _progressText = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBox _resultText = ReadOnlyMultiline();

    private readonly TextBox _path = new() { MinWidth = 420 };
    private readonly Button _browse = new() { Content = "_Browse…", Width = 90 };
    private readonly TextBlock _sourceFacts = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ComboBox _encoding = new() { Width = 220 };
    private readonly ComboBox _delimiter = new() { Width = 180 };
    private readonly TextBox _customDelimiter = new() { Width = 40, MaxLength = 1 };
    private readonly ComboBox _quote = new() { Width = 160 };
    private readonly CheckBox _header = new() { Content = "First row contains column names", IsChecked = true };
    private readonly CheckBox _normalizeHeaderSpaces = new()
        { Content = "Normalize header spaces to underscores" };
    private readonly CheckBox _trim = new() { Content = "Trim unquoted surrounding whitespace" };
    private readonly CheckBox _multiline = new() { Content = "Allow multiline quoted fields", IsChecked = true };
    private readonly TextBox _nullMarker = new() { Width = 100, Text = "\\N" };
    private readonly TextBox _skipRows = new() { Width = 70, Text = "0" };
    private readonly DataGrid _preview = CreateGrid("Source preview");
    private readonly RadioButton _existingTable = new() { Content = "Import into an existing table", IsChecked = true };
    private readonly RadioButton _newTable = new() { Content = "Create a new table" };
    private readonly ComboBox _schema = new() { Width = 220, IsEditable = true, Text = "public" };
    private readonly ComboBox _table = new() { Width = 300, IsEditable = true };
    private readonly TextBlock _destinationFacts = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ObservableCollection<WizardMappingRow> _mappings = new();
    private readonly DataGrid _mappingGrid = CreateGrid("Column mappings");
    private readonly ComboBox _mappingMode = new() { Width = 220 };
    private readonly ComboBox _strategy = new() { Width = 260 };
    private readonly ComboBox _transaction = new() { Width = 260 };
    private readonly ComboBox _errorMode = new() { Width = 260 };
    private readonly TextBox _batchSize = new() { Width = 90, Text = "500" };
    private readonly TextBox _errorLimit = new() { Width = 90, Text = "100" };
    private readonly TextBox _rejectedPath = new() { MinWidth = 360 };
    private readonly TextBox _review = ReadOnlyMultiline();

    private readonly ObservableCollection<ExportColumnRow> _exportColumns = new();
    private readonly DataGrid _exportColumnGrid = CreateGrid("Export columns");
    private readonly ComboBox _exportScope = new() { Width = 280 };
    private readonly TextBox _rowLimit = new() { Width = 100 };
    private readonly TextBox _where = new() { MinWidth = 420 };
    private readonly TextBox _orderBy = new() { MinWidth = 420 };
    private readonly ComboBox _exportFormat = new() { Width = 240 };
    private readonly ComboBox _exportEncoding = new() { Width = 220 };
    private readonly ComboBox _exportDelimiter = new() { Width = 180 };
    private readonly TextBox _exportCustomDelimiter = new() { Width = 40, MaxLength = 1 };
    private readonly ComboBox _exportQuote = new() { Width = 160 };
    private readonly ComboBox _exportLineEnding = new() { Width = 180 };
    private readonly TextBox _exportNullText = new() { Width = 100 };
    private readonly CheckBox _includeHeaders = new() { Content = "Include header row", IsChecked = true };
    private readonly CheckBox _prettyJson = new() { Content = "Pretty-print JSON array" };
    private readonly CheckBox _includeTransaction = new() { Content = "Wrap SQL output in BEGIN/COMMIT", IsChecked = true };
    private readonly TextBox _sqlBatch = new() { Width = 90, Text = "100" };

    private FileInspection? _inspection;
    private DelimitedPreview? _previewData;
    private TransferDestinationMetadata? _destinationMetadata;
    private CancellationTokenSource? _cancellation;
    private int _stepIndex;
    private bool _executing;
    private bool _completed;

    public DataTransferWorkspaceWindow(
        DataTransferWorkspaceMode mode,
        TransferHistoryService history,
        string connectionString,
        NpgsqlDataTransferService? importService = null,
        IResultExportService? exportService = null,
        IResultSetStore? resultSet = null,
        ITransferMetadataProvider? metadataProvider = null,
        IRelationExportService? relationExportService = null,
        ProductionDelimitedFileInspector? inspector = null,
        TransferRelationSource? relationSource = null,
        ResultSelection? resultSelection = null)
    {
        _mode = mode;
        _history = history;
        _connectionString = connectionString;
        _database = new NpgsqlConnectionStringBuilder(connectionString).Database ?? "postgres";
        _importService = importService;
        _resultExport = exportService;
        _resultSet = resultSet;
        _metadata = metadataProvider ?? new NpgsqlTransferMetadataProvider();
        _relationExport = relationExportService ?? new NpgsqlRelationExportService();
        _inspector = inspector ?? new ProductionDelimitedFileInspector();
        _relationSource = relationSource;
        _resultSelection = resultSelection;
        _steps = mode == DataTransferWorkspaceMode.Import
            ? ["Source", "Format", "Preview", "Destination", "Column Mapping",
                "Data Types and Rules", "Review", "Execution", "Results"]
            : ["Source", "Columns", "Rows and Filtering", "Format", "Destination",
                "Review", "Execution", "Results"];
        Title = mode == DataTransferWorkspaceMode.Import
            ? "Import data into PostgreSQL" : relationSource is null
                ? "Export query result" : $"Export {relationSource.QualifiedName}";
        Width = 1080;
        Height = 760;
        MinWidth = 820;
        MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = BuildShell();
        ConfigureControls();
        if (mode == DataTransferWorkspaceMode.Import && relationSource is not null)
        {
            _schema.Text = relationSource.Schema;
            _table.Text = relationSource.Name;
            _existingTable.IsChecked = true;
        }
        _stepList.ItemsSource = _steps.Select((name, index) => $"{index + 1}. {name}");
        _stepList.SelectedIndex = 0;
        _back.Click += async (_, _) => await MoveAsync(-1);
        _next.Click += async (_, _) => await MoveAsync(1);
        _finish.Click += async (_, _) => await ExecuteAsync();
        _cancel.Click += (_, _) => CancelOrClose();
        PreviewKeyDown += OnPreviewKeyDown;
        Closing += OnClosing;
        ShowStep();
    }

    private UIElement BuildShell()
    {
        var root = new Grid { Margin = new Thickness(12) };
        root.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        root.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var stepsBorder = new Border
        {
            BorderBrush = System.Windows.Media.Brushes.LightGray,
            BorderThickness = new Thickness(0, 0, 1, 0),
            Padding = new Thickness(0, 0, 12, 0),
            Child = _stepList,
        };
        root.Children.Add(stepsBorder);
        var content = new DockPanel { Margin = new Thickness(16, 0, 0, 0) };
        DockPanel.SetDock(_stepTitle, Dock.Top);
        DockPanel.SetDock(_validation, Dock.Top);
        content.Children.Add(_stepTitle);
        content.Children.Add(_validation);
        content.Children.Add(_page);
        Grid.SetColumn(content, 1);
        root.Children.Add(content);
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };
        foreach (var button in new[] { _back, _next, _finish, _cancel })
        {
            button.Margin = new Thickness(6, 0, 0, 0);
            actions.Children.Add(button);
        }
        Grid.SetRow(actions, 1);
        Grid.SetColumnSpan(actions, 2);
        root.Children.Add(actions);
        return root;
    }

    private void ConfigureControls()
    {
        AutomationProperties.SetName(_stepList, "Wizard steps");
        AutomationProperties.SetName(_validation, "Validation summary");
        AutomationProperties.SetName(_path, _mode == DataTransferWorkspaceMode.Import
            ? "Import source file" : "Export destination file");
        AutomationProperties.SetName(_review, "Transfer review");
        AutomationProperties.SetName(_resultText, "Transfer results");
        AutomationProperties.SetLiveSetting(_validation, AutomationLiveSetting.Assertive);
        AutomationProperties.SetName(_progress, "Transfer progress");
        AutomationProperties.SetName(_progressText, "Transfer progress detail");
        _path.AllowDrop = _mode == DataTransferWorkspaceMode.Import;
        _path.PreviewDragOver += (_, args) => args.Effects = DragDropEffects.Copy;
        _path.Drop += (_, args) =>
        {
            if (args.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
                _path.Text = files[0];
        };
        _browse.Click += (_, _) => Browse();
        _encoding.ItemsSource = new[]
        {
            "Detected / automatic", "UTF-8", "UTF-8 with BOM",
            "UTF-16 little-endian", "UTF-16 big-endian", "Windows-1252",
        };
        _encoding.SelectedIndex = 0;
        _delimiter.ItemsSource = new[] { "Comma (,)", "Tab", "Semicolon (;)", "Pipe (|)", "Custom" };
        _delimiter.SelectedIndex = 0;
        _quote.ItemsSource = new[] { "Double quote (\")", "Single quote (')", "None" };
        _quote.SelectedIndex = 0;
        _mappingMode.ItemsSource = Enum.GetValues<ImportMappingMode>();
        _mappingMode.SelectedItem = ImportMappingMode.CaseInsensitiveName;
        _mappingMode.SelectionChanged += (_, _) => ApplyAutomaticMapping();
        _strategy.ItemsSource = new[]
        {
            "Automatic fast import (safe COPY with typed fallback)",
            "Validated typed import using parameterised batches",
        };
        _strategy.SelectedIndex = 0;
        _transaction.ItemsSource = new[]
        {
            "Atomic import — all rows commit or roll back",
            "Batched import — completed batches remain committed",
        };
        _transaction.SelectedIndex = 0;
        _errorMode.ItemsSource = new[] { "Stop on first error", "Collect errors and rejected rows" };
        _errorMode.SelectedIndex = 0;
        _exportScope.ItemsSource = _relationSource is null
            ? new[] { "All retained rows", "Selected rows/cell range" }
            : new[] { "All rows", "Apply row limit and optional SQL filter" };
        _exportScope.SelectedIndex = 0;
        _exportFormat.ItemsSource = new[]
        {
            "CSV", "TSV", "JSON array", "JSON Lines", "PostgreSQL INSERT statements",
        };
        _exportFormat.SelectedIndex = 0;
        _exportEncoding.ItemsSource = new[] { "UTF-8", "UTF-8 with BOM", "UTF-16 little-endian" };
        _exportEncoding.SelectedIndex = 0;
        _exportDelimiter.ItemsSource = new[] { "Comma (,)", "Tab", "Semicolon (;)", "Pipe (|)", "Custom" };
        _exportDelimiter.SelectedIndex = 0;
        _exportQuote.ItemsSource = new[] { "Double quote (\")", "Single quote (')" };
        _exportQuote.SelectedIndex = 0;
        _exportLineEnding.ItemsSource = new[] { "Windows (CRLF)", "Unix (LF)" };
        _exportLineEnding.SelectedIndex = 0;
        _exportFormat.SelectionChanged += (_, _) =>
        {
            if (_exportFormat.SelectedIndex == 1) _exportDelimiter.SelectedIndex = 1;
            else if (_exportFormat.SelectedIndex == 0 && _exportDelimiter.SelectedIndex == 1)
                _exportDelimiter.SelectedIndex = 0;
        };
        BuildMappingColumns();
        BuildExportColumns();
    }

    private void BuildMappingColumns()
    {
        _mappingGrid.AutoGenerateColumns = false;
        _mappingGrid.Columns.Add(new DataGridCheckBoxColumn
            { Header = "Include", Binding = new Binding(nameof(WizardMappingRow.Included)) });
        _mappingGrid.Columns.Add(new DataGridTextColumn
            { Header = "Source column", Binding = new Binding(nameof(WizardMappingRow.SourceName)), IsReadOnly = true });
        _mappingGrid.Columns.Add(new DataGridTextColumn
            { Header = "Sample value", Binding = new Binding(nameof(WizardMappingRow.Sample)), IsReadOnly = true });
        _mappingGrid.Columns.Add(new DataGridComboBoxColumn
        {
            Header = "Destination column",
            SelectedItemBinding = new Binding(nameof(WizardMappingRow.DestinationName))
                { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
            ItemsSource = _mappings.Select(row => row.DestinationName).ToArray(),
        });
        _mappingGrid.Columns.Add(new DataGridTextColumn
            { Header = "PostgreSQL type", Binding = new Binding(nameof(WizardMappingRow.DestinationType)) });
        _mappingGrid.Columns.Add(new DataGridTextColumn
            { Header = "Mapping status", Binding = new Binding(nameof(WizardMappingRow.Status)), IsReadOnly = true });
        _mappingGrid.Columns.Add(new DataGridCheckBoxColumn
            { Header = "Trim", Binding = new Binding(nameof(WizardMappingRow.TrimWhitespace)) });
        _mappingGrid.Columns.Add(new DataGridCheckBoxColumn
            { Header = "Empty → NULL", Binding = new Binding(nameof(WizardMappingRow.EmptyBecomesNull)) });
        _mappingGrid.Columns.Add(new DataGridTextColumn
            { Header = "Date/time format", Binding = new Binding(nameof(WizardMappingRow.DateTimeFormat)) });
        _mappingGrid.Columns.Add(new DataGridTextColumn
            { Header = "Decimal separator", Binding = new Binding(nameof(WizardMappingRow.DecimalSeparator)) });
        _mappingGrid.Columns.Add(new DataGridTextColumn
            { Header = "Thousands separator", Binding = new Binding(nameof(WizardMappingRow.ThousandsSeparator)) });
        _mappingGrid.Columns.Add(new DataGridTextColumn
            { Header = "True values", Binding = new Binding(nameof(WizardMappingRow.TrueValues)) });
        _mappingGrid.Columns.Add(new DataGridTextColumn
            { Header = "False values", Binding = new Binding(nameof(WizardMappingRow.FalseValues)) });
        _mappingGrid.Columns.Add(new DataGridTextColumn
            { Header = "Time-zone assumption", Binding = new Binding(nameof(WizardMappingRow.TimeZoneAssumption)) });
        _mappingGrid.Columns.Add(new DataGridCheckBoxColumn
            { Header = "Strip currency", Binding = new Binding(nameof(WizardMappingRow.StripCurrencySymbol)) });
        _mappingGrid.Columns.Add(new DataGridCheckBoxColumn
            { Header = "(n) is negative", Binding = new Binding(nameof(WizardMappingRow.ParenthesesAreNegative)) });
        _mappingGrid.Columns.Add(new DataGridCheckBoxColumn
            { Header = "Invalid → NULL", Binding = new Binding(nameof(WizardMappingRow.SubstituteInvalidWithNull)) });
    }

    private void BuildExportColumns()
    {
        _exportColumnGrid.AutoGenerateColumns = false;
        _exportColumnGrid.Columns.Add(new DataGridCheckBoxColumn
            { Header = "Include", Binding = new Binding(nameof(ExportColumnRow.Included)) });
        _exportColumnGrid.Columns.Add(new DataGridTextColumn
            { Header = "Source column", Binding = new Binding(nameof(ExportColumnRow.SourceName)), IsReadOnly = true });
        _exportColumnGrid.Columns.Add(new DataGridTextColumn
            { Header = "Output header", Binding = new Binding(nameof(ExportColumnRow.OutputName)) });
        _exportColumnGrid.Columns.Add(new DataGridTextColumn
            { Header = "Type", Binding = new Binding(nameof(ExportColumnRow.DataType)), IsReadOnly = true });
        _exportColumnGrid.Columns.Add(new DataGridTextColumn
            { Header = "Order", Binding = new Binding(nameof(ExportColumnRow.Ordinal)), IsReadOnly = true });
        _exportColumnGrid.ItemsSource = _exportColumns;
    }

    private async Task MoveAsync(int direction)
    {
        if (_executing) return;
        _validation.Text = string.Empty;
        try
        {
            if (direction > 0 && !await PrepareCurrentStepAsync()) return;
            _stepIndex = Math.Clamp(_stepIndex + direction, 0, _steps.Length - 1);
            ShowStep();
        }
        catch (Exception exception)
        {
            _validation.Text = DesktopErrorPresentation.Failure("Wizard validation", exception);
        }
    }

    private async Task<bool> PrepareCurrentStepAsync()
    {
        if (_mode == DataTransferWorkspaceMode.Import)
        {
            if (_stepIndex == 0)
            {
                if (string.IsNullOrWhiteSpace(_path.Text)) return Invalid("Choose a readable source file.");
                _inspection = await _inspector.InspectAsync(_path.Text);
                ApplyDetectedFormat(_inspection);
            }
            else if (_stepIndex == 1)
            {
                _previewData = await _inspector.PreviewAsync(_path.Text, SelectedEncoding(),
                    SelectedFormat(), 200);
                if (_normalizeHeaderSpaces.IsChecked == true)
                    _previewData = _previewData with
                    {
                        Headers = HeaderNormalizationService.Normalize(
                            _previewData.Headers, convertSpacesToUnderscores: true),
                    };
                BuildPreviewGrid(_previewData);
                BuildInferredMappings(_previewData);
            }
            else if (_stepIndex == 2)
                await LoadDestinationChoicesAsync();
            else if (_stepIndex == 3)
                await LoadDestinationColumnsAsync();
            else if (_stepIndex == 4 && !ValidateMappings()) return false;
            else if (_stepIndex == 5)
            {
                if (!ValidateRules()) return false;
                BuildImportReview();
            }
        }
        else
        {
            if (_stepIndex == 0) await LoadExportColumnsAsync();
            else if (_stepIndex == 1 && _exportColumns.All(column => !column.Included))
                return Invalid("Select at least one export column.");
            else if (_stepIndex == 2 && !ValidateExportRows()) return false;
            else if (_stepIndex == 4)
            {
                if (string.IsNullOrWhiteSpace(_path.Text))
                    return Invalid("Choose an export destination file.");
                BuildExportReview();
            }
        }
        return true;
    }

    private void ShowStep()
    {
        _stepList.SelectedIndex = _stepIndex;
        _stepTitle.Text = _steps[_stepIndex];
        _page.Content = _mode == DataTransferWorkspaceMode.Import
            ? ImportPage(_stepIndex) : ExportPage(_stepIndex);
        _back.IsEnabled = _stepIndex > 0 && !_executing && !_completed;
        var reviewIndex = _mode == DataTransferWorkspaceMode.Import ? 6 : 5;
        var terminal = _stepIndex >= reviewIndex + 1;
        _next.Visibility = _stepIndex < reviewIndex ? Visibility.Visible : Visibility.Collapsed;
        _finish.Visibility = _stepIndex == reviewIndex ? Visibility.Visible : Visibility.Collapsed;
        _finish.IsEnabled = !_executing;
        _cancel.Content = _executing ? "Cancel operation" : _completed ? "Close" : "Cancel";
        _cancel.IsCancel = !_executing;
    }

    private UIElement ImportPage(int index) => index switch
    {
        0 => SourcePage("Delimited files are read by the application and streamed from this computer."),
        1 => FormatPage(),
        2 => GridPage(_preview,
            "Bounded sample only. Malformed rows are marked and the complete file is not loaded into memory."),
        3 => DestinationPage(),
        4 => MappingPage(),
        5 => RulesPage(),
        6 => GridPage(_review, "Review every setting before Finish starts the import."),
        7 => ProgressPage(),
        _ => ResultsPage(),
    };

    private UIElement ExportPage(int index) => index switch
    {
        0 => GridPage(new TextBlock
        {
            Text = ExportSourceSummary(),
            TextWrapping = TextWrapping.Wrap,
        }, "The application streams data through the active connection to a local temporary file."),
        1 => GridPage(_exportColumnGrid, "Preserve source order by default; edit output headers as needed."),
        2 => ExportRowsPage(),
        3 => ExportFormatPage(),
        4 => SourcePage("The final path is replaced only after successful completion."),
        5 => GridPage(_review, "Review completeness warnings before Finish starts the export."),
        6 => ProgressPage(),
        _ => ResultsPage(),
    };

    private UIElement SourcePage(string note)
    {
        var browseRow = Horizontal(Label(_mode == DataTransferWorkspaceMode.Import
            ? "Local source file:" : "Destination file:"), _path, _browse);
        return Vertical(new TextBlock { Text = note, TextWrapping = TextWrapping.Wrap },
            browseRow, _sourceFacts);
    }

    private UIElement FormatPage() => Vertical(
        Horizontal(Label("Encoding:"), _encoding),
        Horizontal(Label("Delimiter:"), _delimiter, _customDelimiter),
        Horizontal(Label("Quote character:"), _quote),
        _header, _normalizeHeaderSpaces, _trim, _multiline,
        Horizontal(Label("NULL marker:"), _nullMarker, Label("Rows to skip:"), _skipRows),
        new TextBlock
        {
            Text = "Empty strings remain empty by default. The explicit NULL marker becomes PostgreSQL NULL.",
            TextWrapping = TextWrapping.Wrap,
        });

    private UIElement DestinationPage() => Vertical(
        _existingTable, _newTable,
        Horizontal(Label("Schema:"), _schema),
        Horizontal(Label("Table:"), _table),
        _destinationFacts);

    private UIElement MappingPage()
    {
        var exact = new Button { Content = "Auto-map" };
        exact.Click += (_, _) => ApplyAutomaticMapping();
        var ordinal = new Button { Content = "Map by ordinal" };
        ordinal.Click += (_, _) =>
        {
            _mappingMode.SelectedItem = ImportMappingMode.Ordinal;
            ApplyAutomaticMapping();
        };
        var clear = new Button { Content = "Clear mappings" };
        clear.Click += (_, _) =>
        {
            foreach (var row in _mappings) { row.Included = false; row.DestinationName = null; }
            _mappingGrid.Items.Refresh();
        };
        return Vertical(Horizontal(Label("Automatic mapping:"), _mappingMode, exact, ordinal, clear),
            _mappingGrid);
    }

    private UIElement RulesPage()
    {
        var rejectedBrowse = new Button { Content = "Browse…" };
        rejectedBrowse.Click += (_, _) =>
        {
            var dialog = new SaveFileDialog
            {
                Filter = "CSV (*.csv)|*.csv", DefaultExt = ".csv",
                FileName = Path.GetFileNameWithoutExtension(_path.Text) + "-rejected.csv",
            };
            if (dialog.ShowDialog(this) == true) _rejectedPath.Text = dialog.FileName;
        };
        return Vertical(
            new TextBlock
            {
                Text = "Per-column conversion settings are editable in the mapping grid. Invalid values are rejected by default.",
                TextWrapping = TextWrapping.Wrap,
            },
            Horizontal(Label("Execution strategy:"), _strategy),
            Horizontal(Label("Transaction mode:"), _transaction),
            Horizontal(Label("Error mode:"), _errorMode),
            Horizontal(Label("Batch size:"), _batchSize, Label("Maximum errors:"), _errorLimit),
            Horizontal(Label("Rejected-row report:"), _rejectedPath, rejectedBrowse));
    }

    private UIElement ExportRowsPage() => Vertical(
        Horizontal(Label("Rows:"), _exportScope),
        Horizontal(Label("Maximum rows (blank = all):"), _rowLimit),
        Horizontal(Label("Optional SQL WHERE predicate:"), _where),
        Horizontal(Label("Optional SQL ORDER BY:"), _orderBy),
        new TextBlock
        {
            Text = _relationSource is null
                ? "Result exports contain only rows retained by the client. Truncated results are never labelled complete."
                : "SQL fragments are shown on Review and execute only in the selected database. Statement separators and comments are blocked.",
            TextWrapping = TextWrapping.Wrap,
        });

    private UIElement ExportFormatPage() => Vertical(
        Horizontal(Label("Output format:"), _exportFormat),
        Horizontal(Label("Encoding:"), _exportEncoding),
        Horizontal(Label("Delimiter:"), _exportDelimiter, _exportCustomDelimiter),
        Horizontal(Label("Quote character:"), _exportQuote),
        Horizontal(Label("Line endings:"), _exportLineEnding),
        Horizontal(Label("NULL text:"), _exportNullText),
        _includeHeaders, _prettyJson, _includeTransaction,
        Horizontal(Label("SQL rows per INSERT:"), _sqlBatch),
        new TextBlock
        {
            Text = "Delimited output uses RFC-style escaping. JSON Lines is recommended for very large exports.",
            TextWrapping = TextWrapping.Wrap,
        });

    private UIElement ProgressPage() => Vertical(_progress, _progressText);
    private UIElement ResultsPage() => Vertical(_resultText);

    private async Task LoadDestinationChoicesAsync()
    {
        _destinationMetadata = await _metadata.LoadAsync(_connectionString, _database);
        _schema.ItemsSource = _destinationMetadata.Schemas;
        if (_destinationMetadata.Schemas.Contains("public")) _schema.Text = "public";
        _table.ItemsSource = _destinationMetadata.Relations.Where(relation => relation.CanImport)
            .Select(relation => relation.Name).ToArray();
        if (string.IsNullOrWhiteSpace(_table.Text))
            _table.Text = ProposedTableName(_path.Text);
    }

    private async Task LoadDestinationColumnsAsync()
    {
        if (string.IsNullOrWhiteSpace(_schema.Text) || string.IsNullOrWhiteSpace(_table.Text))
        {
            Invalid("Choose a destination schema and table.");
            return;
        }
        if (_newTable.IsChecked == true)
        {
            var permissions = await _metadata.LoadAsync(
                _connectionString, _database, _schema.Text);
            var proposals = ProductionDataTypeInferenceService.Infer(
                _previewData!.Headers, _previewData.Records);
            _destinationMetadata = permissions with
            {
                Columns = proposals.Select(proposal =>
                    new DestinationColumn(proposal.Name, proposal.PostgreSqlType, true)).ToArray(),
            };
            BuildInferredMappings(_previewData!);
            _destinationFacts.Text =
                "New table definition is inferred from the bounded sample and remains editable.";
        }
        else
        {
            _destinationMetadata = await _metadata.LoadAsync(
                _connectionString, _database, _schema.Text, _table.Text);
            if (_destinationMetadata.Columns.Count == 0)
            {
                Invalid("The selected table was not found or has no visible columns.");
                return;
            }
            BuildExistingMappings();
            _destinationFacts.Text =
                $"Loaded {_destinationMetadata.Columns.Count:N0} PostgreSQL columns. "
                + "Generated and identity-always columns cannot be mapped.";
        }
    }

    private async Task LoadExportColumnsAsync()
    {
        if (_exportColumns.Count > 0) return;
        if (_relationSource is not null)
        {
            var metadata = await _metadata.LoadAsync(_connectionString, _database,
                _relationSource.Schema, _relationSource.Name);
            foreach (var column in metadata.Columns.Select((value, index) => (value, index)))
                _exportColumns.Add(new(column.index + 1, column.value.Name,
                    column.value.Name, column.value.PostgreSqlType));
        }
        else if (_resultSet is not null)
        {
            foreach (var column in _resultSet.Schema.Columns.Select((value, index) => (value, index)))
                _exportColumns.Add(new(column.index + 1, column.value.Name,
                    column.value.Name, column.value.PostgreSqlTypeName ?? "result value"));
        }
        else Invalid("No export source is available.");
    }

    private void ApplyDetectedFormat(FileInspection inspection)
    {
        _sourceFacts.Text =
            $"{inspection.SizeBytes:N0} bytes · {inspection.EncodingLabel} · {inspection.LineEnding} · "
            + $"approximately {inspection.EstimatedRows:N0} rows. Detection is a best estimate.";
        _delimiter.SelectedIndex = inspection.Format.Delimiter switch
        {
            ',' => 0, '\t' => 1, ';' => 2, '|' => 3, _ => 4,
        };
        if (_delimiter.SelectedIndex == 4) _customDelimiter.Text = inspection.Format.Delimiter.ToString();
        _quote.SelectedIndex = inspection.Format.Quote switch { '"' => 0, '\'' => 1, _ => 2 };
        _header.IsChecked = inspection.Format.HasHeader;
        _nullMarker.Text = inspection.Format.NullMarker;
    }

    private void BuildPreviewGrid(DelimitedPreview preview)
    {
        _preview.Columns.Clear();
        _preview.Columns.Add(new DataGridTextColumn
            { Header = "Source row", Binding = new Binding(nameof(PreviewRow.SourceRow)) });
        _preview.Columns.Add(new DataGridTextColumn
            { Header = "Status", Binding = new Binding(nameof(PreviewRow.Status)) });
        for (var index = 0; index < preview.Headers.Count; index++)
        {
            var ordinal = index;
            _preview.Columns.Add(new DataGridTextColumn
            {
                Header = preview.Headers[index],
                Binding = new Binding($"Values[{ordinal}]"),
            });
        }
        _preview.ItemsSource = preview.Records.Select(record =>
            new PreviewRow(record.SourceRow,
                record.IsMalformed ? record.Error! : "Valid sample row",
                record.Fields.Select(field => field.IsExplicitNull ? "<NULL>" : field.Value).ToArray()))
            .ToArray();
        _sourceFacts.Text = $"Sampled {preview.Records.Count:N0} rows. "
            + string.Join(" ", preview.Warnings.Take(4));
    }

    private void BuildInferredMappings(DelimitedPreview preview)
    {
        var proposals = ProductionDataTypeInferenceService.Infer(preview.Headers, preview.Records);
        _mappings.Clear();
        foreach (var proposal in proposals.Select((value, index) => (value, index)))
        {
            var sample = preview.Records.FirstOrDefault(record =>
                proposal.index < record.Fields.Count)?.Fields[proposal.index].Value ?? string.Empty;
            _mappings.Add(new(proposal.index, proposal.value.Name, sample,
                proposal.value.Name, proposal.value.PostgreSqlType, true,
                $"Inferred proposal ({proposal.value.Confidence:P0})"));
        }
        RefreshDestinationChoices();
    }

    private void BuildExistingMappings()
    {
        var source = _previewData!.Headers.Select((name, ordinal) =>
            new SourceColumn(ordinal, name,
                _previewData.Records.FirstOrDefault(record => ordinal < record.Fields.Count)
                    ?.Fields[ordinal].Value ?? string.Empty)).ToArray();
        var mappings = ProductionImportMappingService.Map(source, _destinationMetadata!.Columns,
            _mappingMode.SelectedItem is ImportMappingMode mode
                ? mode : ImportMappingMode.CaseInsensitiveName);
        _mappings.Clear();
        foreach (var sourceColumn in source)
        {
            var mapping = mappings.Single(value => value.SourceOrdinal == sourceColumn.Ordinal);
            var destination = _destinationMetadata.Columns.FirstOrDefault(column =>
                column.Name == mapping.DestinationName);
            _mappings.Add(new(sourceColumn.Ordinal, sourceColumn.Name, sourceColumn.Sample,
                mapping.DestinationName, destination?.PostgreSqlType ?? string.Empty,
                mapping.Included, mapping.Included ? "Mapped" : "Unmapped"));
        }
        RefreshDestinationChoices();
    }

    private void RefreshDestinationChoices()
    {
        if (_mappingGrid.Columns.OfType<DataGridComboBoxColumn>().FirstOrDefault() is { } combo)
            combo.ItemsSource = _destinationMetadata?.Columns.Where(column => column.Writable)
                .Select(column => column.Name).ToArray()
                ?? _mappings.Select(row => row.DestinationName).Where(name => name is not null).ToArray();
        _mappingGrid.ItemsSource = _mappings;
        _mappingGrid.Items.Refresh();
    }

    private void ApplyAutomaticMapping()
    {
        if (_destinationMetadata?.Columns.Count > 0 && _previewData is not null)
            BuildExistingMappings();
    }

    private bool ValidateMappings()
    {
        var mappings = CurrentMappings();
        if (mappings.All(mapping => !mapping.Included || mapping.DestinationName is null))
            return Invalid("Map at least one source column.");
        try
        {
            ImportMappingService.Validate(mappings, CurrentDestinationColumns());
            return true;
        }
        catch (ArgumentException exception) { return Invalid(exception.Message); }
    }

    private bool ValidateRules()
    {
        if (!int.TryParse(_batchSize.Text, out var batch) || batch <= 0)
            return Invalid("Batch size must be a positive integer.");
        if (!int.TryParse(_errorLimit.Text, out var limit) || limit <= 0)
            return Invalid("Maximum errors must be a positive integer.");
        if (_errorMode.SelectedIndex == 1 && EffectiveImportStrategy() == ImportStrategy.Copy)
            return Invalid("Collect-errors mode requires row-by-row validated import.");
        return true;
    }

    private bool ValidateExportRows()
    {
        if (!string.IsNullOrWhiteSpace(_rowLimit.Text)
            && (!long.TryParse(_rowLimit.Text, out var value) || value <= 0))
            return Invalid("Row limit must be blank or a positive integer.");
        if (_relationSource is null && _exportScope.SelectedIndex == 1 && _resultSelection is null)
            return Invalid("No rectangular result-grid selection is available.");
        return true;
    }

    private void BuildImportReview()
    {
        var destinationColumns = CurrentDestinationColumns();
        var createSql = _newTable.IsChecked == true
            ? NewTableSqlBuilder.Build(_schema.Text, _table.Text, destinationColumns)
            : null;
        var preflight = ProductionImportPreflight.Validate(new(
            _path.Text, _schema.Text, _table.Text,
            _newTable.IsChecked == true ? ImportDestinationMode.CreateNewTable
                : ImportDestinationMode.ExistingTable,
            CurrentMappings(), destinationColumns,
            EffectiveImportStrategy(),
            _transaction.SelectedIndex == 0 ? TransactionMode.AllRows : TransactionMode.PerBatch,
            _errorMode.SelectedIndex == 1,
            _destinationMetadata?.HasCreatePermission ?? true,
            _newTable.IsChecked == true || (_destinationMetadata?.HasInsertPermission ?? true)));
        if (!preflight.IsValid) throw new InvalidOperationException(string.Join(Environment.NewLine, preflight.Errors));
        _review.Text =
            $"Source: {_path.Text}\r\n"
            + $"Format: delimiter {Display(SelectedFormat().Delimiter)}, quote {Display(SelectedFormat().Quote)}"
            + $", encoding {SelectedEncoding().WebName}, header {_header.IsChecked == true}, NULL marker {_nullMarker.Text}\r\n"
            + $"Destination: {_database} / {_schema.Text}.{_table.Text} "
            + $"({(_newTable.IsChecked == true ? "create new table" : "existing table")})\r\n"
            + $"Estimated rows: {_inspection?.EstimatedRows:N0}\r\n"
            + $"Strategy: {ImportStrategySelector.DisplayName(EffectiveImportStrategy())}\r\n"
            + $"Requested mode: {_strategy.SelectedItem}\r\nTransaction: {_transaction.SelectedItem}\r\n"
            + $"Errors: {_errorMode.SelectedItem}; limit {_errorLimit.Text}; rejected file {_rejectedPath.Text}\r\n"
            + $"Mappings:\r\n{string.Join("\r\n", _mappings.Select(row =>
                $"  {(row.Included ? "Include" : "Exclude")} {row.SourceName} → {row.DestinationName ?? "<unmapped>"} ({row.DestinationType})"))}\r\n"
            + (createSql is null ? string.Empty : $"\r\nGenerated CREATE TABLE proposal:\r\n{createSql}\r\n")
            + $"\r\nWarnings:\r\n{string.Join("\r\n", preflight.Warnings.Select(warning => "  " + warning))}";
    }

    private void BuildExportReview()
    {
        var selected = _exportColumns.Where(column => column.Included).ToArray();
        var completeness = _relationSource is not null ? "Complete database source"
            : _resultSet?.WasTruncated == true
                ? $"INCOMPLETE — retained {_resultSet.LoadedRowCount:N0} of {_resultSet.ReceivedRowCount:N0} received rows"
                : $"Complete retained result ({_resultSet?.LoadedRowCount:N0} rows)";
        _review.Text =
            $"Source: {ExportSourceSummary()}\r\n"
            + $"Database: {_database}\r\nRows: {_exportScope.SelectedItem}; limit {_rowLimit.Text}\r\n"
            + $"Filter: {_where.Text}\r\nOrder: {_orderBy.Text}\r\n"
            + $"Columns: {string.Join(", ", selected.Select(column => column.SourceName))}\r\n"
            + $"Output headers: {string.Join(", ", selected.Select(column => column.OutputName))}\r\n"
            + $"Format: {_exportFormat.SelectedItem}; {SelectedExportEncoding().WebName}; "
            + $"delimiter {Display(SelectedExportDelimiter())}; quote {Display(SelectedExportQuote())}; "
            + $"line endings {(_exportLineEnding.SelectedIndex == 0 ? "CRLF" : "LF")}; "
            + $"NULL text {_exportNullText.Text}; headers {_includeHeaders.IsChecked == true}\r\n"
            + $"Destination: {_path.Text}\r\nCompleteness: {completeness}\r\n"
            + "Processing: client-side streaming through the active PostgreSQL connection to an atomic temporary file.";
    }

    private async Task ExecuteAsync()
    {
        if (_executing) return;
        _validation.Text = string.Empty;
        try
        {
            _executing = true;
            _cancellation = new CancellationTokenSource();
            _stepIndex++;
            ShowStep();
            var started = DateTimeOffset.UtcNow;
            if (_mode == DataTransferWorkspaceMode.Import)
                await ExecuteImportAsync(started, _cancellation.Token);
            else await ExecuteExportAsync(started, _cancellation.Token);
        }
        catch (Exception exception)
        {
            _resultText.Text = DesktopErrorPresentation.Failure("Data transfer", exception)
                + "\r\n\r\nThe connection remains available; review settings and retry.";
        }
        finally
        {
            _executing = false;
            _completed = true;
            _cancellation?.Dispose();
            _cancellation = null;
            _stepIndex = _steps.Length - 1;
            ShowStep();
        }
    }

    private async Task ExecuteImportAsync(DateTimeOffset started, CancellationToken cancellationToken)
    {
        var destinationColumns = CurrentDestinationColumns();
        var createSql = _newTable.IsChecked == true
            ? NewTableSqlBuilder.Build(_schema.Text, _table.Text, destinationColumns) : null;
        var request = new ImportRequest(
            _path.Text, _schema.Text, _table.Text, CurrentMappings(),
            new(SelectedFormat().Delimiter, SelectedFormat().Quote ?? '\0',
                SelectedEncoding(), _header.IsChecked == true, _nullMarker.Text,
                _trim.IsChecked == true),
            new(
                EffectiveImportStrategy(),
                ExistingDataMode.Append,
                _transaction.SelectedIndex == 0 ? TransactionMode.AllRows : TransactionMode.PerBatch,
                int.Parse(_batchSize.Text),
                _errorMode.SelectedIndex == 1,
                int.Parse(_errorLimit.Text),
                string.IsNullOrWhiteSpace(_rejectedPath.Text) ? null : _rejectedPath.Text),
            destinationColumns,
            _newTable.IsChecked == true,
            createSql,
            _mappings.ToDictionary(row => row.SourceOrdinal,
                row => new ImportColumnRule(row.TrimWhitespace, row.EmptyBecomesNull,
                    DateFormat: row.DateTimeFormat, TimeFormat: row.DateTimeFormat,
                    TimestampFormat: row.DateTimeFormat,
                    DecimalSeparator: string.IsNullOrEmpty(row.DecimalSeparator)
                        ? "." : row.DecimalSeparator,
                    ThousandsSeparator: EmptyToNull(row.ThousandsSeparator),
                    TrueValues: SplitValues(row.TrueValues),
                    FalseValues: SplitValues(row.FalseValues),
                    StripCurrencySymbol: row.StripCurrencySymbol,
                    ParenthesesAreNegative: row.ParenthesesAreNegative,
                    InvalidValueMode: row.SubstituteInvalidWithNull
                        ? InvalidValueMode.SubstituteNull : InvalidValueMode.RejectRow,
                    TimeZoneAssumption: EmptyToNull(row.TimeZoneAssumption))),
            _previewData?.Headers);
        var progress = new Progress<ImportProgress>(value =>
        {
            var throughput = value.Elapsed is { TotalSeconds: > 0 } elapsed
                ? value.RowsWritten / elapsed.TotalSeconds : 0;
            _progress.IsIndeterminate = true;
            _progressText.Text =
                $"{value.Phase}\r\nRows read {value.RowsRead:N0} · imported {value.RowsWritten:N0} · "
                + $"rejected {value.RowsRejected:N0} · batch {value.CurrentBatch:N0} · "
                + $"{throughput:N0} rows/s · elapsed {value.Elapsed:g}";
        });
        var result = await _importService!.ImportAsync(
            _connectionString, request, progress, cancellationToken);
        _resultText.Text =
            $"{result.Status}\r\nDestination: {_schema.Text}.{_table.Text}\r\n"
            + $"Strategy: {ImportStrategySelector.DisplayName(request.Options.Strategy)}\r\n"
            + $"Rows read: {result.RowsRead:N0}\r\nRows imported: {result.RowsWritten:N0}\r\n"
            + $"Rows rejected: {result.RowsRejected:N0}\r\nRows skipped: {result.RowsSkipped:N0}\r\n"
            + $"Elapsed: {result.Elapsed:g}\r\nTransaction: "
            + (result.PartialCommit ? "Partial batches committed" : result.Status == "Cancelled"
                ? "Active atomic transaction rolled back" : "Committed")
            + $"\r\nNew table created: {result.NewTableCreated}\r\n"
            + $"Rejected-row report: {result.RejectedRowsPath ?? "None"}\r\n"
            + (result.Errors.Count == 0 ? string.Empty
                : "\r\nErrors:\r\n" + string.Join("\r\n", result.Errors.Take(100)));
        _history.Add(new(started, DateTimeOffset.UtcNow, "Import", _path.Text,
            $"{_schema.Text}.{_table.Text}", result.Status, result.RowsRead,
            result.RowsWritten, result.RowsRejected, result.RejectedRowsPath, result.Errors));
    }

    private ImportStrategy EffectiveImportStrategy() =>
        ImportStrategySelector.Select(
            _strategy.SelectedIndex == 0 ? ImportStrategy.Copy : ImportStrategy.BatchInsert,
            CurrentMappings(),
            CurrentDestinationColumns());

    private async Task ExecuteExportAsync(DateTimeOffset started, CancellationToken cancellationToken)
    {
        var format = SelectedExportFormat();
        if (_relationSource is not null)
        {
            var selected = _exportColumns.Where(column => column.Included).ToArray();
            var request = new RelationExportRequest(
                _relationSource.Schema, _relationSource.Name, (RelationExportFormat)format,
                _path.Text, new(selected.Select(column => column.SourceName).ToArray(),
                    selected.Select(column => column.OutputName).ToArray(),
                    long.TryParse(_rowLimit.Text, out var limit) ? limit : null,
                    string.IsNullOrWhiteSpace(_where.Text) ? null : _where.Text,
                    string.IsNullOrWhiteSpace(_orderBy.Text) ? null : _orderBy.Text,
                    _includeHeaders.IsChecked == true,
                    SelectedExportDelimiter(),
                    SelectedExportQuote(),
                    _exportNullText.Text,
                    SelectedExportLineEnding(),
                    SelectedExportEncoding(),
                    PrettyJson: _prettyJson.IsChecked == true,
                    SqlBatchSize: int.TryParse(_sqlBatch.Text, out var batch) ? batch : 100,
                    IncludeTransaction: _includeTransaction.IsChecked == true));
            var result = await _relationExport.ExportAsync(
                _connectionString, _database, request,
                new Progress<TransferExportProgress>(value =>
                {
                    _progress.IsIndeterminate = true;
                    _progressText.Text =
                        $"{value.Phase}\r\nRows {value.RowsWritten:N0} · bytes {value.BytesWritten:N0} · "
                        + $"elapsed {value.Elapsed:g}";
                }), cancellationToken);
            _resultText.Text =
                $"{result.Status}\r\nSource: {result.Source}\r\nFormat: {result.Format}\r\n"
                + $"Output: {result.DestinationPath}\r\nRows exported: {result.RowsWritten:N0}\r\n"
                + $"Bytes written: {result.BytesWritten:N0}\r\nElapsed: {result.Elapsed:g}\r\n"
                + $"Complete source: {result.SourceComplete}\r\n"
                + string.Join("\r\n", result.Warnings);
            _history.Add(new(started, DateTimeOffset.UtcNow, "Export", result.Source,
                result.DestinationPath, result.Status, result.RowsWritten, result.RowsWritten,
                0, result.DestinationPath, result.Warnings));
        }
        else
        {
            var selected = _exportColumns.Where(column => column.Included).ToArray();
            var selection = _exportScope.SelectedIndex == 1 ? _resultSelection : null;
            var outcome = await _resultExport!.ExportAsync(new(
                _resultSet!, selection, format,
                selection is null ? ResultExportScope.EntireResult : ResultExportScope.SelectedCells,
                _path.Text,
                new(_includeHeaders.IsChecked == true,
                    SelectedExportDelimiter().ToString(),
                    SelectedExportLineEnding(),
                    _exportNullText.Text,
                    JsonArrayLayout: false,
                    TargetSchema: "public",
                    TargetTable: "exported_results",
                    RowsPerInsert: int.TryParse(_sqlBatch.Text, out var batch) ? batch : 100,
                    IncludeTransaction: _includeTransaction.IsChecked == true,
                    Encoding: SelectedExportEncoding(),
                    QuoteCharacter: SelectedExportQuote().ToString(),
                    PrettyJson: _prettyJson.IsChecked == true,
                    ColumnIndexes: selected.Select(column => column.Ordinal - 1).ToArray(),
                    HeaderNames: selected.Select(column => column.OutputName).ToArray(),
                    SourceWasTruncated: _resultSet!.WasTruncated)),
                new Progress<ResultExportProgress>(value =>
                {
                    _progress.IsIndeterminate = true;
                    _progressText.Text =
                        $"{value.Phase}\r\nRows {value.RowsWritten:N0} of {value.TotalRows:N0}";
                }), cancellationToken);
            _resultText.Text =
                $"{(outcome.Completed ? "Completed" : "Cancelled")}\r\n"
                + $"Output: {outcome.Path}\r\nRows exported: {outcome.RowsWritten:N0}\r\n"
                + $"Columns: {outcome.ColumnsWritten:N0}\r\nBytes: {outcome.BytesWritten:N0}\r\n"
                + $"Elapsed: {outcome.Duration:g}\r\nComplete source: {outcome.SourceComplete}\r\n"
                + string.Join("\r\n", outcome.Warnings ?? []);
            _history.Add(new(started, DateTimeOffset.UtcNow, "Export", "Query results",
                outcome.Path, outcome.Completed ? "Completed" : "Cancelled",
                outcome.RowsWritten, outcome.RowsWritten, 0, outcome.Path,
                outcome.Warnings ?? []));
        }
    }

    private DelimitedFormatOptions SelectedFormat()
    {
        if (!int.TryParse(_skipRows.Text, out var skip) || skip < 0)
            throw new InvalidOperationException("Rows to skip must be zero or a positive integer.");
        var delimiter = _delimiter.SelectedIndex switch
        {
            0 => ',', 1 => '\t', 2 => ';', 3 => '|',
            _ when _customDelimiter.Text.Length == 1 => _customDelimiter.Text[0],
            _ => throw new InvalidOperationException("Enter one custom delimiter character."),
        };
        var quote = _quote.SelectedIndex switch { 0 => '"', 1 => '\'', _ => (char?)null };
        if (quote == delimiter) throw new InvalidOperationException("Delimiter and quote character must differ.");
        return new(delimiter, quote, _header.IsChecked == true,
            _trim.IsChecked == true, _multiline.IsChecked == true, _nullMarker.Text,
            SkipRows: skip);
    }

    private Encoding SelectedEncoding() => _encoding.SelectedIndex switch
    {
        0 => _inspection?.Encoding ?? new UTF8Encoding(false, true),
        1 => TransferEncodingDetector.FromLabel("UTF-8"),
        2 => TransferEncodingDetector.FromLabel("UTF-8 with BOM"),
        3 => TransferEncodingDetector.FromLabel("UTF-16 little-endian"),
        4 => TransferEncodingDetector.FromLabel("UTF-16 big-endian"),
        _ => TransferEncodingDetector.FromLabel("Windows-1252"),
    };

    private IReadOnlyList<DestinationColumn> CurrentDestinationColumns()
    {
        if (_newTable.IsChecked != true) return _destinationMetadata?.Columns ?? [];
        return _mappings.Where(row => row.Included).Select(row =>
            new DestinationColumn(row.DestinationName ?? row.SourceName,
                string.IsNullOrWhiteSpace(row.DestinationType) ? "text" : row.DestinationType,
                true)).ToArray();
    }

    private IReadOnlyList<ColumnMapping> CurrentMappings() => _mappings.Select(row =>
        new ColumnMapping(row.SourceOrdinal, row.DestinationName,
            row.Included && !string.IsNullOrWhiteSpace(row.DestinationName))).ToArray();

    private ResultExportFormat SelectedExportFormat() => _exportFormat.SelectedIndex switch
    {
        1 => ResultExportFormat.Tsv,
        2 => ResultExportFormat.Json,
        3 => ResultExportFormat.JsonLines,
        4 => ResultExportFormat.SqlInsert,
        _ => ResultExportFormat.Csv,
    };

    private char SelectedExportDelimiter() => _exportDelimiter.SelectedIndex switch
    {
        0 => ',', 1 => '\t', 2 => ';', 3 => '|',
        _ when _exportCustomDelimiter.Text.Length == 1 => _exportCustomDelimiter.Text[0],
        _ => throw new InvalidOperationException("Enter one custom export delimiter character."),
    };

    private char SelectedExportQuote() => _exportQuote.SelectedIndex == 1 ? '\'' : '"';

    private string SelectedExportLineEnding() =>
        _exportLineEnding.SelectedIndex == 1 ? "\n" : "\r\n";

    private Encoding SelectedExportEncoding() => _exportEncoding.SelectedIndex switch
    {
        1 => new UTF8Encoding(true),
        2 => new UnicodeEncoding(false, true, true),
        _ => new UTF8Encoding(false),
    };

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static IReadOnlyList<string>? SplitValues(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private string ExportSourceSummary()
    {
        if (_relationSource is not null)
            return $"{_relationSource.ObjectType} {_relationSource.QualifiedName}; all rows are read from PostgreSQL.";
        if (_resultSet is null) return "No query result is available.";
        return _resultSet.WasTruncated
            ? $"Query result: {_resultSet.LoadedRowCount:N0} retained of {_resultSet.ReceivedRowCount:N0} received rows. Export is incomplete."
            : $"Query result: {_resultSet.LoadedRowCount:N0} retained rows; status {_resultSet.Status}.";
    }

    private void Browse()
    {
        if (_mode == DataTransferWorkspaceMode.Import)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Delimited files (*.csv;*.tsv;*.txt)|*.csv;*.tsv;*.txt|All files (*.*)|*.*",
            };
            if (dialog.ShowDialog(this) == true) _path.Text = dialog.FileName;
        }
        else
        {
            var dialog = new SaveFileDialog
            {
                Filter = "CSV (*.csv)|*.csv|TSV (*.tsv)|*.tsv|JSON (*.json)|*.json|JSON Lines (*.jsonl)|*.jsonl|SQL (*.sql)|*.sql",
                DefaultExt = ".csv",
                AddExtension = true,
                FileName = _relationSource?.Name ?? "query-results",
            };
            if (dialog.ShowDialog(this) == true) _path.Text = dialog.FileName;
        }
    }

    private void CancelOrClose()
    {
        if (_executing)
        {
            _progressText.Text += "\r\nCancellation requested…";
            _cancellation?.Cancel();
        }
        else Close();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key == Key.Escape && !_executing)
        {
            args.Handled = true;
            Close();
        }
    }

    private void OnClosing(object? sender, CancelEventArgs args)
    {
        if (!_executing) return;
        args.Cancel = true;
        _cancellation?.Cancel();
        _progressText.Text += "\r\nCancellation requested before closing.";
    }

    private bool Invalid(string message)
    {
        _validation.Text = message;
        return false;
    }

    private static string ProposedTableName(string path)
    {
        var raw = Path.GetFileNameWithoutExtension(path);
        var cleaned = new string(raw.Select(character =>
            char.IsLetterOrDigit(character) || character == '_' ? character : '_').ToArray()).Trim('_');
        if (string.IsNullOrWhiteSpace(cleaned)) cleaned = "imported_data";
        if (char.IsDigit(cleaned[0])) cleaned = "data_" + cleaned;
        while (Encoding.UTF8.GetByteCount(cleaned) > 63) cleaned = cleaned[..^1];
        return cleaned;
    }

    private static string Display(char? value) => value switch
    {
        null => "none", '\t' => "TAB", '\r' => "CR", '\n' => "LF", _ => $"'{value}'",
    };

    private static DataGrid CreateGrid(string name)
    {
        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            CanUserReorderColumns = true,
            SelectionMode = DataGridSelectionMode.Extended,
            SelectionUnit = DataGridSelectionUnit.CellOrRowHeader,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        AutomationProperties.SetName(grid, name);
        return grid;
    }

    private static TextBox ReadOnlyMultiline() => new()
    {
        IsReadOnly = true,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.NoWrap,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
    };

    private static TextBlock Label(string text) => new()
        { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };

    private static StackPanel Horizontal(params UIElement[] children)
    {
        var panel = new StackPanel
            { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        foreach (var child in children)
        {
            if (child is FrameworkElement element) element.Margin = new Thickness(0, 0, 8, 0);
            panel.Children.Add(child);
        }
        return panel;
    }

    private static StackPanel Vertical(params UIElement[] children)
    {
        var panel = new StackPanel();
        foreach (var child in children)
        {
            if (child is FrameworkElement element) element.Margin = new Thickness(0, 0, 0, 8);
            panel.Children.Add(child);
        }
        return panel;
    }

    private static UIElement GridPage(UIElement content, string note)
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        var text = new TextBlock { Text = note, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        grid.Children.Add(text);
        Grid.SetRow(content, 1);
        grid.Children.Add(content);
        return grid;
    }

    private sealed record PreviewRow(long SourceRow, string Status, IReadOnlyList<string> Values);

    private sealed class WizardMappingRow(
        int sourceOrdinal,
        string sourceName,
        string sample,
        string? destinationName,
        string destinationType,
        bool included,
        string status) : INotifyPropertyChanged
    {
        private string? _destinationName = destinationName;
        private bool _included = included;
        public int SourceOrdinal { get; } = sourceOrdinal;
        public string SourceName { get; } = sourceName;
        public string Sample { get; } = sample;
        public string DestinationType { get; set; } = destinationType;
        public string Status { get; set; } = status;
        public bool TrimWhitespace { get; set; }
        public bool EmptyBecomesNull { get; set; }
        public string? DateTimeFormat { get; set; }
        public string DecimalSeparator { get; set; } = ".";
        public string? ThousandsSeparator { get; set; }
        public string? TrueValues { get; set; } = "true,t,1,yes";
        public string? FalseValues { get; set; } = "false,f,0,no";
        public string? TimeZoneAssumption { get; set; }
        public bool StripCurrencySymbol { get; set; }
        public bool ParenthesesAreNegative { get; set; }
        public bool SubstituteInvalidWithNull { get; set; }
        public string? DestinationName
        {
            get => _destinationName;
            set { _destinationName = value; Changed(); }
        }
        public bool Included
        {
            get => _included;
            set { _included = value; Changed(); }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
        private void Changed([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new(name));
    }

    private sealed class ExportColumnRow(
        int ordinal, string sourceName, string outputName, string dataType)
    {
        public int Ordinal { get; } = ordinal;
        public string SourceName { get; } = sourceName;
        public string OutputName { get; set; } = outputName;
        public string DataType { get; } = dataType;
        public bool Included { get; set; } = true;
    }
}
