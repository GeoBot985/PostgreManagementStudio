using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using PostgreManagementStudio.Application;
using PostgreManagementStudio.Core;

namespace PostgreManagementStudio.Desktop;

public partial class App : System.Windows.Application
{
    private ServiceProvider? _services;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            var settingsStore = new JsonApplicationSettingsStore(ProductionServices.DefaultSettingsPath);
            var loaded = await settingsStore.LoadAsync();
            _services = ProductionServices.Build(ProductionServices.DefaultSettingsPath, loaded.Settings);
            var window = _services.GetRequiredService<MainWindow>();
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"PostgreManagementStudio could not start.\n\n{SecretRedactor.Redact(ex.Message)}",
                "Startup failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        base.OnExit(e);
    }
}
