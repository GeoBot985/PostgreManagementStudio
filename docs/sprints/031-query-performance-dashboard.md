# Sprint 31 — Query Performance Dashboard

Implemented the application-layer Query Performance Dashboard foundation on top of the existing `pg_stat_statements`-oriented statistics model.

## Delivered

- Explicit availability guidance for available, not-installed, not-preloaded, insufficient-permission, unsupported, and temporary states.
- Aggregate summary cards for tracked statements, calls, execution time, rows, shared reads, temporary writes, cache-hit ratio, and reset time.
- Centralized dashboard thresholds and quick filters for total time, mean time, frequency, variability, I/O, temporary writes, WAL, and cache hit.
- Safe cumulative-counter deltas with missing-entry, reset, and negative-counter protection.
- Explicit reset validation requiring availability, permission, and confirmation, with global/targeted wording.
- Readable adaptive duration/byte formatting and unit-test coverage for dashboard calculations.

## Boundary

The desktop does not yet expose a dedicated Query Performance document tab, and PostgreSQL catalog collection/capability detection/reset execution remain in the existing integration layer’s next increment. No automatic extension installation, statistics reset, query execution, persistent history, or cross-server aggregation was added.
