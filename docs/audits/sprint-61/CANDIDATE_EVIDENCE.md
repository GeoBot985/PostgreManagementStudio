# Sprint 61 candidate evidence

## Source and package

| Item | Value |
| --- | --- |
| Repository | `D:\Projects\CURRENT\PostgreManagementStudio` |
| Branch | `master` |
| Candidate revision | `424133bff9684c962b93a71feab3ebdc49da46bd` |
| Initial status | `?? STATE_OF_THE_NATION.md` |
| Version | `0.9.0-rc.3` |
| Configuration / RID | `Release` / `win-x64` |
| Package | `PostgreManagementStudio-0.9.0-rc.3-win-x64.zip` |
| Size | 62,374,510 bytes |
| First SHA-256 | `8179e145d014fe5bc872a9def541d1d411f314283c259b6cdb861010238f3c61` |
| Rebuilt SHA-256 | `44ebdd10983468c9091a10cfa84907eedd5c334a6bd3ec4e8951ca5dded790d6` |
| Files verified | 407 |
| Manifest source | `424133bff9684c962b93a71feab3ebdc49da46bd` |
| Runtime mode | Self-contained |
| Signing | Unsigned internal candidate |

The package was rebuilt twice from the same source. Both builds completed and
produced the same byte count, but the archive hashes differed. The current
manifest and checksum file identify the rebuilt package.

## Build record

Command:

```powershell
scripts/release/build-release.ps1
```

Observed result:

- restore passed;
- clean passed;
- Release build passed;
- zero warnings and zero errors;
- automated packaging tests passed;
- self-contained publish passed;
- package inventory, manifest and checksums generated;
- ZIP generated;
- elapsed time approximately 49.3 seconds.

Verification:

```powershell
scripts/release/verify-package.ps1 `
  -PackagePath artifacts/release/PostgreManagementStudio-0.9.0-rc.3-win-x64.zip
```

Result: pass, 407 files.

## Environment record

```text
OS: Microsoft Windows 11 Pro 64-bit
Version/build: 10.0.26200 / 26200
.NET SDK: 10.0.302
Additional SDK: 9.0.315
.NET 9 runtime: 9.0.17
PostgreSQL server: 18.4 x86_64-windows
psql: 18.4
pg_dump: 18.4
pg_restore: 18.4
```

## Repeatable automated suite

Command:

```powershell
scripts/test-release.ps1 `
  -AdminConnectionString <local-secret-safe-admin-string> `
  -Repeat 2 `
  -IncludeLargeDataset `
  -SkipCoverage
```

Run ID: `2f853cd4c5`.

| Iteration | Assembly | Passed | Failed | Skipped | Seconds |
| ---: | --- | ---: | ---: | ---: | ---: |
| 1 | PostgreManagementStudio.Core.Tests | 232 | 0 | 0 | 1.48 |
| 1 | PostgreManagementStudio.Results.Tests | 65 | 0 | 0 | 1.31 |
| 1 | PostgreManagementStudio.Postgres.Tests | 54 | 0 | 0 | 1.32 |
| 1 | PostgreManagementStudio.Desktop.Tests | 31 | 0 | 0 | 4.34 |
| 1 | PostgreManagementStudio.IntegrationTests | 71 | 0 | 0 | 29.35 |
| 2 | PostgreManagementStudio.Core.Tests | 232 | 0 | 0 | 1.09 |
| 2 | PostgreManagementStudio.Results.Tests | 65 | 0 | 0 | 0.90 |
| 2 | PostgreManagementStudio.Postgres.Tests | 54 | 0 | 0 | 0.84 |
| 2 | PostgreManagementStudio.Desktop.Tests | 31 | 0 | 0 | 3.16 |
| 2 | PostgreManagementStudio.IntegrationTests | 71 | 0 | 0 | 27.39 |

Aggregate: 906 passed, 0 failed, 0 skipped; disposable database/role cleanup
passed. The harness elapsed time was approximately 70.88 seconds.

## Packaged workflow measurements

| Operation | Measurement/result |
| --- | --- |
| Application launch | Pass |
| Connection | `postgres@localhost:5432`, pass |
| Audit schema expansion | ~1,136 ms |
| 304-table folder expansion | ~2,231 ms |
| Alt+F1 description | ~902 ms |
| SELECT execution | 2 rows, pass |
| Result JSONL export | 2 rows, 10 columns, 529 bytes, ~0.014 s |
| JSONL independent parse | 2 valid records |
| JSONL SHA-256 | `6a6ceaa9b7ddeeb4ccee09f3b34e42d02a1da405204487b2b6934cac6ec395ea` |
| Valid JSONB existing-table import | Failed, `XX000` |
| Rows after failed atomic import | 0 |
| Connection after import failure | Available |
| Shutdown behavior | Expected save prompt for unsaved query; cancel preserved data |

## Static, dependency, and secret review

- No `TODO`, `FIXME`, `HACK`, `#warning`, `NotImplementedException`, production
  local path, hard-coded credential, SQL Server syntax, or exposed placeholder
  command was found.
- `.Result` search found a domain property, not synchronous task blocking.
- `Gate.Wait(0)` is an intentional non-blocking semaphore probe.
- Empty catches are confined mainly to best-effort cancellation/cleanup.
- `dotnet list package --vulnerable --include-transitive` reported no known
  vulnerable packages from the configured NuGet source.
- Production package versions: Npgsql `8.0.6`,
  Microsoft.Extensions.DependencyInjection `9.0.0`,
  Microsoft.Extensions.Logging.Abstractions `9.0.0`.
- xUnit v2 was reported as legacy; it is test-only migration debt.
- No supplied test password or password field was found in the repository,
  local connection-profile store, or recovery snapshots.
- No durable application log was emitted in the inspected local application
  directory.

## Cleanup

The exact temporary schema `s61_audit` was verified to exist once and then
dropped with `CASCADE`; PostgreSQL reported 314 contained objects and a
post-check returned zero matching schemas. No pre-existing user schema or data
was included in the cleanup.
