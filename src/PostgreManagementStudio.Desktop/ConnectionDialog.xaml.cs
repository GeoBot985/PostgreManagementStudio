using System.Data.Common;
using System.Windows;
using System.Windows.Controls;
using PostgreManagementStudio.Application;
using PostgreManagementStudio.Core;
using PostgreManagementStudio.Postgres;

namespace PostgreManagementStudio.Desktop;

public partial class ConnectionDialog : Window
{
    private readonly IConnectionProbe _probe;

    public ConnectionDialog(IConnectionProbe probe, ShellConnectionInfo? current = null)
    {
        InitializeComponent();
        _probe = probe;
        if (current is null) return;
        HostText.Text = current.Host;
        PortText.Text = current.Port.ToString();
        DatabaseText.Text = current.Database;
        UsernameText.Text = current.Username;
        SelectSslMode(current.SslMode);
    }

    public ShellConnectionInfo? Connection { get; private set; }

    private async void Test_Click(object sender, RoutedEventArgs e) => await ValidateAsync(closeOnSuccess: false);
    private async void Connect_Click(object sender, RoutedEventArgs e) => await ValidateAsync(closeOnSuccess: true);

    private async Task ValidateAsync(bool closeOnSuccess)
    {
        SetBusy(true);
        try
        {
            var (connection, configuration) = BuildConnection();
            StatusText.Text = "Connecting…";
            var result = await _probe.TestAsync(configuration);
            if (!result.Succeeded)
            {
                StatusText.Text = result.Message;
                return;
            }

            Connection = connection with
            {
                Database = result.Database ?? connection.Database,
                Username = result.Username ?? connection.Username,
                ServerVersion = result.ServerVersion,
                IsEncrypted = result.IsEncrypted,
            };
            StatusText.Text = $"Connected to {Connection.Host}:{Connection.Port}/{Connection.Database} as {Connection.Username} in {result.Elapsed.TotalMilliseconds:N0} ms.";
            if (closeOnSuccess) DialogResult = true;
        }
        catch (Exception ex)
        {
            StatusText.Text = SecretRedactor.Redact(ex.Message);
        }
        finally
        {
            if (IsVisible) SetBusy(false);
        }
    }

    private (ShellConnectionInfo Connection, EffectiveConnectionConfiguration Configuration) BuildConnection()
    {
        if (!int.TryParse(PortText.Text, out var port)) throw new InvalidOperationException("Port must be a number.");
        var sslMode = Enum.Parse<Npgsql.SslMode>(((ComboBoxItem)SslModeBox.SelectedItem).Content.ToString()!);
        var profile = new ConnectionProfile
        {
            Id = "interactive",
            Name = "Interactive connection",
            Host = HostText.Text.Trim(),
            Port = port,
            Database = DatabaseText.Text.Trim(),
            Username = UsernameText.Text.Trim(),
            Password = PasswordText.Password,
            AuthenticationMode = string.IsNullOrEmpty(PasswordText.Password)
                ? ConnectionAuthenticationMode.Integrated
                : ConnectionAuthenticationMode.Password,
            SslMode = sslMode,
        };
        var configuration = EffectiveConnectionConfigurationBuilder.Build(profile);
        var builder = new DbConnectionStringBuilder
        {
            ["Host"] = profile.Host,
            ["Port"] = profile.Port,
            ["Database"] = profile.Database,
            ["Username"] = profile.Username,
            ["SSL Mode"] = profile.SslMode.ToString(),
            ["Pooling"] = true,
            ["Application Name"] = "PostgreManagementStudio",
        };
        if (!string.IsNullOrEmpty(profile.Password)) builder["Password"] = profile.Password;
        return (new(builder.ConnectionString, profile.Host, profile.Port, profile.Database, profile.Username,
            profile.SslMode, null, null, false), configuration);
    }

    private void SetBusy(bool busy)
    {
        TestButton.IsEnabled = !busy;
        ConnectButton.IsEnabled = !busy;
    }

    private void SelectSslMode(Npgsql.SslMode value)
    {
        foreach (ComboBoxItem item in SslModeBox.Items)
            if (string.Equals(item.Content?.ToString(), value.ToString(), StringComparison.Ordinal))
            {
                SslModeBox.SelectedItem = item;
                return;
            }
    }
}

public sealed record ShellConnectionInfo(
    string ConnectionString,
    string Host,
    int Port,
    string Database,
    string Username,
    Npgsql.SslMode SslMode,
    string? ServerVersion,
    bool? IsEncrypted,
    bool IsDevelopmentFallback)
{
    public string SafeDisplayName => $"{Username}@{Host}:{Port}";
}
