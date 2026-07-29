using System.Text.Json;
using PostgreManagementStudio.Application;

namespace PostgreManagementStudio.Postgres;

public interface IConnectionProfileStore
{
    Task<IReadOnlyList<ConnectionProfile>> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(ConnectionProfile profile, CancellationToken cancellationToken = default);
    Task DeleteAsync(string profileId, bool deleteCredential = true, CancellationToken cancellationToken = default);
}

public sealed class JsonConnectionProfileStore(
    string path,
    CredentialLifecycleService credentials) : IConnectionProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public string Path { get; } = System.IO.Path.GetFullPath(path);

    public async Task<IReadOnlyList<ConnectionProfile>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(Path)) return [];
        try
        {
            await using var stream = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var profiles = await JsonSerializer.DeserializeAsync<ConnectionProfile[]>(stream, JsonOptions, cancellationToken) ?? [];
            return profiles.Where(IsSafePersistedProfile).Select(x => x with { Password = null, ClientKey = null, ClientCertificatePassword = null }).ToArray();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            try { await BackupCorruptFileAsync(cancellationToken); }
            catch (Exception backupError) when (backupError is IOException or UnauthorizedAccessException) { }
            return [];
        }
    }

    public async Task SaveAsync(ConnectionProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var profiles = (await LoadAsync(cancellationToken)).ToDictionary(x => x.Id, StringComparer.Ordinal);
        profiles[profile.Id] = profile with { Password = null, ClientKey = null, ClientCertificatePassword = null };
        await AtomicWriteAsync(profiles.Values.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray(), cancellationToken);
    }

    public async Task DeleteAsync(string profileId, bool deleteCredential = true, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        var profiles = (await LoadAsync(cancellationToken)).ToDictionary(x => x.Id, StringComparer.Ordinal);
        if (!profiles.Remove(profileId, out var removed)) return;
        await AtomicWriteAsync(profiles.Values.ToArray(), cancellationToken);
        if (deleteCredential) await credentials.DeleteAsync(removed.CredentialReference, cancellationToken);
    }

    private async Task AtomicWriteAsync(ConnectionProfile[] profiles, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var temporary = Path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, profiles, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            File.Move(temporary, Path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private async Task BackupCorruptFileAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(Path)) return;
        var backup = Path + ".corrupt-" + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss") + ".bak";
        await using var source = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using var destination = new FileStream(backup, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await source.CopyToAsync(destination, cancellationToken);
    }

    private static bool IsSafePersistedProfile(ConnectionProfile profile)
        => !string.IsNullOrWhiteSpace(profile.Id) && profile.Id.Length <= 128 && !profile.Id.Any(char.IsControl)
           && !string.IsNullOrWhiteSpace(profile.Host) && profile.Host.Length <= 1024
           && !string.IsNullOrWhiteSpace(profile.Database) && profile.Database.Length <= 255
           && !string.IsNullOrWhiteSpace(profile.Username) && profile.Username.Length <= 255
           && profile.Port is >= 1 and <= 65535
           && Enum.IsDefined(profile.AuthenticationMode) && Enum.IsDefined(profile.Environment) && Enum.IsDefined(profile.SslMode);
}
