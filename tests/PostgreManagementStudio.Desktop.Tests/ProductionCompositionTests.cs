using Microsoft.Extensions.DependencyInjection;
using System.IO;
using System.Windows.Threading;
using PostgreManagementStudio.Application;
using PostgreManagementStudio.Core;
using PostgreManagementStudio.Postgres;

namespace PostgreManagementStudio.Desktop.Tests;

public sealed class ProductionCompositionTests
{
    [Fact]
    [Trait("Category", "Smoke")]
    [Trait("Priority", "P0")]
    public void ProductionProvider_ValidatesAndResolvesRealServices()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pms-composition-{Guid.NewGuid():N}.json");
        using var provider = ProductionServices.Build(path);

        Assert.IsType<NpgsqlConnectionFactory>(provider.GetRequiredService<INpgsqlConnectionFactory>());
        Assert.IsType<NpgsqlQueryExecutor>(provider.GetRequiredService<IQueryExecutor>());
        Assert.IsType<NpgsqlMetadataProvider>(provider.GetRequiredService<IPostgresMetadataProvider>());
        Assert.IsType<JsonApplicationSettingsStore>(provider.GetRequiredService<IApplicationSettingsStore>());
        Assert.NotNull(provider.GetRequiredService<QueryTabManager>());
        Assert.NotNull(provider.GetRequiredService<ObjectExplorerService>());
        Assert.NotNull(provider.GetRequiredService<DestructiveOperationGuard>());
        Assert.IsType<WpfUserConfirmationService>(provider.GetRequiredService<IUserConfirmationService>());
        Assert.NotNull(provider.GetRequiredService<RecoverySnapshotService>());
        Assert.NotNull(provider.GetRequiredService<NpgsqlActivityService>());
        Assert.NotNull(provider.GetRequiredService<NpgsqlExecutionPlanService>());
        Assert.DoesNotContain(
            provider.GetServices<IQueryExecutor>(),
            service => service.GetType().Name.Contains("Fake", StringComparison.OrdinalIgnoreCase) ||
                       service.GetType().Name.Contains("Mock", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    [Trait("Category", "Component")]
    [Trait("Priority", "P0")]
    public void AlternateSettingsPath_IsolatesProfilesAndRecoveryFromUserState()
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"pms-isolation-{Guid.NewGuid():N}.json");
        using var provider = ProductionServices.Build(settingsPath);

        var profileStore = Assert.IsType<JsonConnectionProfileStore>(
            provider.GetRequiredService<IConnectionProfileStore>());
        var expectedStateDirectory = Path.Combine(
            Path.GetDirectoryName(settingsPath)!,
            Path.GetFileNameWithoutExtension(settingsPath) + ".state");
        Assert.Equal(
            Path.Combine(expectedStateDirectory, "connections.json"),
            profileStore.Path,
            ignoreCase: true);
        Assert.NotEqual(
            Path.GetFullPath(ProductionServices.DefaultConnectionProfilesPath),
            profileStore.Path);
    }

    [Fact]
    [Trait("Category", "UiIntegration")]
    [Trait("Priority", "P0")]
    public void ApplicationShell_CanBeCreatedAndClosedOnStaThread()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var provider = ProductionServices.Build(Path.Combine(Path.GetTempPath(), $"pms-shell-{Guid.NewGuid():N}.json"));
                var window = provider.GetRequiredService<MainWindow>();
                Assert.NotNull(window.Content);
                Assert.Single(provider.GetRequiredService<QueryTabManager>().Documents);
                window.Close();
            }
            catch (Exception ex) { failure = ex; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Application shell creation timed out.");
        Assert.Null(failure);
    }
}
