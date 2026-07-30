# Sprint 62 candidate evidence

## Identity

| Field | Value |
| --- | --- |
| Candidate | PostgreManagementStudio `0.9.0-rc.4` |
| Source revision | `516e655a2c6a94c1e7556b2f279ac457353626aa` |
| Dirty source at package time | `false` |
| Configuration | `Release` |
| Runtime | self-contained `win-x64` |
| Package | `PostgreManagementStudio-0.9.0-rc.4-win-x64.zip` |
| SHA-256 | `A928BDB8600CD2E4787B0ADCF9AE0FEAE84FB5E01B07DE987073178568F01E7E` |
| Inventory | 407 verified files |
| Signing | unsigned internal candidate |

## Automated evidence

`scripts/test-release.ps1 -Configuration Release -Repeat 2` ran with the
PostgreSQL and large-dataset gates enabled.

| Iteration | Core | Results | PostgreSQL | Desktop | Integration | Result |
| ---: | ---: | ---: | ---: | ---: | ---: | --- |
| 1 | 235 | 65 | 54 | 33 | 72 | 459 passed |
| 2 | 235 | 65 | 54 | 33 | 72 | 459 passed |

Run ID `9ec79f1f71`: **918 passed, 0 failed, 0 skipped**. Database and role
cleanup succeeded.

## Deterministic package evidence

Two clean release builds from revision `516e655` produced the same package
SHA-256:

`A928BDB8600CD2E4787B0ADCF9AE0FEAE84FB5E01B07DE987073178568F01E7E`

`scripts/release/verify-package.ps1` validated the manifest, source identity,
archive checksum, inventory, and all 407 per-file hashes.

## Exact-package workflow evidence

Environment:

- Windows 11 x64 development host;
- PostgreSQL 18.4 local disposable database objects;
- packaged executable from `artifacts/release/stage-0.9.0-rc.4/app`;
- no IDE-hosted or source-run substitute.

Observed results:

- connection status: connected to local PostgreSQL;
- simple SQL: `SELECT * FROM s62_audit.example_table;`;
- Alt+F1 resolved the physical table;
- Replace `*` emitted `id`, `customer_id`, `payload`, `tags`, `mood`,
  `created_at` in catalogue order;
- undo restored the exact original and redo restored the expansion;
- repeated query execution returned two rows and did not crash;
- multiline import preview contained two valid logical records;
- review estimated two rows and selected validated typed parameter batches;
- existing-table execution read 2, imported 2, rejected 0, committed;
- round-trip read returned the expected JSON property, second array element,
  enum, and non-null timestamp for both rows.

## Complex-type qualification

The PostgreSQL integration matrix covers:

- `json` and `jsonb`;
- enum and domain;
- arrays;
- UUID and bytea;
- date, time, timestamp, timestamptz, and interval;
- inet;
- range and multirange;
- numeric;
- atomic rollback and batched partial completion;
- invalid JSON with logical-row/source/destination diagnostics.

The exact package additionally covered JSONB, `text[]`, enum, timestamptz,
multiline input, and typed fallback composition.

## Security checks

`dotnet list PostgreManagementStudio.sln package --vulnerable
--include-transitive` reported no vulnerable resolved packages from NuGet.
Credential-pattern review found no supplied local password in tracked source.
The package verifier also scans text payloads for seeded local credentials.

## Clean-machine evidence

No qualifying clean target was available:

- current host contains development SDKs;
- Windows Sandbox executable is absent;
- Hyper-V PowerShell module is absent;
- no VirtualBox or VMware command-line guest was found.

This is recorded as a failed qualification precondition, not as a pass.

## Evidence boundaries

The exact product defects are closed by automated and packaged evidence.
Release approval is withheld because SDK-free startup and the uninterrupted
25-step packaged sequence have not been demonstrated, and the artifact is
unsigned.
