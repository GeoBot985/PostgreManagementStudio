# Sprint 004 — SQL Query Editor and Execution

Status: In progress.

Implemented the application-layer `QueryDocument` and `QueryTabManager` boundaries, per-document SQL/connection/database/state/session/message ownership, selection-versus-full-script execution, duplicate-execution protection, cancellation, dirty-tab close policy, and unit coverage. The temporary WPF shell now supports a New Query action, F5/Ctrl+Enter execution, Escape cancellation, selected-text execution, and the existing result/message preview.

Release build and all tests pass. Remaining work is binding the manager to a real WPF TabControl with per-tab controls and adding dedicated UI automation/manual acceptance coverage. Production editor features excluded by the sprint remain deferred.
