using PostgreManagementStudio.Application;
using PostgreManagementStudio.Postgres;

namespace PostgreManagementStudio.Postgres.Tests;

public sealed class ConnectionProfileStoreTests
{
    [Fact]
    public async Task ProfilePersistence_StoresReferenceButNeverCredentialMaterial()
    {
        var root = Path.Combine(Path.GetTempPath(), "pms-profiles-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "connections.json");
        var credentials = new RecordingCredentialStore();
        var lifecycle = new CredentialLifecycleService(credentials);
        var store = new JsonConnectionProfileStore(path, lifecycle);
        try
        {
            var reference = await lifecycle.SaveAsync("profile", "seed-profile-secret".AsMemory());
            await store.SaveAsync(new ConnectionProfile
            {
                Id = "profile",
                Name = "Production",
                Host = "localhost",
                Database = "postgres",
                Username = "postgres",
                Password = "seed-profile-secret",
                CredentialReference = reference,
                Environment = EnvironmentClassification.Production,
            });

            var json = await File.ReadAllTextAsync(path);
            Assert.DoesNotContain("seed-profile-secret", json);
            Assert.Contains(reference, json);
            var loaded = Assert.Single(await store.LoadAsync());
            Assert.Null(loaded.Password);
            Assert.Equal(reference, loaded.CredentialReference);

            await store.DeleteAsync("profile");
            Assert.Contains(reference, credentials.Deleted);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task CorruptProfileStore_IsBackedUpAndIgnored()
    {
        var root = Path.Combine(Path.GetTempPath(), "pms-profiles-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "connections.json");
        try
        {
            await File.WriteAllTextAsync(path, "[malformed");
            var store = new JsonConnectionProfileStore(path,
                new CredentialLifecycleService(new RecordingCredentialStore()));
            Assert.Empty(await store.LoadAsync());
            Assert.Single(Directory.GetFiles(root, "*.bak"));
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class RecordingCredentialStore : IProtectedCredentialStore
    {
        private readonly Dictionary<string, char[]> _values = new();
        public List<string> Deleted { get; } = [];
        public ValueTask StoreAsync(string reference, ReadOnlyMemory<char> secret, CancellationToken cancellationToken = default)
        { _values[reference] = secret.ToArray(); return ValueTask.CompletedTask; }
        public ValueTask<char[]?> RetrieveAsync(string reference, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(_values.TryGetValue(reference, out var value) ? value.ToArray() : null);
        public ValueTask DeleteAsync(string reference, CancellationToken cancellationToken = default)
        { _values.Remove(reference); Deleted.Add(reference); return ValueTask.CompletedTask; }
    }
}
