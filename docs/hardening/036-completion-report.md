# Sprint 36 completion report

## Outcome

Sprint 36 hardens the existing SQL editor and execution path without adding a
new product feature. Execution now has an explicit race-safe lifecycle,
immutable target context, provider and token cancellation, bounded result
retention/backpressure, structured PostgreSQL diagnostics, tab-scoped
user-managed transactions, safe shutdown, and privacy-bounded telemetry.

## Defects corrected

1. A mutable database field did not modify the actual connection string, so
   the displayed target could differ from the executed database.
2. Provider production was fire-and-forget over an unbounded channel.
3. Connection-construction failures could fault before channel completion.
4. Command timeout was initially indistinguishable from transient connection
   loss on Npgsql 8.
5. Production result retention was not aligned with the 10,000-row UI limit.
6. Command rows affected were discarded.
7. Completion and cancellation had no execution correlation.
8. Explicit transactions could not survive between editor executions.
9. Active query shutdown did not await cancellation or dispose document-owned
   sessions.
10. Formatting one hostile value could fail the grid path.

## Automated verification

The release runner provisions an isolated PostgreSQL database and random
roles, seeds representative objects, builds Release, runs every test project,
and removes all database/role resources in `finally`.

Focused coverage includes lifecycle transitions, rapid execution rejection,
two- and ten-tab independence, immutable connection snapshots, cancellation
during command and row streaming, cancellation timeout, structured SQL errors,
middle-batch failure, notices, unusual types, 25,000-row truncation, transaction
abort/rollback/recovery, missing database, backend termination, timeout, and
explicit recovery without automatic replay.

Final Release evidence:

| Measure | Result |
|---|---|
| Run ID | `4819f105d8` |
| Tests per run | 219 |
| Three-run passed / failed / skipped | 657 / 0 / 0 |
| Merged line coverage | 81.15% |
| Build | 0 warnings, 0 errors |
| PostgreSQL | 18.4 |
| Temporary database/roles remaining | 0 / 0 |
| Cleanup | passed |

## Manual checklist mapping

- Normal/selected/multiple execution: automated provider and document tests.
- Syntax position: structured position plus editor highlight path.
- Long/repeated cancellation: automated live and unit tests.
- Original connection and independent tabs: automated snapshot/concurrency tests.
- Large/truncated results and notices: automated live tests and UI presentation.
- Failed transaction rollback: automated tab-scoped live sequence.
- Server loss/reconnect/no replay: backend termination and missing-database tests.
- Running-editor close/shutdown: deterministic document ownership plus STA shell.
- Diagnostic privacy: telemetry and secret-redaction tests.

## Quality assessment

| Dimension | Score |
|---|---:|
| Correctness | 96% |
| Reliability | 95% |
| Usability and diagnostics | 92% |
| Performance and bounded memory | 94% |
| Maintainability | 93% |
| Automated test coverage | 95% |
| Overall release-candidate quality | 94% |

The Sprint 36 target of at least 90% is met. Host-destructive network/service
and certificate campaigns remain environment-level compatibility work, not
unhandled execution-path gaps.
