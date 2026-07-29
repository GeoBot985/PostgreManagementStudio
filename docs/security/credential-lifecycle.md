# Credential Lifecycle and Storage

## Storage model

Connection profiles are stored in `%LOCALAPPDATA%\PostgreManagementStudio\connections.json`. Secret-bearing properties are excluded from JSON. A saved profile may contain `credentialReference`, an opaque stable target derived from the profile ID. The corresponding password is stored as a Windows Credential Manager generic credential by `WindowsCredentialStore`.

Password persistence is off unless the user selects both **Save connection profile** and **Protect password in Windows Credential Manager**. A profile can therefore be saved with no password. The password field is never populated from an existing credential; retrieval happens only when connecting.

## Lifecycle

1. **Entry:** WPF `PasswordBox` receives the password. It is not copied to profile/history/settings files.
2. **Temporary use:** the connection builder receives the minimum string needed by Npgsql. Mutable character and byte buffers used for Credential Manager transfer are zeroed in `finally` blocks.
3. **Persistence:** explicit opt-in writes the secret to Windows Credential Manager and writes only its reference to profile JSON.
4. **Retrieval:** an empty password field plus an existing reference retrieves a temporary `char[]`; the visible password field remains blank.
5. **Authentication failure/cancellation:** the connection probe returns a classified safe message. Submitted credentials are absent from aggregation and diagnostics; temporary mutable buffers are zeroed.
6. **Replacement:** entering a new password with secure persistence selected overwrites the same stable Credential Manager target.
7. **Editing:** leaving the password blank retains the existing reference; it does not silently erase the password.
8. **Duplication:** `CredentialLifecycleService.DuplicateAsync` excludes the credential by default and requires an explicit `includeCredential=true`.
9. **Export:** JSON serialization omits password, private key, and client-certificate password properties.
10. **Deletion:** **Delete saved password** removes it from Credential Manager and clears the profile reference. Profile deletion deletes its referenced credential by default.
11. **Shutdown:** sessions and pools are disposed; immutable strings remain subject to .NET lifetime constraints, while all owned mutable secret buffers are explicitly cleared.

## Platform limitation

The implementation is Windows-specific because this is a Windows WPF application. Credential Manager encrypts stored credentials for the Windows security context. It does not protect against malware, a debugger, or another process already executing with the same user's authority. Database-side least privilege, workstation protection, and credential rotation remain necessary.
