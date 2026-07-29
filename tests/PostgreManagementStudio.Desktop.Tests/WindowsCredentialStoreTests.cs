using PostgreManagementStudio.Application;
using PostgreManagementStudio.Desktop;

namespace PostgreManagementStudio.Desktop.Tests;

public sealed class WindowsCredentialStoreTests
{
    [Fact]
    public async Task CredentialManager_RoundTripsAndDeletesCredential()
    {
        var store = new WindowsCredentialStore();
        var reference = CredentialReference.ForProfile("test-" + Guid.NewGuid().ToString("N"));
        var secret = ("p@" + Guid.NewGuid().ToString("N")).ToCharArray();
        try
        {
            await store.StoreAsync(reference, secret);
            Assert.Equal(secret, await store.RetrieveAsync(reference));
            await store.DeleteAsync(reference);
            Assert.Null(await store.RetrieveAsync(reference));
        }
        finally { await store.DeleteAsync(reference); }
    }

    [Theory]
    [InlineData("unscoped")]
    [InlineData("PostgreManagementStudio/profile/bad\nvalue")]
    public async Task CredentialManager_RejectsInvalidReferences(string reference)
        => await Assert.ThrowsAsync<ArgumentException>(async () => await new WindowsCredentialStore().RetrieveAsync(reference));
}
