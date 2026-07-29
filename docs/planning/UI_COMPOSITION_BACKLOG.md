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

- Completed workflows: target-aware database maintenance and structured
  execution-plan exploration.
- Composition delivered: `MaintenanceWorkspaceWindow` provides supported
  operation selection, version-aware validation, SQL preview, target-aware
  confirmation, genuine progress messages, cancellation and retry;
  `PlanExplorerWindow` provides operator search/grid/details, raw-plan viewing,
  deterministic warnings and raw-plan save.
- Query History remains deferred because execution capture and privacy-aware
  persistence are not yet composed into the lifecycle. Live activity remains
  deferred because refresh and session-action identity safeguards need a full
  workspace.
- Acceptance: met for maintenance and plan exploration; see
  `docs/sprints/SPRINT_51_REPORT.md` and Sprint 51 evidence.
- Risk: medium; target selection beyond the active database and history remain.

## Sprint 52 — Execution-plan explorer and SQL completion

- Completed workflows: index inventory/reindex and explicit two-source schema
  comparison with synchronisation preview/script review.
- Composition delivered: `IndexWorkspaceWindow` uses PostgreSQL catalogue and
  statistics data with filtering, refresh, definition copy and target-aware
  reindex; `SchemaComparisonWorkspaceWindow` validates distinct source/target
  databases, compares asynchronously, displays structured differences and
  generates a non-executing source-to-target preview script.
- Deferred: index create/drop editor, dependency extraction, snapshot sources,
  direct synchronisation execution, query history, activity monitoring and SQL
  completion.
- Acceptance: met for the delivered workflows; see
  `docs/sprints/SPRINT_52_REPORT.md` and Sprint 52 evidence.
- Risk: medium; cross-version/catalogue permissions and dependency scope remain.

## Sprint 53 — Data-transfer wizard and transaction state

- Target workflows: import mapping/review/progress/rejected rows; explicit
  transaction state and safe rollback/recovery.
- Backend support: data-transfer wizard models, mapping/validation, query
  transaction and recovery policies.
- Composition required: source/destination preview, type mapping, transaction
  choice, progress, rejected-row report, commit/rollback state presentation.
- Unknowns: supported export destinations and editable-grid boundary.
- Dependencies: result presentation and connection-state model.
- Acceptance: met for the delivered delimited-file import and retained-result
  export workflows; PostgreSQL-to-PostgreSQL transfer remains deferred.
- Risk: medium; destination metadata is currently supplied by the existing
  import adapter and database/object export is not composed.

## Sprint 54 — Performance history and query workspace

- Target workflows: query-performance availability, history, filters, local
  baselines and drill-through to editor/plan.
- Backend support: performance/history/dashboard models and PostgreSQL adapters.
- Composition required: durable grid/dashboard, extension/permission states,
  baseline persistence, navigation to query and plan documents.
- Unknowns: retention and privacy policy for SQL text.
- Dependencies: plan explorer and query-history decisions.
- Acceptance: reduced scope met for the server activity dashboard, blocking/
  lock diagnostics, safe session actions, and activity snapshot export. Query
  performance and database statistics remain deferred because their PostgreSQL
  adapters are not present.
- Risk: medium; query/database statistics need real adapters before composition.

## Sprint 55/56 — Administration workspaces

- Sprint 55: shell consistency, command routing, context targeting and
  workspace lifecycle policy completed; maintenance/security/index gaps remain
  separately tracked.
- Sprint 56: schema compare, synchronisation preview and script export.
- Backend support: existing maintenance, security, index, schema extractor,
  planner and preview services.
- Composition required: dedicated workspaces with permissions, target identity,
  review/confirmation, structured output and persistence.
- Unknowns: product policy for destructive administration and supported scope.
- Dependencies: activity/session identity and restore safety patterns.
- Acceptance: Sprint 55 shell policy is complete; administration workflows that
  remain service-only are not claimed as desktop-complete.
- Risk: medium; the next administration workflow needs a deliberate scope.
