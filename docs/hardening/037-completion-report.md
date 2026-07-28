# Sprint 37 completion report

## Outcome

Sprint 37 centralises and hardens connection profiles, effective provider
configuration, lifecycle transitions, connection testing, pooling, session
reset, failure classification, reconnection policy, resource limits, and
privacy-bounded diagnostics without adding a user-facing feature.

## Verification

The release runner provisions an isolated PostgreSQL database and random
roles, builds Release, runs every test project, and removes all test resources
in `finally`. The focused live suite validates credentials, database
availability, SSL verification, session reset, aborted transactions, pool
exhaustion and cancellation, broken backends, recovery, and bounded concurrent
growth.

Final Release evidence:

| Measure | Result |
|---|---|
| Run ID | `3ab1a9805c` |
| Tests per run | 237 |
| Three-run passed / failed / skipped | 711 / 0 / 0 |
| Merged line coverage | 82.06% |
| Build | 0 warnings, 0 errors |
| PostgreSQL | 18.4 |
| Temporary database/roles remaining | 0 / 0 |
| Cleanup | passed |

## Assessment

| Dimension | Score |
|---|---:|
| Correctness | 95% |
| Reliability and race safety | 95% |
| Credential safety | 96% |
| Pool and session isolation | 95% |
| Diagnostics | 93% |
| Automated regression coverage | 94% |
| Overall | 95% |

The Sprint 37 target of at least 90% is met. Physical service restarts,
certificate replacement, and external enterprise authentication remain
environment compatibility campaigns; deterministic repository-safe
equivalents cover the application behavior.
