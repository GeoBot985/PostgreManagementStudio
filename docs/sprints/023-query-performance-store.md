# Sprint 23 — Query Performance Store and pg_stat_statements Analysis

Implemented the UI-independent query-performance analysis foundation.

## Delivered

- Immutable query-statistics snapshots with explicit timing units, capability flags, statistics-reset metadata, bounded history, and compound server/database/user/query identity.
- Deterministic ranking and filtering for total time, mean time, calls, maximum time, variability, rows, reads, temporary writes, WAL, and planning time.
- Conservative statement classification, including comments and common CTEs.
- Counter-delta interval calculations with reset, missing-query, negative-counter, and invalid-window handling.
- Conservative regression classification with configurable minimum calls, mean time, workload delta, and increase thresholds.
- Safe EXPLAIN template generation; ANALYZE warnings are emitted for data-changing statements.
- Unit coverage for identities, classification, ranking, interval math, reset handling, regression detection, EXPLAIN safety, and bounded history.

## Boundary

The desktop does not yet expose a dedicated Query Performance Store workspace, and the PostgreSQL collector does not yet add version-aware `pg_stat_statements` availability/query loading/reset commands. Those integration/UI pieces are explicitly left as the next increment; no automatic extension installation, statistics reset, query execution, or SQL rewriting was added.
