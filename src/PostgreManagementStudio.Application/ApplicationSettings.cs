using System.Text.Json;
using System.Text.Json.Serialization;

namespace PostgreManagementStudio.Application;

public sealed record ApplicationSettings
{
    public const int CurrentVersion = 2;

    public int Version { get; init; } = CurrentVersion;
    public string DefaultDatabase { get; init; } = "postgres";
    public int CommandTimeoutSeconds { get; init; } = 30;
    public int CancellationTimeoutSeconds { get; init; } = 5;
    public int DisplayedRowLimit { get; init; } = 10_000;
    public int ResultWarningThreshold { get; init; } = 5_000;
    public int CellDisplayLimit { get; init; } = 512;
    public bool DiagnosticMode { get; init; }
    public int RecentFileLimit { get; init; } = 20;
    public bool QueryHistoryEnabled { get; init; } = true;
    public bool PrivateSessionByDefault { get; init; }
    public QueryTextStorageMode QueryHistoryTextMode { get; init; } = QueryTextStorageMode.FingerprintAndPreview;
    public int QueryHistoryRetentionDays { get; init; } = 30;
    public int QueryHistoryMaximumPerQuery { get; init; } = 100;

    [JsonExtensionData]
    public Dictionary<string, JsonElement> AdditionalValues { get; init; } = new(StringComparer.Ordinal);

    public ApplicationSettings Validate() => this with
    {
        Version = CurrentVersion,
        DefaultDatabase = SafeText(DefaultDatabase, "postgres", 255),
        CommandTimeoutSeconds = Math.Clamp(CommandTimeoutSeconds, 1, 86_400),
        CancellationTimeoutSeconds = Math.Clamp(CancellationTimeoutSeconds, 1, 60),
        DisplayedRowLimit = Math.Clamp(DisplayedRowLimit, 100, 1_000_000),
        ResultWarningThreshold = Math.Clamp(ResultWarningThreshold, 100, 1_000_000),
        CellDisplayLimit = Math.Clamp(CellDisplayLimit, 32, 32_768),
        RecentFileLimit = Math.Clamp(RecentFileLimit, 1, 200),
        QueryHistoryTextMode = Enum.IsDefined(QueryHistoryTextMode) ? QueryHistoryTextMode : QueryTextStorageMode.FingerprintAndPreview,
        QueryHistoryRetentionDays = Math.Clamp(QueryHistoryRetentionDays, 1, 3650),
        QueryHistoryMaximumPerQuery = Math.Clamp(QueryHistoryMaximumPerQuery, 1, 10_000),
        AdditionalValues = AdditionalValues
            .Where(x => !IsSensitiveSettingName(x.Key))
            .ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal),
    };

    private static string SafeText(string? value, string fallback, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl)) return fallback;
        value = value.Trim();
        return value.Length <= maximum ? value : value[..maximum];
    }

    private static bool IsSensitiveSettingName(string name) =>
        new[] { "password", "pwd", "token", "secret", "connectionstring", "privatekey", "passphrase" }
            .Any(x => name.Replace("_", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal)
                .Contains(x, StringComparison.OrdinalIgnoreCase));
}

public sealed record SettingsLoadResult(ApplicationSettings Settings, string? Warning = null);

public interface IApplicationSettingsStore
{
    Task<SettingsLoadResult> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken = default);
}

public sealed class JsonApplicationSettingsStore(string path) : IApplicationSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public string Path { get; } = System.IO.Path.GetFullPath(
        string.IsNullOrWhiteSpace(path) ? throw new ArgumentException("Settings path is required.", nameof(path)) : path);

    public async Task<SettingsLoadResult> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(Path)) return new(new ApplicationSettings());
        try
        {
            await using var stream = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var settings = await JsonSerializer.DeserializeAsync<ApplicationSettings>(stream, JsonOptions, cancellationToken);
            return settings is null
                ? new(new ApplicationSettings(), "Settings were empty; defaults were loaded.")
                : new(settings.Validate(), settings.Version == ApplicationSettings.CurrentVersion ? null : $"Settings version {settings.Version} was migrated to {ApplicationSettings.CurrentVersion}.");
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            string backupStatus;
            try
            {
                var backup = await BackupCorruptSettingsAsync(cancellationToken);
                backupStatus = $"A copy was retained as {System.IO.Path.GetFileName(backup)}.";
            }
            catch (Exception backupError) when (backupError is IOException or UnauthorizedAccessException)
            {
                backupStatus = "The corrupt file could not be backed up.";
            }
            return new(new ApplicationSettings(), $"Settings could not be loaded; defaults were used. {backupStatus} {ex.GetType().Name}.");
        }
    }

    public async Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var directory = System.IO.Path.GetDirectoryName(Path)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, settings.Validate(), JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            File.Move(temporaryPath, Path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private async Task<string> BackupCorruptSettingsAsync(CancellationToken cancellationToken)
    {
        var backup = Path + ".corrupt-" + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss") + ".bak";
        await using var source = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using var destination = new FileStream(backup, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await source.CopyToAsync(destination, cancellationToken);
        return backup;
    }
}
