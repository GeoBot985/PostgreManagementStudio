# Sprint 22 — Activity Monitor and Session Management

Implemented the operational monitoring foundation and hardened session-management safety.

## Delivered

- Existing PostgreSQL activity collection and explicit cancel/terminate actions remain parameterised and permission-aware.
- Session classification distinguishes active, waiting, blocked, idle, idle-in-transaction, aborted idle transactions, background workers, and unknown states.
- Added deterministic blocking graph construction with duplicate-edge removal, cycle detection, missing/inconsistent snapshot warnings, and bounded recursive traversal.
- Added valid transaction-rate and rollback-ratio calculations from two metric samples, including statistics-reset handling.
- Existing bounded snapshot/metric/action histories, stale-refresh coordination, redacted exports, filters, and protected-backend checks remain covered by tests.

## Boundary

The desktop currently exposes Activity Monitor from the query-tab command surface. The collector’s full locks/metrics tabs, dedicated virtualised workspace, multi-session action UI, and richer version-capability catalog queries remain follow-up integration/UI work. Administrative actions are never automatic and no persistent activity history is introduced.
