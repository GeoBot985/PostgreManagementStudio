# PostgreManagementStudio

Windows-only WPF PostgreSQL management application. The desktop host composes
query editing/execution, results/export, Object Explorer browsing and scripting,
backup/restore, data transfer, schema/index analysis, and operational
monitoring. The exact audited scope and explicit boundaries are tracked in
`docs/audits/UI_REACHABILITY_MATRIX.md`.

## Prerequisites

Git, .NET SDK 9, VS Code (or another editor), and PostgreSQL for Windows.
Visual Studio is optional; the .NET SDK contains the required build tools.

## Build and test

```powershell
dotnet restore
dotnet build --configuration Debug
dotnet build --configuration Release
dotnet test --configuration Release
```

For the isolated PostgreSQL release regression, including real metadata,
permissions, transactions, monitoring, plans, backup, performance, UI smoke,
coverage, and cleanup:

```powershell
$env:PMS_ADMIN_CONNECTION_STRING = "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=<local-test-password>"
.\scripts\test-release.ps1
```

See `docs/testing/integration-environment.md`.

SQL execution lifecycle, transaction ownership, cancellation, bounded results,
diagnostics, and privacy rules are documented in
`docs/hardening/036-execution-contract.md`. Sprint 36 evidence is recorded in
`docs/hardening/036-completion-report.md`.

Connection configuration, lifecycle, credentials, pooling, session reset, and
reconnect rules are documented in
`docs/hardening/037-connection-contract.md`. Sprint 37 evidence is recorded in
`docs/hardening/037-completion-report.md`.

Object Browser identity, lazy metadata loading, refresh, filtering, cache,
permission, and diagnostics rules are documented in
`docs/hardening/038-metadata-contract.md`. Sprint 38 evidence is recorded in
`docs/hardening/038-completion-report.md`.

Backup/restore lifecycle, immutable plans, tool and credential handling,
atomic output, destructive confirmation, process cancellation, and recovery
rules are documented in `docs/hardening/039-backup-restore-contract.md`.
Sprint 39 evidence is recorded in
`docs/hardening/039-completion-report.md`.

To run the gated perf suite:

```powershell
$env:PMS_RUN_PERF = "1"
dotnet test --configuration Release --filter "FullyQualifiedName~ResultStoragePerfTests"
```

To direct the perf report to a persistent path:

```powershell
$env:PMS_PERF_REPORT_DIR = "D:\Projects\CURRENT\PostgreManagementStudio"
$env:PMS_RUN_PERF = "1"
dotnet test --configuration Release --filter "FullyQualifiedName~WritesReportSummary"
```

## Running the desktop project

```powershell
$env:PMS_CONNECTION_STRING = "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=<password>"
dotnet run --project src/PostgreManagementStudio.Desktop --configuration Release
```

Do not commit credentials.

Integration tests requiring PostgreSQL are reported as skipped when
`PMS_CONNECTION_STRING` is absent; they run normally when it is configured.

## Release candidate

The current internal candidate is a self-contained Windows 11 x64 ZIP and
does not require .NET, Visual Studio, or PostgreSQL on the client machine.
Install, repair, upgrade, and removal instructions are in
`docs/release/installation-guide.md`; Sprint 56 qualification evidence is in
`docs/release/RC_EXIT_REVIEW.md`. It is not a public-release approval: clean
machine, upgrade, broader PostgreSQL compatibility, signing, malware-scan, and
licence gates remain open.

Sprint 57 froze the internal candidate at package SHA-256
`e6244a56b6a654123cd3ae7a7318e2bc28e978b35981b709f0149a564d8829aa`.
Its explicit decision and conditions are in `docs/release/FINAL_RELEASE_DECISION.md`.
Sprint 58 source changes are newer than that frozen package; Object Explorer
scripting is documented in `docs/features/object-explorer-scripting.md` and
requires a new package qualification before it becomes a release claim.
