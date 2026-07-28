# Sprint 35 test strategy

## Purpose and priorities

The release baseline proves workflows and production wiring, not test count.
Priorities are:

- **P0 release-critical:** startup, settings, production provider, PostgreSQL
  connection/context, Object Explorer load, query/result execution,
  transaction rollback/isolation, cancellation/recovery, connection failure,
  destructive-action guard coverage, and shutdown.
- **P1 high-risk:** permissions, backup tools, import/export, search,
  monitoring, plans, index recommendations, file safety, and workspace refresh.
- **P2 important:** productivity, history, comparison, diagnostics, and
  non-destructive transformations.
- **P3 supporting:** helper formatting and secondary exports.
- **P4 deferred:** compatibility matrix, accessibility, installer, visual
  regression, chaos, and scale profiling assigned to later hardening sprints.

An automated P0 failure fails `scripts/test-release.ps1`. A P0 or P1 workflow
without meaningful automation is an explicit release blocker in the regression
matrix; manual checks are never counted as automated coverage.

## Taxonomy

| Category | Boundary |
|---|---|
| Unit | one deterministic production unit, no OS/database |
| Component | multiple production classes behind a controlled boundary |
| Contract | PostgreSQL/file/process/service assumptions |
| Integration | real PostgreSQL or operating-system dependency |
| UI integration | real WPF view/composition/command state on STA |
| End-to-end | application-level entry through observable workflow outcome |
| Smoke | small prerequisite gate before deeper regression |
| Manual | documented human verification, excluded from automated totals |

New tests use `Feature_Scenario_ExpectedOutcome` naming and `Category` and
`Priority` traits. Environment-sensitive facts report an explicit skip reason:
`PostgreSqlFact`, `SeededPostgreSqlFact`, `ExternalToolsFact`, and
`PerformanceFact`. Required release runs configure every one; therefore the
release baseline permits no skips.

## Environment and isolation

The release script accepts an administrative connection through
`PMS_ADMIN_CONNECTION_STRING`, discovers PostgreSQL tools, creates a random
database and three random login roles, loads `scripts/testing/seed.sql`, sets
process-local test connection strings, runs the suite, and removes all database
and role state in `finally`. Passwords are random per run and never written to
the repository or summary. Database and role identifiers include a GUID
fragment, so parallel runs do not share objects.

Primary version is PostgreSQL 18.4. Minimum intended hardening target is
PostgreSQL 14. The admin endpoint and tool directory are external inputs, so a
future container or version-specific service can be selected without modifying
tests. Sprint 40 owns the full matrix.

## Production wiring and mocking

`ProductionServices.Build` is the single registration source used by both the
WPF application and composition tests. Provider validation is enabled.
Production registrations must resolve Npgsql adapters and filesystem settings;
fake, mock, sample, in-memory PostgreSQL, and always-success registrations are
forbidden. Unit/component tests may use explicit boundary fakes, but integration
and smoke tests use production implementations.

## Reliability policy

- Tests must not depend on order, developer schemas, fixed roles, shared files,
  current culture, or persisted credentials.
- Database tests use the isolated database or test-owned temporary objects.
- Every external prerequisite is required by the release script or explicitly
  skipped in unsupported ad-hoc runs.
- Arbitrary retry-to-green is forbidden. A repeated run must pass every
  iteration; intermittent failures become release blockers.
- Quarantine requires `Flaky` metadata, a debt identifier, and exclusion from
  reliable pass totals. Sprint 35 has no quarantined tests.
- Cleanup failure changes the release command exit code to failure.

## Mutation sampling

Architecture tests fail if connection construction bypasses the factory or a
forbidden project reference is added. Identifier tests fail if quote escaping
is removed. Object Explorer cancellation and duplicate suppression tests fail
if token propagation or filtering is removed. Transaction integration fails if
rollback becomes commit. Permission tests fail if grants are broadened.
Cancellation/recovery tests fail if cancellation is removed or misclassified.

## Coverage policy

Risk-based coverage is authoritative. Raw Cobertura line coverage is collected
for diagnostic use and merged by source line in the run summary. P0/P1 gaps are
not excused by high coverage elsewhere. Low-value assertions must not be added
to inflate percentages.

## Commands

```powershell
dotnet restore PostgreManagementStudio.sln --force-evaluate
dotnet build PostgreManagementStudio.sln --configuration Release --no-restore
dotnet test tests/PostgreManagementStudio.Core.Tests --configuration Release
dotnet test tests/PostgreManagementStudio.Results.Tests --configuration Release
dotnet test tests/PostgreManagementStudio.Postgres.Tests --configuration Release
dotnet test tests/PostgreManagementStudio.Desktop.Tests --configuration Release
dotnet test tests/PostgreManagementStudio.IntegrationTests --configuration Release
```

The authoritative release command is:

```powershell
$env:PMS_ADMIN_CONNECTION_STRING = "<local administrative test connection>"
.\scripts\test-release.ps1 -Repeat 1
```

Use `-Repeat 3` for the standard flakiness pass, `-SkipCoverage` for a local
diagnostic iteration, and `-KeepDatabase` only while troubleshooting.
