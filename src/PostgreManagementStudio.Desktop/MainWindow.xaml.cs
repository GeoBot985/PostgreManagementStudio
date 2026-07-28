using System.Text;
using System.Windows;
using PostgreManagementStudio.Application;
using PostgreManagementStudio.Core;
using PostgreManagementStudio.Postgres;

namespace PostgreManagementStudio.Desktop;

public partial class MainWindow : Window
{
    private CancellationTokenSource? _executionCancellation;
    private readonly QueryExecutionService _service = new(new NpgsqlQueryExecutor());

    public MainWindow() => InitializeComponent();

    private async void Execute_Click(object sender, RoutedEventArgs e)
    {
        var connectionString = Environment.GetEnvironmentVariable("PMS_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString)) { StatusText.Text = "Set PMS_CONNECTION_STRING first."; return; }
        _executionCancellation = new CancellationTokenSource(); ExecuteButton(false); var output = new StringBuilder();
        try
        {
            await foreach (var item in _service.ExecuteAsync(new QueryRequest(SqlText.Text, connectionString), _executionCancellation.Token))
            { output.AppendLine(item switch { RowBatchReceived b => $"Rows {b.Batch.StartRowIndex}..{b.Batch.StartRowIndex + b.Batch.Rows.Count - 1} ({b.Batch.Rows.Count})", ResultSetStarted s => $"Result set {s.ResultSetIndex}: {s.Schema.Columns.Count} columns", DatabaseNoticeReceived n => $"NOTICE [{n.Notice.Severity}]: {n.Notice.Message}", ExecutionFailed f => $"ERROR [{f.Error.SqlState}]: {f.Error.Message}", ExecutionCancelled => "CANCELLED", ExecutionCompleted c => $"Completed in {c.Elapsed.TotalMilliseconds:N0} ms", _ => item.GetType().Name }); ResultText.Text = output.ToString(); }
            StatusText.Text = "Finished.";
        }
        catch (OperationCanceledException) { StatusText.Text = "Cancelled."; }
        catch (Exception ex) { StatusText.Text = $"Unexpected error: {ex.Message}"; }
        finally { _executionCancellation.Dispose(); _executionCancellation = null; ExecuteButton(true); }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _executionCancellation?.Cancel();
    private void ExecuteButton(bool enabled) { ((System.Windows.Controls.Button)FindName("Execute"))?.SetValue(IsEnabledProperty, enabled); }
}
