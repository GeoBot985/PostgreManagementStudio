# Sprint 20 — Schema Compare and Synchronisation Preview

Implemented a review-first synchronisation preview on top of the Sprint 19 snapshot comparison foundation.

## Delivered

- UI-independent `SchemaSynchronisationPreviewBuilder` with explicit inclusion/exclusion state and filters for additions, modifications, deletions, warnings, and selected changes.
- Destructive, manual, unsupported, and unresolved changes are excluded from the preview by default; warnings explain why review is required.
- Included operations are rendered as a transaction-wrapped script with operation comments and high-risk warnings.
- Existing identifier quoting is used for generated drop statements and scripts never contain connection credentials.
- Preview tests cover safety defaults, explicit exclusions, dependency ordering, transaction framing, and quoted identifiers.

## Boundary

The current desktop entry point remains a lightweight command in the query tab and uses the configured local connection for both snapshots. A dedicated source/target selector workspace, clipboard/save/editor actions, richer PostgreSQL catalog extraction (columns, constraints, indexes, routines, triggers, enums), and UI automation require the next increment. No direct schema execution was added.
