using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
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
    private readonly IConnectionProfileStore _profiles;
    private readonly CredentialLifecycleService _credentials;
    private ConnectionProfile? _persistedProfile;
    private ConnectionRecoverySession? _pendingSession;
    private bool _accepted;

    public ConnectionDialog(
        IConnectionProbe probe,
        IConnectionRecoveryDiagnostics diagnostics,
        IConnectionProfileStore profiles,
        CredentialLifecycleService credentials,
        ShellConnectionInfo? current = null)
    {
        InitializeComponent();
        _probe = probe;
        _diagnostics = diagnostics;
        _profiles = profiles;
        _credentials = credentials;
        Closed += ConnectionDialog_Closed;
        Loaded += ConnectionDialog_Loaded;
        if (current is not null) ApplyProfile(current.Configuration.Profile);
    }

    public ShellConnectionInfo? Connection { get; private set; }

    private async void Test_Click(object sender, RoutedEventArgs e) => await ValidateAsync(closeOnSuccess: false);
    private async void Connect_Click(object sender, RoutedEventArgs e) => await ValidateAsync(closeOnSuccess: true);

    private async Task ValidateAsync(bool closeOnSuccess)
    {
        SetBusy(true);
        try
        {
            var (connection, configuration, profile) = await BuildConnectionAsync();
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
            if (SaveProfileCheck.IsChecked == true)
            {
                var credentialReference = profile.CredentialReference;
                if (SavePasswordCheck.IsChecked == true && !string.IsNullOrEmpty(PasswordText.Password))
                {
                    var chars = PasswordText.Password.ToCharArray();
                    try { credentialReference = await _credentials.SaveAsync(profile.Id, chars); }
                    finally { CryptographicOperations.ZeroMemory(System.Runtime.InteropServices.MemoryMarshal.AsBytes(chars.AsSpan())); }
                }
                profile = profile with { CredentialReference = credentialReference };
                await _profiles.SaveAsync(profile);
                Connection = Connection with { Configuration = EffectiveConnectionConfigurationBuilder.Build(profile) };
                _persistedProfile = profile;
                DeletePasswordButton.IsEnabled = !string.IsNullOrWhiteSpace(credentialReference);
            }
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

    private async Task<(ShellConnectionInfo Connection, EffectiveConnectionConfiguration Configuration, ConnectionProfile Profile)> BuildConnectionAsync()
    {
        if (!int.TryParse(PortText.Text, out var port)) throw new InvalidOperationException("Port must be a number.");
        var sslMode = Enum.Parse<Npgsql.SslMode>(((ComboBoxItem)SslModeBox.SelectedItem).Content.ToString()!);
        var password = PasswordText.Password;
        if (string.IsNullOrEmpty(password) && !string.IsNullOrWhiteSpace(_persistedProfile?.CredentialReference))
        {
            var stored = await _credentials.RetrieveAsync(_persistedProfile.CredentialReference);
            if (stored is not null)
                try { password = new string(stored); }
                finally { CryptographicOperations.ZeroMemory(System.Runtime.InteropServices.MemoryMarshal.AsBytes(stored.AsSpan())); }
        }
        var profileId = _persistedProfile?.Id ?? StableProfileId(HostText.Text, port, DatabaseText.Text, UsernameText.Text);
        var environment = Enum.Parse<EnvironmentClassification>(((ComboBoxItem)EnvironmentBox.SelectedItem).Content.ToString()!);
        var profile = new ConnectionProfile
        {
            Id = profileId,
            Name = _persistedProfile?.Name ?? $"{UsernameText.Text.Trim()}@{HostText.Text.Trim()}/{DatabaseText.Text.Trim()}",
            Host = HostText.Text.Trim(),
            Port = port,
            Database = DatabaseText.Text.Trim(),
            Username = UsernameText.Text.Trim(),
            Password = password,
            CredentialReference = _persistedProfile?.CredentialReference,
            AuthenticationMode = string.IsNullOrEmpty(password)
                ? ConnectionAuthenticationMode.Integrated
                : ConnectionAuthenticationMode.Password,
            SslMode = sslMode,
            Environment = environment,
            CustomEnvironmentName = environment == EnvironmentClassification.Custom ? CustomEnvironmentText.Text.Trim() : null,
            IsReadOnly = ReadOnlyCheck.IsChecked == true,
        };
        var configuration = EffectiveConnectionConfigurationBuilder.Build(profile);
        var builder = new Npgsql.NpgsqlConnectionStringBuilder
        {
            Host = profile.Host,
            Port = profile.Port,
            Database = profile.Database,
            Username = profile.Username,
            SslMode = profile.SslMode,
            Pooling = true,
            ApplicationName = "PostgreManagementStudio",
            NoResetOnClose = false,
            IncludeErrorDetail = false,
        };
        if (!string.IsNullOrEmpty(profile.Password)) builder.Password = profile.Password;
        return (new(builder.ConnectionString, profile.Host, profile.Port, profile.Database, profile.Username,
            profile.SslMode, null, null, false, configuration, null!), configuration, profile);
    }

    private async void ConnectionDialog_Loaded(object sender, RoutedEventArgs e)
    {
        if (_persistedProfile is not null) return;
        var profile = (await _profiles.LoadAsync()).FirstOrDefault();
        if (profile is not null) ApplyProfile(profile);
    }

    private void ApplyProfile(ConnectionProfile profile)
    {
        _persistedProfile = profile;
        HostText.Text = profile.Host;
        PortText.Text = profile.Port.ToString();
        DatabaseText.Text = profile.Database;
        UsernameText.Text = profile.Username;
        SelectSslMode(profile.SslMode);
        SelectEnvironment(profile.Environment);
        ReadOnlyCheck.IsChecked = profile.IsReadOnly;
        CustomEnvironmentText.Text = profile.CustomEnvironmentName ?? "";
        SaveProfileCheck.IsChecked = true;
        DeletePasswordButton.IsEnabled = !string.IsNullOrWhiteSpace(profile.CredentialReference);
        StatusText.Text = string.IsNullOrWhiteSpace(profile.CredentialReference)
            ? "Saved profile loaded. No password is stored."
            : "Saved profile loaded. The protected password is not displayed.";
    }

    private void SelectEnvironment(EnvironmentClassification value)
    {
        foreach (ComboBoxItem item in EnvironmentBox.Items)
            if (string.Equals(item.Content?.ToString(), value.ToString(), StringComparison.Ordinal))
            {
                EnvironmentBox.SelectedItem = item;
                return;
            }
    }

    private void SaveProfile_Changed(object sender, RoutedEventArgs e) =>
        SavePasswordCheck.IsEnabled = SaveProfileCheck.IsChecked == true;

    private void Environment_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (CustomEnvironmentText is null) return;
        CustomEnvironmentText.IsEnabled = EnvironmentBox.SelectedItem is ComboBoxItem item
            && string.Equals(item.Content?.ToString(), nameof(EnvironmentClassification.Custom), StringComparison.Ordinal);
    }

    private async void DeletePassword_Click(object sender, RoutedEventArgs e)
    {
        if (_persistedProfile is null || string.IsNullOrWhiteSpace(_persistedProfile.CredentialReference)) return;
        await _credentials.DeleteAsync(_persistedProfile.CredentialReference);
        _persistedProfile = _persistedProfile with { CredentialReference = null };
        await _profiles.SaveAsync(_persistedProfile);
        DeletePasswordButton.IsEnabled = false;
        SavePasswordCheck.IsChecked = false;
        StatusText.Text = "The saved password was deleted from Windows Credential Manager.";
    }

    private static string StableProfileId(string host, int port, string database, string username)
    {
        var value = $"{host.Trim().ToUpperInvariant()}:{port}/{database.Trim().ToUpperInvariant()}:{username.Trim().ToUpperInvariant()}";
        return "profile:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
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
    [property: DebuggerBrowsable(DebuggerBrowsableState.Never)] string ConnectionString,
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
    public override string ToString() => $"{Configuration.Profile.Name} ({SafeDisplayName}/{Database})";
}
