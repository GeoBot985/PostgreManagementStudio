# Sprint 51 workspace evidence

## Maintenance

Route: connect to PostgreSQL, then `Database > Maintenance` or the Object
Explorer context menu. The workspace shows the exact active server/database and
the selected supported operation. It obtains the PostgreSQL major version
before generating the preview, rejects invalid combinations through the shared
maintenance plan validator, confirms the exact target, reports real service
messages, supports cancellation, and remains usable after failure or
completion. It currently targets the active database; schema/table/index
selection is intentionally deferred.

## Execution Plan Explorer

Routes: `Query > Display Estimated Execution Plan`, include an actual plan and
execute, then use `View > Execution Plan`. The explorer is a durable owned
workspace associated with the generated plan. It provides a searchable operator
grid, selected-operator details, raw JSON, deterministic warnings and raw-plan
save. Estimated and actual values are labelled separately; unavailable metrics
are displayed as unavailable. Large raw output remains bounded in the legacy
output tab while the workspace retains the service-produced raw plan.

## Deferred workflows

Query History remains service/model-only because the execution lifecycle still
needs a privacy-aware bounded capture store and a safe editor-reopen route.
Activity monitoring remains service-only because refresh, stale-selection
identity, permission-aware cancellation and termination are not yet composed.

## Automated evidence

- `Sprint51_MaintenanceAndPlanWorkspacesExposeStructuredControls`
- Existing execution-plan and maintenance application tests
- Desktop suite: 23 passing tests
- Release build: 0 warnings, 0 errors
