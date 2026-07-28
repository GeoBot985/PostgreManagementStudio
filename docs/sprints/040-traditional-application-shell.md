# Sprint 40 — Traditional Application Shell and Workflow Reachability

## Delivered

- Native WPF File, Edit, View, Query, Tools, Window, and Help menus.
- Compact Standard and Query toolbars with native overflow behavior.
- Fixed management-studio layout with docked Object Explorer, resizable query workspace, output splitter, and status bar.
- Shared routed commands across menus, toolbars, keyboard gestures, editor/results context menus, and tab controls.
- Session-only interactive PostgreSQL connection dialog with validation and Test Connection/Connect actions.
- Per-query connection and database context. Environment-variable support remains visibly labelled as a development fallback.
- Meaningful `SQLQueryN.sql` titles, dirty marker, close button, save confirmation, and running-query cancellation protection.
- Temporary Find/Replace panel and contextual results toolbar.
- Separate Results, Messages, and Execution Plan output tabs with nested result-set tabs.
- Result commands disabled until a result set exists.
- Live status for safe server/database/role identity, query state, elapsed time, row counts, and caret position.
- Desktop command-state, shortcut, reachability, resize, title, composition, and release-surface tests.

## Deliberately deferred

- Schema Compare is removed from the release command surface. The existing extractor does not produce faithful schema definitions and the old UI compared one connection to itself.
- Recent Files, transaction options, Options, and unsupported Object Explorer context actions remain disabled or absent until their workflows can be verified.
- Layout dimensions are not persisted in this sprint.
- Environment-fallback and interactive passwords are never persisted.

## Verification

- Debug and Release solution builds treat warnings as errors.
- Desktop tests exercise the native shell on an STA thread at 1024×768 and a wide desktop size.
- Manual WPF smoke verification checks the menu, toolbars, disconnected states, connection dialog, output panes, and normal shutdown.
- `scripts/test-release.ps1` remains the release integration gate for existing query, metadata, backup, restore, and PostgreSQL workflows.
