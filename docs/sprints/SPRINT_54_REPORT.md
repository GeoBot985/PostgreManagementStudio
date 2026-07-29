# Sprint 54 — Performance Dashboard, Diagnostics, and Monitoring Composition

## Outcome

The reduced Sprint 54 scope is complete for the monitoring capabilities backed
by a real PostgreSQL adapter: a server activity dashboard, blocking/wait and
lock diagnostics, safe session actions, and privacy-aware activity snapshot
export. Query-performance and database-statistics dashboards remain explicitly
deferred because their source adapters are not present.

## Delivered

- Added `MonitoringWorkspaceWindow`, bound to one explicit server/connection
  identity.
- Added shared `Performance Dashboard` and `Blocking Diagnostics` commands,
  menu routes and Object Explorer context-menu routes.
- Added Activity, Blocking and waits, Locks, and Diagnostic output tabs.
- Added manual refresh, opt-in automatic refresh, bounded interval choices,
  pause/resume, last-successful-refresh state, refresh errors and lifecycle
  cancellation.
- Added target-aware Cancel query and Terminate session actions. Termination
  uses the existing protected-backend safety contract; both actions revalidate
  the selected session against a fresh snapshot before sending a request.
- Added JSON and CSV diagnostic snapshot export with atomic temporary output,
  timestamped snapshot metadata, explicit omitted sections, bounded query
  previews, and privacy-safe defaults.
- Added WPF reachability coverage and updated the source-of-truth matrix,
  backlog and feature-claim corrections.

## Semantics and safety

Activity values are point-in-time observations from `pg_stat_activity` and
blocking relationships from `pg_blocking_pids`; the UI does not fabricate
rates, zero-fill unavailable statistics, or merge sessions from another
connection. A refresh error leaves the last snapshot visible and states the
error. Automatic refresh stops when the workspace closes. Diagnostic exports
exclude credentials and connection strings and omit query text by default.

## Verification

- Release solution build: 0 warnings, 0 errors.
- Focused Sprint 54 WPF test: passed.
- Full solution tests: 332 passed, 60 PostgreSQL integration tests skipped
  because the integration environment is not configured, 0 failed.

## Deferred scope

- `pg_stat_statements` query performance adapter, availability grid, filters,
  query drill-through and reset workflow.
- Database-level/table-level statistics adapter and database performance
  workspace.
- Counter-rate time series, persisted monitoring filters and multi-server
  dashboard layout.
- Full support bundle and richer lock catalog fields where the current
  activity adapter does not return them.

Evidence: `docs/audits/ui-reachability-evidence/sprint-54-monitoring-workspace.md`.
