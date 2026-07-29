# Sprint 50 — Core Workspace Composition

## Outcome

Sprint 50 completed two priority workflows end to end: PostgreSQL restore and
database object search. The activity monitor was deliberately deferred because
the existing backend snapshot service did not yet support a truthful live
workspace. Its unfinished menu route was removed from the release shell.

## Delivered

- `Database > Restore` opens a durable restore workspace with archive selection,
  format inspection, exact target identity, supported options, progress/output,
  cancellation, destructive confirmation and failure presentation.
- `Tools > Search Objects` and `Ctrl+Shift+F` open a durable search workspace
  with server/database scope, supported type filters, definition/system-object
  filters, sortable results, cancellation, stale-result suppression, clear and
  qualified-name copy.
- Workspace instances are owned by the active query tab, reused while visible,
  and closed on tab unload or connection replacement. No sensitive state is
  persisted.
- The activity snapshot menu/command route is no longer exposed; the backend
  remains available for Sprint 51.

## Validation

The full solution test run passed: 327 tests passed, 60 integration tests were
skipped because they require the configured PostgreSQL integration environment,
and no tests failed. The desktop composition suite passed 22/22. The Release
build completed with zero warnings and zero errors, and `git diff --check`
reported no whitespace errors. Live desktop inspection confirmed the current Release executable,
connected local PostgreSQL shell, shared Search Objects route and absence of the
unfinished Activity Monitor menu item. No destructive restore was executed.

## Known limitations and follow-up

Restore still needs qualification across supported `pg_dump`/`pg_restore`
versions, archive types and post-restore metadata refresh. Search does not yet
activate an object designer or retain search history. Sprint 51 should compose
the live activity grid with refresh/filter/pause, identity-safe selection,
permission-aware cancel/terminate actions and connection-loss reconciliation.

See the [reachability matrix](../audits/UI_REACHABILITY_MATRIX.md), [release
scope reset](../planning/RELEASE_SCOPE_RESET.md), and [workspace evidence](../audits/ui-reachability-evidence/sprint-50-workspaces.md).
