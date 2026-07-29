using System.Security.Cryptography;
using System.Text;

namespace PostgreManagementStudio.Application;

public interface IProtectedCredentialStore
{
    ValueTask StoreAsync(string reference, ReadOnlyMemory<char> secret, CancellationToken cancellationToken = default);
    ValueTask<char[]?> RetrieveAsync(string reference, CancellationToken cancellationToken = default);
    ValueTask DeleteAsync(string reference, CancellationToken cancellationToken = default);
}

public static class CredentialReference
{
    public static string ForProfile(string profileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(profileId.Trim()));
        return $"PostgreManagementStudio/profile/{Convert.ToHexString(digest)}";
    }
}

public sealed class CredentialLifecycleService(IProtectedCredentialStore store)
{
    public async ValueTask<string> SaveAsync(string profileId, ReadOnlyMemory<char> secret, CancellationToken cancellationToken = default)
    {
        if (secret.IsEmpty) throw new ArgumentException("A non-empty credential is required.", nameof(secret));
        var reference = CredentialReference.ForProfile(profileId);
        await store.StoreAsync(reference, secret, cancellationToken);
        return reference;
    }

    public ValueTask<char[]?> RetrieveAsync(string? reference, CancellationToken cancellationToken = default)
        => string.IsNullOrWhiteSpace(reference)
            ? ValueTask.FromResult<char[]?>(null)
            : store.RetrieveAsync(reference, cancellationToken);

    public ValueTask DeleteAsync(string? reference, CancellationToken cancellationToken = default)
        => string.IsNullOrWhiteSpace(reference)
            ? ValueTask.CompletedTask
            : store.DeleteAsync(reference, cancellationToken);

    public async ValueTask<string?> DuplicateAsync(
        string? sourceReference,
        string destinationProfileId,
        bool includeCredential,
        CancellationToken cancellationToken = default)
    {
        if (!includeCredential || string.IsNullOrWhiteSpace(sourceReference)) return null;
        var secret = await store.RetrieveAsync(sourceReference, cancellationToken);
        if (secret is null) return null;
        try { return await SaveAsync(destinationProfileId, secret, cancellationToken); }
        finally { CryptographicOperations.ZeroMemory(System.Runtime.InteropServices.MemoryMarshal.AsBytes(secret.AsSpan())); }
    }
}
