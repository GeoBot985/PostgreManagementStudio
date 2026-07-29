# Sprint 52 workspace evidence

## Index management

Route: connect to PostgreSQL, then `Tools > Index Management`,
`Ctrl+Shift+I`, or the Object Explorer context menu. The workspace loads
catalogue metadata and `pg_stat_all_indexes` observations asynchronously. It
shows schema, table, index, access method, uniqueness, validity, size and scan
count; supports name/schema/table filtering, valid/unique filters, refresh,
definition copy and target-aware reindex. Reindex uses the detected PostgreSQL
major version and the existing maintenance service. Create/drop/editor actions
are deliberately not exposed.

## Schema comparison and synchronisation preview

Route: connect to PostgreSQL, then `Tools > Compare Schemas`,
`Ctrl+Shift+C`, or the Object Explorer context menu. The active connection is
the explicit source. The user must enter a distinct target host/database/user
and password in a masked field. Self-comparison is blocked. Source and target
schemas are extracted asynchronously with cancellation; differences show
object kind, source/target presence, change, risk and action. A source-to-target
preview is generated through the existing planner, excludes destructive steps
by default, marks blocked/manual actions and supports copy/save without
execution. No credentials are included in generated SQL.

## Limitations

The current extractor compares permitted non-system schemas and relation-level
objects already supported by the repository. File snapshots, schema-scope
filtering, dependency extraction, object-level selection, full index create/drop
editing, and direct synchronisation execution remain deferred.

## Automated evidence

- `Sprint52_CommandSurfaceUsesSharedIndexAndSchemaRoutes`
- `Sprint52_WorkspacesExposeExplicitTargetsAndSafePreviewControls`
- Existing schema/index/planner tests
- Final solution and desktop test results are recorded in
  `docs/sprints/SPRINT_52_REPORT.md`.
