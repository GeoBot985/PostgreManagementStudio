using System.Text.Json;
using PostgreManagementStudio.Application;

namespace PostgreManagementStudio.Core.Tests;

public sealed class ApplicationSettingsTests
{
    [Fact]
    [Trait("Category", "Component")]
    [Trait("Priority", "P0")]
    public async Task Settings_MissingSaveReloadAndUnknownValues_PreserveSafeState()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PMS Settings Tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "settings.json");
        try
        {
            var store = new JsonApplicationSettingsStore(path);
            var missing = await store.LoadAsync();
            Assert.Equal("postgres", missing.Settings.DefaultDatabase);

            using var extraDocument = JsonDocument.Parse("\"preserved\"");
            var settings = new ApplicationSettings
            {
                DefaultDatabase = " regression ",
                CommandTimeoutSeconds = -5,
                CancellationTimeoutSeconds = 999,
                DisplayedRowLimit = 1,
                ResultWarningThreshold = 2_000_000,
                CellDisplayLimit = 1,
                RecentFileLimit = 999,
                AdditionalValues = new() { ["futureSetting"] = extraDocument.RootElement.Clone() },
            };
            await store.SaveAsync(settings);
            var reloaded = await store.LoadAsync();

            Assert.Equal("regression", reloaded.Settings.DefaultDatabase);
            Assert.Equal(1, reloaded.Settings.CommandTimeoutSeconds);
            Assert.Equal(60, reloaded.Settings.CancellationTimeoutSeconds);
            Assert.Equal(100, reloaded.Settings.DisplayedRowLimit);
            Assert.Equal(1_000_000, reloaded.Settings.ResultWarningThreshold);
            Assert.Equal(32, reloaded.Settings.CellDisplayLimit);
            Assert.Equal(200, reloaded.Settings.RecentFileLimit);
            Assert.Equal("preserved", reloaded.Settings.AdditionalValues["futureSetting"].GetString());
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    [Trait("Category", "Component")]
    [Trait("Priority", "P0")]
    public async Task Settings_CorruptOrOlderConfiguration_RecoversAndReportsMigration()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pms-settings-{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(path, "{not-json");
            var corrupt = await new JsonApplicationSettingsStore(path).LoadAsync();
            Assert.NotNull(corrupt.Warning);
            Assert.Equal(ApplicationSettings.CurrentVersion, corrupt.Settings.Version);

            await File.WriteAllTextAsync(path, """{"version":0,"defaultDatabase":"legacy"}""");
            var older = await new JsonApplicationSettingsStore(path).LoadAsync();
            Assert.Contains("migrated", older.Warning, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("legacy", older.Settings.DefaultDatabase);
            Assert.Equal(ApplicationSettings.CurrentVersion, older.Settings.Version);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
