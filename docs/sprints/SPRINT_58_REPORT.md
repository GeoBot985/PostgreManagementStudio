# Sprint 58 — Object Explorer scripting and context actions

## Outcome

Object Explorer now provides an SSMS-style, node-specific context menu and
safe PostgreSQL script generation through the existing query-document
workspace. Scripts open in a new connected, unsaved tab and never execute
automatically.

## Delivered

- Structured object-script metadata, deterministic script generation, and
  rename/delete action contracts in the application layer.
- PostgreSQL catalogue adapters for relations, routines, sequences, indexes,
  constraints, triggers, types, schemas, databases, and extensions.
- Object Explorer metadata nodes for constraints, indexes, triggers,
  enum/domain/composite types, and extensions.
- Right-click targeting plus context-menu-key and `Shift+F10` routing.
- CREATE, DROP, DROP/CREATE, SELECT, INSERT, UPDATE, and DELETE script commands
  where applicable.
- Direct Select Rows generation with explicit columns and PostgreSQL `LIMIT`.
- Read-only properties, hierarchy refresh, copy-name commands, confirmed
  rename, and exact-DROP confirmed delete.
- Connection/read-only gating and routing of asynchronous failures through the
  existing error surface.

## Architecture

`NpgsqlObjectScriptMetadataProvider` retrieves structured catalogue metadata.
`ObjectScriptService` converts that metadata into deterministic SQL without UI
dependencies. `NpgsqlObjectActionService` executes only explicitly confirmed
rename/delete operations. `MainWindow` composes commands, editor tabs,
confirmation, and refresh; it contains no catalogue SQL or DDL reconstruction.

## Verification

- Release build: 0 warnings, 0 errors.
- Disposable PostgreSQL 18.4 qualification with large dataset:
  - Core: 193 passed.
  - Results: 63 passed.
  - PostgreSQL: 54 passed.
  - Desktop: 30 passed.
  - Integration: 63 passed.
  - Total: 403 passed, 0 failed, 0 skipped.
- Live WPF verification confirmed the connected traditional shell, lazy
  database/schema expansion, and dynamic Object Explorer menu. The inspection
  exposed and drove a fix for right-click target selection before final
  validation.

## Boundaries and follow-up

The exact supported surface and materially incomplete reconstruction fields are
recorded in `docs/features/object-explorer-scripting.md`. Full ownership/grant
scripting, identity sequence-option reconstruction, row-security policies, and
a richer editable properties workspace remain deferred. These cases are
described explicitly and are not claimed as fully reconstructed.

The prior Sprint 57 package remains the frozen `0.9.0-rc.3` candidate and does
not contain Sprint 58. A new package and release qualification are required
before this feature can be represented as part of a release candidate.
