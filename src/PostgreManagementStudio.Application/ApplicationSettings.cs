using System.Text.Json;
using System.Text.Json.Serialization;

namespace PostgreManagementStudio.Application;

public sealed record ApplicationSettings
{
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;
    public string DefaultDatabase { get; init; } = "postgres";
    public int CommandTimeoutSeconds { get; init; } = 30;
    public int RecentFileLimit { get; init; } = 20;

    [JsonExtensionData]
    public Dictionary<string, JsonElement> AdditionalValues { get; init; } = new(StringComparer.Ordinal);

    public ApplicationSettings Validate() => this with
    {
        Version = CurrentVersion,
        DefaultDatabase = string.IsNullOrWhiteSpace(DefaultDatabase) ? "postgres" : DefaultDatabase.Trim(),
        CommandTimeoutSeconds = Math.Clamp(CommandTimeoutSeconds, 1, 86_400),
        RecentFileLimit = Math.Clamp(RecentFileLimit, 1, 200),
    };
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
            return new(new ApplicationSettings(), $"Settings could not be loaded; defaults were used. {ex.GetType().Name}: {ex.Message}");
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
}
