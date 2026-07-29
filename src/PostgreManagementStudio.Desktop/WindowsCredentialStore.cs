using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using PostgreManagementStudio.Application;

namespace PostgreManagementStudio.Desktop;

public sealed class WindowsCredentialStore : IProtectedCredentialStore
{
    private const int CredentialTypeGeneric = 1;
    private const int PersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;
    private const int MaximumBlobBytes = 5 * 512;

    public ValueTask StoreAsync(string reference, ReadOnlyMemory<char> secret, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateReference(reference);
        var bytes = new byte[Encoding.Unicode.GetByteCount(secret.Span)];
        Encoding.Unicode.GetBytes(secret.Span, bytes);
        if (bytes.Length is 0 or > MaximumBlobBytes)
        {
            CryptographicOperations.ZeroMemory(bytes);
            throw new ArgumentOutOfRangeException(nameof(secret), $"Windows Credential Manager accepts 1 to {MaximumBlobBytes / 2} password characters.");
        }

        var blob = Marshal.AllocCoTaskMem(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var credential = new NativeCredential
            {
                Type = CredentialTypeGeneric,
                TargetName = reference,
                CredentialBlobSize = bytes.Length,
                CredentialBlob = blob,
                Persist = PersistLocalMachine,
                UserName = "PostgreManagementStudio",
            };
            if (!CredWrite(ref credential, 0)) throw new Win32Exception(Marshal.GetLastWin32Error(), "The credential could not be protected by Windows Credential Manager.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
            ZeroUnmanaged(blob, bytes.Length);
            Marshal.FreeCoTaskMem(blob);
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask<char[]?> RetrieveAsync(string reference, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateReference(reference);
        if (!CredRead(reference, CredentialTypeGeneric, 0, out var pointer))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == ErrorNotFound) return ValueTask.FromResult<char[]?>(null);
            throw new Win32Exception(error, "The credential could not be retrieved from Windows Credential Manager.");
        }

        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(pointer);
            var bytes = new byte[credential.CredentialBlobSize];
            try
            {
                Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
                return ValueTask.FromResult<char[]?>(Encoding.Unicode.GetChars(bytes));
            }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        finally { CredFree(pointer); }
    }

    public ValueTask DeleteAsync(string reference, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateReference(reference);
        if (!CredDelete(reference, CredentialTypeGeneric, 0))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != ErrorNotFound) throw new Win32Exception(error, "The credential could not be deleted from Windows Credential Manager.");
        }
        return ValueTask.CompletedTask;
    }

    private static void ValidateReference(string reference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        if (!reference.StartsWith("PostgreManagementStudio/profile/", StringComparison.Ordinal) || reference.Length > 256 || reference.Any(char.IsControl))
            throw new ArgumentException("The credential reference is invalid.", nameof(reference));
    }

    private static void ZeroUnmanaged(IntPtr pointer, int length)
    {
        for (var index = 0; index < length; index++) Marshal.WriteByte(pointer, index, 0);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public int Flags;
        public int Type;
        [MarshalAs(UnmanagedType.LPWStr)] public string TargetName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        [MarshalAs(UnmanagedType.LPWStr)] public string? TargetAlias;
        [MarshalAs(UnmanagedType.LPWStr)] public string UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref NativeCredential credential, int flags);

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, int type, int flags, out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, int type, int flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr credential);
}
