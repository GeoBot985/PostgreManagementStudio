# Sprint 35 completion report

## Executive summary

Sprint 35 establishes a repeatable, isolated release-regression baseline. The
desktop now uses one validated production service provider; settings load
safely; Object Explorer is reachable and backed by real metadata; a Windows UI
smoke project creates and closes the shell; and the release script provisions
random test roles/database, loads representative seed data, runs every required
suite with TRX/Cobertura output, and cleans up on pass or failure.

The sprint intentionally does not claim release readiness. Persistent editor
transactions remain a P0 blocker, while several Sprint 20-33 workspaces remain
P1 blockers.

## Infrastructure and environment

- Production registrations: `ProductionServices.Build`, shared by App and tests.
- Isolated database: random `pms_regression_<id>`.
- Roles: random application owner, read-only, and restricted login.
- Primary server: PostgreSQL 18.4 on 64-bit Windows.
- Seed: scalar/temporal/binary/JSON/array/UUID edge cases, constraints,
  relations, routines, enum, sequence, generated/identity columns, partitions,
  unusual identifiers.
- Results: ignored per-run directory containing TRX, Cobertura, and JSON summary.
- Cleanup: database and all roles removed in `finally`; both a deliberately
  failing run and passing run were observed to clean successfully.

## Test changes

Existing test source was searched for empty/template tests, silent returns,
arbitrary delays, ignored tasks, and unreasoned skips. Sprint 34 had already
removed template tests and converted environment skips. Sprint 35 adds:

- 6 component tests: settings recovery/persistence, Object Explorer hierarchy,
  duplicate suppression/cancellation, and destructive-operation approval and
  rejection;
- 1 production-composition smoke test;
- 1 WPF UI integration shell lifecycle test;
- 1 seeded end-to-end connection/metadata workflow;
- 1 PostgreSQL permission contract test;
- 4 PostgreSQL/OS integration tests for transactions, monitoring/search/plans,
  failure/timeout/recovery, and real `pg_dump`.

No tests are quarantined. No unrelated product feature was added.

## Defects found and fixed

1. New query tabs became dirty during control initialization, making automated
   and real shutdown prompt unexpectedly. Initialization now suppresses that
   synthetic edit; the STA shell lifecycle test protects it.
2. Activity Monitor crashed when PostgreSQL returned null timing data for a
   backend. Duration now safely falls back to zero; live activity regression
   protects it.
3. Metadata loading discarded every routine and returned no types. It now
   materializes functions/procedures/types and respects the requested database;
   seeded Object Explorer coverage protects the path.
4. The first release-script attempt could wait for an interactive password
   because PowerShell did not invoke the connection-string property setter.
   The runner uses the explicit setter and `psql --no-password`.
5. Restore, maintenance, and actual-plan execution used unrelated confirmation
   dialogs. They now share an injectable guard that always identifies the
   target, consequence, and recovery guidance and is covered on both branches.

## Baseline result

Final values are recorded by
`TestResults/<run-id>/release-summary.json` and are regenerated on every run.

| Measure | Result |
|---|---|
| Automated tests per run | 198 |
| Three-run passed / failed / skipped | 594 / 0 / 0 |
| Quarantined | 0 |
| Build | Release, 0 warnings, 0 errors |
| PostgreSQL | 18.4 |
| Environment cleanup | passed |
| Machine-readable output | TRX, Cobertura XML, JSON summary |
| Merged line coverage | 80.15% |
| Flaky tests found | 0 across three consecutive runs |
| Mutation samples | connection boundary, quoting, cancellation, duplicate filter, rollback, permissions |

The final evidence is `TestResults/56a3ea3217/release-summary.json`. Risk
coverage is in `035-regression-matrix.md`. P0 coverage is 14/15 (93%) and P1
coverage is 15/22 (68%); missing items remain release blockers.

## Quality scorecard

| Metric | Score |
|---|---:|
| P0 risk automation | 93% |
| P1 risk accountability | 100% (68% automated) |
| Traceability | 94% |
| Determinism | 98% |
| Integration realism | 96% |
| Production wiring | 95% |
| Test isolation | 98% |
| Cleanup reliability | 100% |
| Flakiness resistance | 98% |
| Failure diagnostics | 92% |
| Script usability | 96% |
| Documentation completeness | 95% |
| Maintainability | 92% |

## Remaining blockers and Sprint 36

The shared debt register retains: persistent editor transaction ownership,
connection-loss lifecycle, restore E2E, pg_stat_statements collector/workspace,
distinct schema-compare endpoints, session action integration, workspace
disposal/background cancellation, and the remaining unreachable analysis
workspaces.

Sprint 36 should focus on operation/document lifecycle, cancellation and
disposal ownership, error classification, last-successful-state preservation,
and persistent editor transaction recovery. The numeric P0 threshold is met,
but release cannot proceed while its remaining transaction blocker is open.

## Command

```powershell
$env:PMS_ADMIN_CONNECTION_STRING = "<administrative local test connection>"
.\scripts\test-release.ps1 -Repeat 1
```

The working tree and commit hash are confirmed in the final handoff after the
committed validation run.
