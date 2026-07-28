using Microsoft.Extensions.DependencyInjection;
using System.IO;
using PostgreManagementStudio.Application;
using PostgreManagementStudio.Core;
using PostgreManagementStudio.Postgres;
using PostgreManagementStudio.Results;

namespace PostgreManagementStudio.Desktop;

public static class ProductionServices
{
    public static ServiceProvider Build(string settingsPath)
    {
        var services = new ServiceCollection();
        services.AddSingleton<INpgsqlConnectionFactory>(NpgsqlConnectionFactory.Shared);
        services.AddSingleton<IQueryExecutor>(sp => new NpgsqlQueryExecutor(sp.GetRequiredService<INpgsqlConnectionFactory>()));
        services.AddSingleton<IPostgresVersionQuery>(sp => new NpgsqlPostgresVersionQuery(sp.GetRequiredService<INpgsqlConnectionFactory>()));
        services.AddSingleton<IPostgresMetadataProvider>(sp => new NpgsqlMetadataProvider(sp.GetRequiredService<INpgsqlConnectionFactory>()));
        services.AddSingleton<IApplicationSettingsStore>(new JsonApplicationSettingsStore(settingsPath));
        services.AddSingleton<IUserConfirmationService, WpfUserConfirmationService>();
        services.AddSingleton<DestructiveOperationGuard>();
        services.AddSingleton<ResultExecutionService>();
        services.AddSingleton<QueryTabManager>();
        services.AddTransient<PostgresVersionService>();
        services.AddTransient<ObjectExplorerService>();
        services.AddTransient<DocumentFileService>();
        services.AddTransient<FindReplaceService>();
        services.AddTransient<ResultExportService>();
        services.AddTransient<ResultViewTransformationService>();
        services.AddTransient<NpgsqlActivityService>();
        services.AddTransient<NpgsqlDataTransferService>();
        services.AddTransient<NpgsqlExecutionPlanService>();
        services.AddTransient<NpgsqlMaintenanceService>();
        services.AddTransient<NpgsqlObjectSearchService>();
        services.AddTransient<NpgsqlSchemaModelExtractor>();
        services.AddTransient<NpgsqlSecurityService>();
        services.AddTransient<NpgsqlSessionManagementService>();
        services.AddTransient<MainWindow>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    public static string DefaultSettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PostgreManagementStudio",
        "settings.json");
}
