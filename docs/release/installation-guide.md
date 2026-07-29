# Installation, upgrade, repair, and removal

The Sprint 56 candidate is `PostgreManagementStudio-0.9.0-rc.3-win-x64.zip`.
Its recorded SHA-256 is
`22fa5b41a1952d90d5514d95efcc95b1b657169d378d6bf083f7ae4dc58b19ad`.
This is an unsigned internal candidate pending clean-machine, upgrade,
compatibility, signing, malware-scan, and licence gates.

The final Sprint 57 candidate hash supersedes the Sprint 56 hash:
`e6244a56b6a654123cd3ae7a7318e2bc28e978b35981b709f0149a564d8829aa`.
It has passed package, launch, installer lifecycle and prior-package installer
upgrade checks, but should remain internal until the conditions in
`FINAL_RELEASE_DECISION.md` are closed.

## Requirements

- Windows 11 x64 for the currently qualified environment.
- No .NET runtime, SDK, Visual Studio, source checkout, or internet connection
  is required by the self-contained package.
- PostgreSQL is not installed or altered by PostgreManagementStudio. Query work
  can connect to a remote server; backup/restore additionally needs compatible
  `pg_dump`, `pg_restore`, and `psql` configured or discoverable locally.

## Install

Extract the approved release ZIP and run `install.ps1`. The default per-user
location is `%LOCALAPPDATA%\Programs\PostgreManagementStudio`; the Start-menu
shortcut points there. Close PostgreManagementStudio before installing or
upgrading. The installer fails safely if the application is still running.

## Upgrade and repair

Run the new package's `install.ps1` in the same install root. It stages files
beside the current installation and replaces only application-owned binaries.
Settings, profiles, Credential Manager references, logs, recovery snapshots,
and user SQL remain outside the install root. `install.ps1 -Repair` restores
application files using the same preservation rules.

## User data and recovery

Application state is stored in `%LOCALAPPDATA%\PostgreManagementStudio`.
Passwords are not stored there: an opt-in saved password is held in Windows
Credential Manager and profile JSON contains only an opaque reference. Corrupt
settings/profiles are copied to timestamped backup files before safe defaults
are used. Recovery snapshots never execute automatically after restart.

## Uninstall

Run `uninstall.ps1`. Normal removal deletes application binaries and the
Start-menu shortcut but preserves state, logs, profiles, credentials, history,
and recovery data. `uninstall.ps1 -RemoveUserData` explicitly removes only
`%LOCALAPPDATA%\PostgreManagementStudio`; it never removes PostgreSQL,
arbitrary SQL files, exports, or backups.

## Diagnostics

Installer logs are written under `%LOCALAPPDATA%\PostgreManagementStudio\logs`.
When reporting a defect, provide application version, package SHA-256, Windows
version/architecture, PostgreSQL version, and redacted logs. Do not include
passwords, full connection strings, private keys, query results, or SQL text
unless explicitly needed and reviewed.
