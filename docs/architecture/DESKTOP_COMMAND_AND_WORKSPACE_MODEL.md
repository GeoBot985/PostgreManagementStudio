# Desktop command and workspace model

## Canonical shell

The primary navigation model is the traditional menu bar: File, Edit, View,
Query, Database, Tools, Window and Help. Database remains a justified
application menu because its actions operate on the active PostgreSQL
database. High-frequency actions only are repeated on the compact toolbar.

## Command ownership and context

`ShellCommands` owns one routed command per user action. Menus, toolbars,
keyboard gestures, editor context menus, result menus and Object Explorer
menus bind to those commands. `MainWindow` resolves the active document and
connection at invocation time; workspace-local commands resolve their current
selection and revalidate destructive targets immediately before execution.
No command falls back silently to a similarly named or previously active
connection.

## Documents and tool windows

SQL editor tabs are documents. Restore, search, maintenance, index, schema,
transfer and monitoring surfaces are modeless tool windows. A connection-bound
tool window is singleton per editor context and focuses an existing instance;
duplicate windows are not created by repeated menu invocation. Short-lived
input and confirmation surfaces are modal dialogs. Staged import/export work is
represented as a workspace/wizard rather than a chain of unrelated prompts.

## Shortcuts

Shortcuts use conventional combinations and are declared on the canonical
routed command: Ctrl+N/O/S, Ctrl+Shift+S, Ctrl+W, Ctrl+Shift+W, Ctrl+Tab,
Ctrl+Shift+Tab, F5, Ctrl+Enter, Esc, Ctrl+F/H/G, Ctrl+L and the Ctrl+Shift
feature shortcuts. A deterministic desktop test rejects duplicate key/Modifier
pairs.

## Lifecycle and close behaviour

Every asynchronous workspace owns a cancellation/lifetime boundary. Closing a
workspace stops timers, cancels refresh or operation work and prevents late UI
updates. Closing a document prompts for running execution and unsaved SQL;
destructive or partial operations identify the workspace, target and outcome
before allowing closure. Reopening creates a fresh safe plan rather than
restoring executable state.

## Persistence and privacy

Only validated, non-sensitive preferences may be persisted. Credentials,
connection strings, active process identifiers, destructive confirmations,
running operations, transfer plans and diagnostic data are excluded. Corrupt
settings fall back to defaults without blocking startup. Layout reset is always
available from Window.

## Cross-workspace navigation

Navigation preserves exact server/database identity. A source workspace may
focus an existing target workspace, but it must report unavailable targets and
must not execute a query merely by opening an editor. Context menus select the
item under the pointer before opening, preventing a later tree selection from
receiving the action.
