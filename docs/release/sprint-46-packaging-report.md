# Sprint 46 packaging and deployment report

## Deployment decision

The first release candidate uses one supported deployment model:

- self-contained `.NET` `win-x64` application;
- offline ZIP package containing published application files and a controlled
  PowerShell installer, repair, and uninstaller;
- per-user installation under `%LOCALAPPDATA%\Programs\PostgreManagementStudio`;
- no administrator privilege, PostgreSQL server installation, internet access,
  Visual Studio, .NET SDK, source tree, or development path required at install
  or first launch.

This is intentionally a controlled portable package rather than a GUI-only
installer project. The scripts are source-controlled, deterministic, reversible,
and suitable for later Authenticode signing. A future MSI/MSIX decision is out
of scope until clean-VM and signing qualification are available.

## Identity and versioning

| Value | Stable first-release value |
|---|---|
| Product / publisher | PostgreManagementStudio |
| Executable | `PostgreManagementStudio.Desktop.exe` |
| Product version | `0.9.0-rc.1` |
| Upgrade identity | Product name plus per-user install root; existing install is replaced in place |
| Package identity | `PostgreManagementStudio-{version}-win-x64.zip` |
| Start-menu entry | `PostgreManagementStudio.lnk` |
| Settings namespace | `%LOCALAPPDATA%\PostgreManagementStudio` |
| Credential namespace | Windows Credential Manager, profile-derived opaque reference |
| File associations / protocol | None registered in this release candidate; SQL opening cannot execute SQL |

`Directory.Build.props` is the single version source. `VersionPrefix` and
`VersionSuffix` flow into assembly, file, informational, package, manifest,
and About-dialog metadata. Dirty builds are marked in the manifest and are
rejected by default unless `-AllowDirty` is explicitly supplied. Unsupported
downgrades are not offered; install is an upgrade/repair operation only.

## Reproducible build

From a committed checkout, run:

```powershell
.\scripts\release\build-release.ps1
.\scripts\release\verify-package.ps1 -PackagePath .\artifacts\release\PostgreManagementStudio-0.9.0-rc.1-win-x64.zip
.\scripts\release\test-installer.ps1 -PackagePath .\artifacts\release\PostgreManagementStudio-0.9.0-rc.1-win-x64.zip
```

The build restores with the `win-x64` runtime, builds Release, runs the
solution tests, publishes self-contained without trimming, single-file, or
ReadyToRun changes, creates the ZIP, release manifest, SHA-256 checksum, and
package inventory. Toolchain: .NET SDK 10.0.302, target framework
`net9.0-windows`, Npgsql 8.0.6. The generated package is approximately 62 MB.

## User-data and migration contract

Application-owned binaries are replaceable; user data is not in the package:

| Data | Location / policy |
|---|---|
| Settings, profiles, history, layout, logs, recovery/cache | `%LOCALAPPDATA%\PostgreManagementStudio` |
| Passwords | Windows Credential Manager only; profile JSON stores references, not secrets |
| User SQL, exports, backups, workspaces | User-selected locations, never installer-owned |
| Temporary files | Operation-specific temporary locations, cleaned on completion/failure |

Settings schema is version 2 and workspace schema is version 1. Existing
settings are validated and unknown safe JSON values are retained; sensitive
extension fields are discarded. Corrupt settings and profiles are copied to a
timestamped `.corrupt-*.bak` before defaults/empty state are used. Recovery
snapshots are atomic and remain outside the install directory. The installer
does not rewrite or delete these files. This preserves profiles, credential
references, workspaces, unsaved SQL, and recovery data across upgrade.

## Install, upgrade, repair, uninstall

The installer refuses to operate while the application process is running,
copies application files to a temporary sibling directory, replaces the
application-owned install root, writes a non-sensitive install record, and
creates the Start-menu shortcut. A failed replacement restores the prior
directory. Repair uses the same safe replacement path and leaves user data
untouched.

Normal uninstall removes application binaries and the shortcut only. It
preserves profiles, Credential Manager entries, SQL files, exports, backups,
workspaces, recovery data, and logs. `uninstall.ps1 -RemoveUserData` is the
explicit opt-in removal path for the application-owned data directory; it does
not touch arbitrary user directories or PostgreSQL installations.

## Package audit and release artefacts

The generated `artifacts\release` directory contains:

- `PostgreManagementStudio-0.9.0-rc.1-win-x64.zip`;
- `release-manifest.json` with source revision, runtime, versions, schema
  versions, checksums, file inventory, tests, and signing status;
- `checksums.sha256`;
- `package-inventory.json` with path, size, and SHA-256 for every staged file;
- the offline installer, repair/uninstaller, licence placeholder, and
  third-party notice files.

The package audit passed with 407 files. No test assemblies, PDBs, coverage,
temporary databases, seeded credentials, development connection strings,
`.git` metadata, or personal paths were found in the package. The SBOM source
is the resolved NuGet package inventory plus the self-contained runtime; the
final public package still requires legal review of the included licence and
third-party notices before publication.

## Verification

| Gate | Result |
|---|---|
| Release build | Pass; 0 warnings, 0 errors |
| Existing Sprint 45 release baseline | Pass; 1,152/1,152 across three iterations |
| Package verification | Pass; expected executable, no forbidden artefacts/secrets |
| Script parse validation | Pass |
| Install | Pass in isolated temporary profile |
| Repair | Pass; application files replaced and user-state marker preserved |
| Upgrade-style replacement | Pass; same per-user root replaced without data reset |
| Uninstall | Pass; binaries/shortcut removed and user state preserved |
| Offline package | Pass structurally; no installer-time restore/download path |
| Authenticode | Unsigned internal candidate; signing order and verification are ready |

The standard local test invocation passes with 324 tests and reports 60
database integration tests skipped when `PMS_CONNECTION_STRING` is absent. The
full PostgreSQL-gated evidence remains the Sprint 45 run and is recorded in
`docs/release/sprint-45-qualification-report.md`.

## External qualification matrix and limitations

| Environment/scenario | Status | Required closure |
|---|---|---|
| Windows 11 x64 developer workstation | Automated package lifecycle pass | Repeat on clean VM |
| Standard-user per-user install | Automated pass | Clean-VM confirmation |
| No .NET SDK/runtime installed | Self-contained design | Clean VM launch confirmation |
| Existing/no PostgreSQL utilities | App design supports configured utility discovery | Manual utility-path matrix |
| PostgreSQL 14 and 18 | 18.4 regression pass | PostgreSQL 14 connected package run |
| TLS/password/certificate auth | Application capability previously qualified only locally | Secure endpoint test |
| Defender/SmartScreen/restricted folders | Not available in this environment | Security workstation campaign |
| Unicode/long paths/non-English profile | Code uses path APIs; not VM-tested | Manual VM matrix |
| Authenticode and malware scan | Ready, not signed/scanned | Certificate and scanner owner required |
| Public licence/attribution | Pending project-owner licence decision | Complete before public distribution |

These limitations are explicit release-candidate qualification gates, not
claims that unsupported scenarios passed.

## Recommendation

**Sprint 47 may begin.** The source-controlled packaging workflow and internal
RC package are ready for final clean-VM, signing, malware-scan, legal notice,
and PostgreSQL compatibility qualification. Public release remains NO-GO until
those external gates are completed.
