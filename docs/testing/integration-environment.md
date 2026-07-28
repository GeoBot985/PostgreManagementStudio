# Integration test environment

## Prerequisites

- Windows, .NET SDK 9, and PowerShell 7 or Windows PowerShell.
- A dedicated local PostgreSQL cluster or CI service container.
- `psql`, `pg_dump`, and `pg_restore` from the selected PostgreSQL installation.
- An administrative test connection allowed to create/drop databases and roles.

Do not point the runner at a production or shared business database. The script
only uses the supplied server as a host for uniquely named disposable objects.

## Running

```powershell
$env:PMS_ADMIN_CONNECTION_STRING = "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=<local-test-password>"
.\scripts\test-release.ps1
```

Optional parameters:

- `-PostgreSqlBin "C:\Program Files\PostgreSQL\18\bin"` selects tool binaries.
- `-Repeat 3` recreates one isolated environment and repeats the suite.
- `-SkipCoverage` omits Cobertura collection.
- `-KeepDatabase` preserves the random database for troubleshooting and reports
  cleanup as incomplete by design.

The PostgreSQL server/version is selected by `PMS_ADMIN_CONNECTION_STRING`;
tests contain no image tag or fixed server path. PostgreSQL 18.4 is primary and
PostgreSQL 14 is the minimum intended baseline pending Sprint 40.

## Lifecycle

Startup performs:

1. prerequisite/tool validation with non-interactive `psql --no-password`;
2. server readiness/version query;
3. unique application, read-only, and restricted login creation;
4. unique database creation;
5. deterministic seed loading;
6. production connection-string and tool environment setup.

Shutdown always attempts to terminate connections to the test database, drop
the database, and drop all three roles. The command fails if cleanup fails,
including after a test failure. `TestResults/<run-id>/release-summary.json`
records cleanup status.

## Seed

`scripts/testing/seed.sql` creates null and Unicode text, 8KB text, binary,
high-precision numeric, boolean, date, timestamp, timestamp-with-time-zone,
interval, UUID, JSON, JSONB, arrays, identity, generated columns, unique and
foreign-key constraints, indexes, a view, materialized view, sequence, enum,
function, procedure, and partitioned table. Names include spaces, case,
Unicode, and a reserved word.

Roles:

- application role owns the database objects;
- read-only role has connect, schema usage, and select;
- restricted role can connect but has no seeded-schema access.

No passwords are committed or included in summaries. They are random,
process-local values.

## Results and troubleshooting

TRX and Cobertura files plus `release-summary.json` are written under ignored
`TestResults/<run-id>/`. A missing server, password, tool, or privilege fails
before tests with the responsible command visible. Full SQL and credentials are
not written to the summary.

If a run is interrupted externally, query the server for
`pms_regression_%` databases and `pms_*_<run-id>` roles, then remove only the
exact run identifiers. Normally rerun without external termination so `finally`
performs cleanup. The design is CI-ready for a service container by supplying
its admin endpoint and tool directory; container orchestration itself remains a
CI concern.
