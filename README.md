# PostgreManagementStudio

Windows-only WPF PostgreSQL management application. The project is in release
hardening after Sprints 1-33. The service and domain layers cover query
execution, result handling, administration, monitoring, schema comparison,
performance analysis, and index analysis. The current desktop host exposes a
smaller prototype subset; see
`docs/hardening/034-feature-traceability.md` for the audited reachability status.

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
