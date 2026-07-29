# Sprint 53 — Data Transfer, Import/Export, and Migration Workflow Composition

## Outcome

Sprint 53 is complete for two coherent, production-scoped workflows:

1. delimited file to PostgreSQL import; and
2. retained query-result to file export.

PostgreSQL-to-PostgreSQL migration is explicitly deferred rather than
represented by a misleading placeholder workflow.

## Delivered

- Added `DataTransferWorkspaceWindow` with Import and Export modes.
- Replaced the shell's one-shot Import Data and Export Results routes with the
  reusable workspace routes.
- Import includes explicit source path, schema/table destination, bounded
  preview, editable mapping, append/truncate/delete choice, COPY/batched
  execution choice, continue-and-collect-rejected option, plan validation,
  stale-plan invalidation, cancellation, progress, partial/rejected counts and
  output history.
- Export includes explicit destination, CSV/TSV/JSON/SQL-insert formats,
  delimiter/header options, current-retained-row scope, validation,
  cancellation, streamed progress and history.
- Added bounded in-memory `TransferHistoryService`; history errors are capped
  and redacted before retention.
- Added DI registration for the history and export abstractions.
- Added WPF reachability coverage for both workspace modes.
- Updated the reachability matrix, backlog and discrepancy corrections.

## Safety and truthfulness

The import plan must be validated after configuration changes. Replace/delete
choices are represented as destructive modes and flow through the existing
import validation/service contracts. Cancellation is passed through to the
streaming service; export writes through the existing atomic temporary-file
path. The workspace only reports rows returned by the service and does not
convert partial/cancelled outcomes into successful completion. Credentials are
not placed in history; retained error text is redacted and bounded.

## Verification

- Release build: passed with 0 warnings and 0 errors.
- Full solution tests: 331 passed, 60 skipped integration tests, 0 failed.
- Desktop tests: 26 passed, including
  `Sprint53_TransferWorkspacesExposeImportMappingAndExportReviewSurfaces`.
- PostgreSQL integration tests remain skipped when the configured integration
  connection is absent; this is an environment limitation, not a claimed
  failure-free live qualification.

## Deferred scope

- PostgreSQL-to-PostgreSQL transfer and database/object export.
- JSON import composition in the mapping preview (the service reader exists,
  while the current workspace accepts delimited files only).
- Full destination catalog/type/constraint discovery in the workspace.
- Multi-destination jobs, resumable migration, and durable history storage.

Evidence: `docs/audits/ui-reachability-evidence/sprint-53-transfer-workspaces.md`.
