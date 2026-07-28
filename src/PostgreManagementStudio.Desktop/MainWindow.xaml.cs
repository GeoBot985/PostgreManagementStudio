using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

using PostgreManagementStudio.Application;
using PostgreManagementStudio.Postgres;

namespace PostgreManagementStudio.Desktop;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private async void CheckVersion_Click(object sender, RoutedEventArgs e)
    {
        var connectionString = Environment.GetEnvironmentVariable("PMS_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString)) { ResultText.Text = "Set PMS_CONNECTION_STRING first."; return; }
        try { ResultText.Text = await new PostgresVersionService(new NpgsqlPostgresVersionQuery()).GetVersionAsync(connectionString); }
        catch (OperationCanceledException) { ResultText.Text = "Operation cancelled."; }
        catch (Exception ex) { ResultText.Text = $"PostgreSQL error: {ex.Message}"; }
    }
}
