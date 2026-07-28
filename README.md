# PostgreManagementStudio

Windows-only WPF PostgreSQL management application. Current milestone: Sprint 002
result storage and virtualisation foundation.

The Sprint 002 layer adds an in-memory, provider-neutral result-store that
sits between the Sprint 001 async query pipeline and a future production
result grid. It accepts streamed row batches, retains typed values, exposes
random-access reads, preserves multiple result sets independently, and avoids
creating UI objects for every cell.

## Prerequisites

Git, .NET SDK 9 (upgrade to current LTS when installed), Visual Studio with
.NET desktop development, and PostgreSQL for Windows.

## Build and test

```powershell
dotnet restore
dotnet build --configuration Release
dotnet test --configuration Release
```

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