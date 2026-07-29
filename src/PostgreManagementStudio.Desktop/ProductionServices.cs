using Microsoft.Extensions.DependencyInjection;
using System.IO;
using PostgreManagementStudio.Application;
using PostgreManagementStudio.Core;
using PostgreManagementStudio.Postgres;
using PostgreManagementStudio.Results;

namespace PostgreManagementStudio.Desktop;

public static class ProductionServices
{
    public static ServiceProvider Build(string settingsPath, ApplicationSettings? applicationSettings = null)
    {
        var settings = (applicationSettings ?? new ApplicationSettings()).Validate();
        var fullSettingsPath = Path.GetFullPath(settingsPath);
        var settingsDirectory = Path.GetDirectoryName(fullSettingsPath)
            ?? throw new ArgumentException("Settings must have a parent directory.", nameof(settingsPath));
        var stateDirectory = string.Equals(
            fullSettingsPath,
            Path.GetFullPath(DefaultSettingsPath),
            StringComparison.OrdinalIgnoreCase)
            ? settingsDirectory
            : Path.Combine(
                settingsDirectory,
                Path.GetFileNameWithoutExtension(fullSettingsPath) + ".state");
        var services = new ServiceCollection();
        services.AddSingleton<INpgsqlConnectionFactory>(NpgsqlConnectionFactory.Shared);
        services.AddSingleton<IConnectionDiagnostics, DiagnosticConnectionDiagnostics>();
        services.AddSingleton<IConnectionRecoveryDiagnostics, DiagnosticConnectionRecoveryDiagnostics>();
        services.AddSingleton<IPerformanceDiagnostics, TracePerformanceDiagnostics>();
        services.AddSingleton<IConnectionProbe, NpgsqlConnectionProbe>();
        services.AddSingleton<IConnectionPoolInvalidator, NpgsqlConnectionPoolInvalidator>();
        services.AddSingleton<ConnectionProfileRegistry>();
        services.AddSingleton<IProtectedCredentialStore, WindowsCredentialStore>();
        services.AddSingleton<CredentialLifecycleService>();
        services.AddSingleton<IConnectionProfileStore>(sp => new JsonConnectionProfileStore(
            Path.Combine(stateDirectory, "connections.json"),
            sp.GetRequiredService<CredentialLifecycleService>()));
        services.AddSingleton(new RecoverySnapshotService(
            Path.Combine(stateDirectory, "recovery")));
        services.AddSingleton<IQueryExecutor>(sp => new NpgsqlQueryExecutor(sp.GetRequiredService<INpgsqlConnectionFactory>()));
        services.AddSingleton<IPostgresVersionQuery>(sp => new NpgsqlPostgresVersionQuery(sp.GetRequiredService<INpgsqlConnectionFactory>()));
        services.AddSingleton<NpgsqlMetadataProvider>(sp => new(sp.GetRequiredService<INpgsqlConnectionFactory>()));
        services.AddSingleton<IPostgresMetadataProvider>(sp => sp.GetRequiredService<NpgsqlMetadataProvider>());
        services.AddSingleton<IPostgresObjectMetadataProvider>(sp => sp.GetRequiredService<NpgsqlMetadataProvider>());
        services.AddSingleton<IMetadataDiagnostics, DiagnosticMetadataDiagnostics>();
        services.AddSingleton<BoundedMetadataCache>();
        services.AddSingleton<HardenedMetadataService>();
        services.AddSingleton<IApplicationSettingsStore>(new JsonApplicationSettingsStore(settingsPath));
        services.AddSingleton(settings);
        services.AddSingleton<IQueryExecutionTelemetry, DiagnosticQueryExecutionTelemetry>();
        services.AddSingleton<IUserConfirmationService, WpfUserConfirmationService>();
        services.AddSingleton<DestructiveOperationGuard>();
        services.AddSingleton<IExternalProcessRunner, ExternalProcessRunner>();
        services.AddSingleton<PostgreSqlToolLocator>();
        services.AddSingleton<PostgreSqlToolDiscoveryService>();
        services.AddSingleton<BackupInspectionService>();
        services.AddSingleton<BackupOperationLockManager>();
        services.AddSingleton<IBackupRestoreConnectionValidator, NpgsqlBackupRestoreConnectionValidator>();
        services.AddSingleton<IBackupRestoreDiagnostics, DiagnosticBackupRestoreDiagnostics>();
        services.AddSingleton<BackupRestoreOperationService>();
        services.AddSingleton<TransferHistoryService>();
        services.AddSingleton(sp => new ResultExecutionService(
            sp.GetRequiredService<IQueryExecutor>(),
            new ResultStorageOptions(
                maximumSessionMemoryBytes: 128L * 1024 * 1024,
                maximumResultSetMemoryBytes: 64L * 1024 * 1024,
                maximumRowsPerResultSet: settings.DisplayedRowLimit)));
        services.AddSingleton<QueryTabManager>();
        services.AddTransient<PostgresVersionService>();
        services.AddTransient<ObjectExplorerService>(sp =>
            new(sp.GetRequiredService<HardenedMetadataService>()));
        services.AddTransient<DocumentFileService>();
        services.AddTransient<FindReplaceService>();
        services.AddTransient<ResultExportService>();
        services.AddTransient<IResultExportService, ResultExportService>();
        services.AddTransient<ResultViewTransformationService>();
        services.AddTransient<NpgsqlActivityService>();
        services.AddTransient<NpgsqlDataTransferService>();
        services.AddTransient<NpgsqlExecutionPlanService>();
        services.AddTransient<NpgsqlMaintenanceService>();
        services.AddTransient<NpgsqlObjectSearchService>();
        services.AddTransient<NpgsqlSchemaModelExtractor>();
        services.AddTransient<NpgsqlIndexAnalysisService>();
        services.AddTransient<NpgsqlSecurityService>();
        services.AddTransient<NpgsqlSessionManagementService>();
        services.AddTransient<MainWindow>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    public static string DefaultSettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PostgreManagementStudio",
        "settings.json");

    public static string DefaultConnectionProfilesPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PostgreManagementStudio",
        "connections.json");
}
