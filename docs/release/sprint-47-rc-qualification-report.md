# Sprint 47 release-candidate qualification

## Candidate decision

The qualified internal candidate is **PostgreManagementStudio 0.9.0-rc.2**.

| Field | Value |
|---|---|
| Source revision | `d9bbc0682f93180e99fbf20948459f282aa84002` |
| Package | `PostgreManagementStudio-0.9.0-rc.2-win-x64.zip` |
| Package SHA-256 | `68a0aac7fcb4860fe0d16625e46f7dbe5e176a97f8529a3fbbcddc22a7b97abc` |
| Executable SHA-256 | `49b54b0d513a8f97af606cd62418f3cb76361543fe4df0076101d1c40eca1535` |
| Build | Release, self-contained `win-x64`, controlled per-user PowerShell installer |
| Settings / workspace schema | 2 / 1 |
| Npgsql | 8.0.6 |
| Signing | Unsigned internal candidate; signing-ready only |

`rc.1` was retired after an intermittent backup/restore integration failure
under parallel execution. `rc.2` serializes the PostgreSQL integration
assembly, preventing external-utility/database lifecycle interference. The
new candidate has a distinct version and package hash.

## Artefact integrity and archive

The exact package passed checksum, manifest, inventory, expected-executable,
forbidden-test/debug artefact, seeded-secret, and development-connection scans.
It contains 407 files. The archive command is:

```powershell
.\scripts\release\archive-release-candidate.ps1 -PackagePath .\artifacts\release\PostgreManagementStudio-0.9.0-rc.2-win-x64.zip -RegressionSummaryPath .\TestResults\36862cdedb\release-summary.json
```

The immutable bundle contains the package, manifest, checksums, inventory,
licence/notice files, and machine-readable regression summary. It intentionally
contains no credentials, private signing material, test database, or user data.

## Qualification results

| Area | Evidence / result |
|---|---|
| Package verification | Pass; 407 files and checksum/manifest agreement |
| Clean installed first run | Pass; packaged executable launched under isolated user state, showed standard menu/toolbar/editor/object-explorer shell, disconnected state, accessible controls, and clean shutdown |
| Upgrade, repair, uninstall | Pass; automated package lifecycle verified replacement/repair and normal uninstall preservation of user-state marker |
| Full PostgreSQL regression | Pass; run `36862cdedb`, 3 iterations, 1,152 passed, 0 failed, 0 skipped, cleanup passed |
| Query, transaction, cancellation, recovery | Pass through the isolated integration and desktop suites |
| Multi-session/wrong-target | Pass through role-isolation, tab-switch, cancellation, and Object Explorer context tests |
| Backup, restore, import, export | Pass through live disposable PostgreSQL 18.4 suite; custom/plain backup and restore revalidation included |
| Large results/resources/endurance | Pass through 1,000,000-row fixture and repeated lifecycle/resource suites; 100 document and 20 connection cycles covered |
| Credential/redaction/hostile metadata | Pass through security hardening and redaction suites; package scan found no seeded secret |
| PostgreSQL type/result handling | Pass for seeded type matrix and serialization/export integration coverage |
| First-run network/privilege | Pass for disconnected packaged shell; no connection attempt, elevation, or development dependency required |

The normal package build’s unseeded integration run records 60 skipped database
tests because it intentionally has no credentials. The formal qualified
PostgreSQL run above supplies the isolated disposable environment and has no
skips.

## Scope exclusions and unavailable qualification

Data editing is not part of the desktop release surface and was not qualified
as a feature. PostgreSQL 14, remote TLS/password, client certificate,
integrated authentication, clean-VM, restrictive-permission, Unicode-profile,
non-English regional, mixed-DPI, Authenticode, malware scanning, and public
licence/attribution review are unavailable in this workstation environment.
These items are recorded in the known-issues file and are not represented as
passing tests.

## Release gate and recommendation

**Conditional GO for external final-release qualification; NO-GO for public
release.**

There are zero known blocker, critical, or high *product-code* defects in the
implemented release surface. The explicit no-go gates for public release are:

1. PostgreSQL 14 and secure remote connection matrix;
2. clean-VM/display/locale deployment matrix;
3. code signing and signed-package malware scan;
4. project-owner licence and final third-party-attribution approval.

No accepted limitation permits data loss, wrong-target work, credential
exposure, transaction-state misrepresentation, unsafe install/uninstall, or
ordinary-use crashes. Sprint 48 may begin only as the external qualification
and release-governance campaign; a public 1.0 release should not begin until
the listed gates are closed.
