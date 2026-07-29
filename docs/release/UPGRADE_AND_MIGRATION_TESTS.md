# Upgrade and configuration migration tests

## Supported policy

The current package is a self-contained per-user application. Application
binaries live under `%LOCALAPPDATA%\Programs\PostgreManagementStudio`; settings,
profiles, Credential Manager references, logs, recovery snapshots and user SQL
live under `%LOCALAPPDATA%\PostgreManagementStudio`. Reinstall/repair preserves
user state. Normal uninstall preserves user state; `-RemoveUserData` is an
explicit separate action.

## Results

| Source | Target | State | Result | Evidence |
|---|---|---|---|---|
| Same 0.9.0-rc.3 package | 0.9.0-rc.3 repair | User marker | Pass; marker preserved | `test-installer.ps1` |
| Same 0.9.0-rc.3 package | 0.9.0-rc.3 uninstall | User marker | Pass; binaries removed and marker preserved | `test-installer.ps1` |
| Pre-composition stable package | 0.9.0-rc.3 | Settings/profiles/history | Not tested in this workstation | RC-002 |
| Older settings schema | Current schema 2 | JSON settings | Automated corrupt/version fallback passes; real old-file campaign not run | ApplicationSettings tests |
| Saved credentials | Current profile reference | Credential Manager | Policy/design reviewed; live migration not run | credential lifecycle tests; RC-002 |

Migration is idempotent and atomic at the settings-store boundary. Corrupt
optional state is backed up and replaced with validated defaults. No migration
copies passwords into ordinary JSON. A rollback of an upgrade is to close the
application, restore the prior package, and preserve the external user-data
directory; destructive operations are never resumed automatically.
