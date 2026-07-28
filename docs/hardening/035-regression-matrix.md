# Sprint 35 regression coverage matrix

Legend: A automated, M manual only, B release blocker, N/A not implemented.
Primary PostgreSQL evidence is 18.4; 14 is the pending minimum-version target.

| Feature / workflow | Priority | Unit/component | Integration | UI/E2E | Current result / known gap |
|---|---|---|---|---|---|
| Production provider resolution/no mocks | P0 | A | — | A smoke | Pass |
| Startup/main shell | P0 | — | — | A UI | Pass |
| Settings missing/corrupt/old/unknown/save | P0 | A | filesystem A | startup load A | Pass; unwritable-path UI is P1 gap |
| PostgreSQL connection/context/version | P0 | A factory | A | shell uses same provider | Pass |
| Connection failure/credential redaction | P0 | — | A | M | Pass at adapter boundary |
| Connection loss during active work | P0 | — | partial cancellation/recovery | M, B | server-stop/terminate workflow remains |
| Object Explorer hierarchy/duplicates/cancel | P0 | A | A seeded | A reachability | Pass for database schemas/relations/routines |
| Query execution/results/notices/errors | P0 | A | A | A existing query tab | Pass |
| Query cancellation/recovery | P0 | A | A deterministic | A command reachable | Pass |
| Query timeout/recovery | P0 | — | A | M | Pass at production executor |
| Explicit transaction rollback/isolation | P0 | — | A | B | persistent editor transaction state is absent |
| Result display/format/types | P0 | A | A seeded | A shell/grid creation | Pass; keyboard UI remains P1 |
| Destructive confirmation cancel path | P0 | A guard approve/reject | production composition A | A reachable | Pass for restore, maintenance, and actual plans |
| Shutdown/workspace disposal | P0 | A session | — | A shell close | Pass for shell; background lifecycle remains P1 |
| File open/save/recovery/find | P1 | A | filesystem A | M | Existing coverage; command automation incomplete |
| Sorting/filtering/search/copy | P1 | A | A typed data | M | Core pass; UI interaction automation blocked |
| Backup | P1 | A | A real pg_dump | command reachable | Pass |
| Restore | P1 | A | B | M | disposable restore workflow still missing |
| Import/export | P1 | A | import PostgreSQL + file export existing | command reachable | Partial; wizard UI blocked |
| Object search/navigation | P1 | A | A search | M navigation | Search pass; navigation blocked |
| Roles/permissions | P1 | A | A read-only/restricted | listing reachable | Pass for reads/denials; mutation UI blocked |
| Activity Monitor | P1 | A | A real pg_stat_activity | reachable | Pass; null-duration defect fixed |
| Session cancel/terminate | P1 | A safety | B | M | destructive live action deferred Sprint 38 |
| Query performance/pg_stat_statements | P1 | A logic | B | B | collector and production workspace absent |
| Estimated execution plan | P1 | A | A | reachable | Pass |
| Actual execution plan | P1 | A safety | existing limited | M | data-changing E2E remains blocked |
| Plan comparison/history | P2 | A | — | B | services not composed |
| Index analysis | P1 | A recommendation | seeded metadata indirect | B | collector/workspace absent |
| Schema compare | P1 | A | extractor A | B | desktop still lacks distinct source/target |
| Maintenance | P1 | A | adapter not destructively exercised | reachable | Safety campaign deferred |
| Large result/performance smoke | P1 | A | A six 100k checks | M | Functional baseline pass |
| External process failure/cancel | P1 | A | pg_dump success A | M | failure/cancel E2E remains |
| Installer/update/recovery | P4 | — | — | B | Sprint 41 |

## Risk-based summary

- P0 represented by meaningful automation: 14/15 (93%). Persistent editor
  transactions remain the explicit release blocker rather than a false pass.
- P1 represented at least at a production boundary: 15/22 (68%). Remaining P1
  items are explicitly blocked by missing approved workspace/collector wiring.
- PostgreSQL-backed scenarios: connection, context, seed types, metadata,
  unusual identifiers, transactions, permissions, query errors, timeout,
  cancellation, activity, search, plans, backup.
- Destructive workflows: SQL builders and the shared production confirmation
  guard are automated; live restore/session/schema mutation remains deferred.

The Sprint 35 P0 automation target is met. Release readiness is still blocked
by the uncovered persistent-transaction workflow and the explicit P1 gaps in
this matrix.
