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
    private readonly IConnectionRecoveryDiagnostics _diagnostics;
    private ConnectionRecoverySession? _pendingSession;
    private bool _accepted;

    public ConnectionDialog(
        IConnectionProbe probe,
        IConnectionRecoveryDiagnostics diagnostics,
        ShellConnectionInfo? current = null)
    {
        InitializeComponent();
        _probe = probe;
        _diagnostics = diagnostics;
        Closed += ConnectionDialog_Closed;
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
            if (_pendingSession is not null) await _pendingSession.DisposeAsync();
            _pendingSession = new ConnectionRecoverySession(_probe, _diagnostics);
            var snapshot = await _pendingSession.ConnectAsync(configuration);
            if (snapshot.State != RecoveryConnectionState.Connected)
            {
                StatusText.Text = snapshot.Failure?.Message ?? "The connection could not be established.";
                return;
            }

            Connection = connection with
            {
                Database = _pendingSession.Configuration?.Profile.Database ?? connection.Database,
                Username = _pendingSession.Configuration?.Profile.Username ?? connection.Username,
                Configuration = configuration,
                Session = _pendingSession,
            };
            StatusText.Text = $"Connected to {Connection.Host}:{Connection.Port}/{Connection.Database} as {Connection.Username}. Backend PID {snapshot.BackendProcessId?.ToString() ?? "unavailable"}.";
            if (closeOnSuccess) { _accepted = true; DialogResult = true; }
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
            profile.SslMode, null, null, false, configuration, null!), configuration);
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

    private async void ConnectionDialog_Closed(object? sender, EventArgs e)
    {
        if (!_accepted && _pendingSession is not null) await _pendingSession.DisposeAsync();
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
    bool IsDevelopmentFallback,
    EffectiveConnectionConfiguration Configuration,
    ConnectionRecoverySession Session)
{
    public string SafeDisplayName => $"{Username}@{Host}:{Port}";
}
