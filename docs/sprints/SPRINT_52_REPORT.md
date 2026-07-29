# Sprint 52 — Schema, Index, and Synchronisation Workspace Composition

## Repository and carry-over reviewed

The sprint began at commit `a58d9ec` (Sprint 51). Sprint 50 restore/search
workspaces and Sprint 51 maintenance/plan workspaces were reviewed and remain
reachable. Sprint 51 Query History and live Activity Monitor remain explicitly
deferred because their lifecycle and identity-safe composition is substantial;
they were not hidden behind new commands.

## Scope selected

The expected order was followed with a safe reduced scope. Index Management was
completed. Schema Comparison and Synchronisation Preview were delivered as one
coherent two-source review workflow. Direct synchronisation execution was not
included because target refresh, dependency extraction, execution boundaries and
partial-completion recovery are not yet mature enough for a safe desktop action.

## Completed workflows

### Index Management

`Tools > Index Management`, `Ctrl+Shift+I`, and the Object Explorer context menu
open a durable workspace scoped to the active server/database. It loads
catalogue metadata and `pg_stat_all_indexes` observations asynchronously,
supports name/schema/table filtering, valid/unique filters, refresh, readable
definition/details, copy, and target-aware reindex. Reindex capability is
version-aware and uses the existing maintenance service and confirmation guard.
No automatic drop or recommendation execution is exposed.

### Schema Comparison and Synchronisation Preview

`Tools > Compare Schemas`, `Ctrl+Shift+C`, and the Object Explorer context menu
open a durable workspace. The active connection is explicit source context;
the user supplies a distinct target with a masked password field. Same-database
self-comparison is blocked. Comparison runs asynchronously with cancellation,
stale/disposal protection, structured differences, source/target definitions,
risk/action display, and a source-to-target non-executing preview script. The
planner orders available steps, excludes destructive changes by default, marks
manual/unsupported actions, and supports copy/save.

## Classification changes

- Index inspection/recommendations: `SERVICE_ONLY` → `END_TO_END_REACHABLE` for
  inspection and reindex; recommendations/create/drop remain deferred.
- Schema comparison: `SERVICE_ONLY` → `END_TO_END_REACHABLE` for supported live
  relation/schema extraction and comparison.
- Synchronisation planning/preview: `SERVICE_ONLY` → `END_TO_END_REACHABLE` for
  review/export only; execution remains deferred.

No feature is classified `RELEASE_QUALITY` solely from this sprint.

## Safety, version handling, and persistence

Index reindex confirms the exact server/database/schema/index target and uses
detected PostgreSQL capabilities. Schema comparison requires distinct explicit
sources, blocks self-comparison, masks target passwords, and never writes
credentials to generated scripts. Destructive synchronisation steps are not
included by default and are visibly classified when preview inclusion is
enabled. No sensitive or transient state is persisted; workspaces close and
cancel background work on connection replacement or tab unload.

## Files and evidence

Implementation includes `NpgsqlIndexAnalysisService.cs`, `IndexWorkspaceWindow.cs`,
`SchemaComparisonWorkspaceWindow.cs`, `MainWindow.xaml(.cs)`,
`QueryTabView.xaml.cs`, shell commands, and desktop tests. Evidence is in
`docs/audits/ui-reachability-evidence/sprint-52-workspaces.md`; the matrix,
release scope, backlog, and feature-claim register were updated.

## Validation

- Release build: 0 warnings, 0 errors.
- Focused desktop validation: 25 tests after Sprint 52 additions.
- Full solution validation: 330 passed, 60 environment-gated integration skips,
  0 failures.
- No reindex, schema mutation, or synchronisation execution was run during UI
  qualification.

## Remaining gaps and Sprint 53 recommendation

Sprint 53 should add dependency-aware selection, snapshot/file sources, object-
level index create/drop review, target Object Explorer refresh, and a separate
decision on safe synchronisation execution. Query History, live Activity
Monitor, SQL completion, and the data-transfer wizard remain service-only or
partial follow-up work.
