# UI composition backlog

The backlog is sequenced by coherent end-to-end workflows. Each sprint should
finish one usable surface rather than expose another collection of buttons.

## Sprint 50 — Restore workspace and release-shell persistence

- Completed workflows: restore inspection/target review/progress/cancellation and
  database object search with filtering, sortable results and copy action.
- Backend support: `BackupRestoreOperationService`, inspection/validation,
  atomic output, `ApplicationSettings`, recovery snapshots.
- Composition delivered: `RestoreWorkspaceWindow` and
  `ObjectSearchWorkspaceWindow`, routed from the shared shell with singleton
  lifetime per query tab and cancellation/disposal on close or connection change.
- Deferred: live activity monitor; settings/layout persistence remains outside
  this sprint because no safe user-facing options contract is composed yet.
- Dependencies: current release shell and destructive-operation guard.
- Acceptance: met for the delivered restore/search workflows; see
  `docs/sprints/SPRINT_50_REPORT.md` and Sprint 50 evidence.
- Risk: medium, with external PostgreSQL tool/version qualification still open.

## Sprint 51 — Database search and live activity workspace

- Target workflows: activity refresh/filter/select; session
  cancellation/termination with confirmation; optional object activation/history
  polish for the Sprint 50 search workspace.
- Backend support: object search, activity snapshot/presentation,
  session-management service, recovery identity safeguards.
- Composition required: searchable result grid, object activation, live refresh
  coordinator, stale-state banner, selection identity, role-aware actions.
- Unknowns: permission/error wording and safe refresh interval under load.
- Dependencies: Object Explorer selection model and command routing standard.
- Acceptance: search result opens the correct object; activity rows remain
  identity-safe across refresh; cancel/terminate is explicit and auditable;
  connection loss does not act on stale selection.
- Risk: large.

## Sprint 52 — Execution-plan explorer and SQL completion

- Target workflows: import/display/search plan tree; estimated/actual plan
  review; visible SQL completion list.
- Backend support: plan parser/explorer/warnings, execution-plan service,
  `SqlCompletionEngine` and latest-request coordination.
- Composition required: durable plan workspace, node navigation, warning
  filters, plan import/export, completion popup with keyboard semantics and
  metadata context.
- Unknowns: PostgreSQL version capability display and editor control choice.
- Dependencies: query document targeting and structured result presentation.
- Acceptance: plan warnings point to nodes; actual execution is confirmed;
  completion never inserts stale results; all failure/cancel states recover.
- Risk: large.

## Sprint 53 — Data-transfer wizard and transaction state

- Target workflows: import mapping/review/progress/rejected rows; explicit
  transaction state and safe rollback/recovery.
- Backend support: data-transfer wizard models, mapping/validation, query
  transaction and recovery policies.
- Composition required: source/destination preview, type mapping, transaction
  choice, progress, rejected-row report, commit/rollback state presentation.
- Unknowns: supported export destinations and editable-grid boundary.
- Dependencies: result presentation and connection-state model.
- Acceptance: preview is required before write; cancellation and rollback have
  truthful outcomes; no partial result is labelled complete.
- Risk: large.

## Sprint 54 — Performance history and query workspace

- Target workflows: query-performance availability, history, filters, local
  baselines and drill-through to editor/plan.
- Backend support: performance/history/dashboard models and PostgreSQL adapters.
- Composition required: durable grid/dashboard, extension/permission states,
  baseline persistence, navigation to query and plan documents.
- Unknowns: retention and privacy policy for SQL text.
- Dependencies: plan explorer and query-history decisions.
- Acceptance: unavailable `pg_stat_statements` is explicit; baseline identity is
  database/server-safe; no credential or sensitive SQL leakage.
- Risk: large.

## Sprint 55/56 — Administration workspaces

- Sprint 55: maintenance, security roles, index review/recommendations.
- Sprint 56: schema compare, synchronisation preview and script export.
- Backend support: existing maintenance, security, index, schema extractor,
  planner and preview services.
- Composition required: dedicated workspaces with permissions, target identity,
  review/confirmation, structured output and persistence.
- Unknowns: product policy for destructive administration and supported scope.
- Dependencies: activity/session identity and restore safety patterns.
- Acceptance: every destructive step is explicit, reviewable, cancellable where
  possible, and reconciled after completion; no service-only claim remains.
- Risk: large.
