# Sprint 53 transfer workspace evidence

The release shell now routes the existing transfer services through a reusable
WPF workspace instead of the earlier one-shot import/export dialogs.

## Import workflow

`Database > Import Data` opens `DataTransferWorkspaceWindow` in Import mode.
The operator chooses a delimited source file, names the explicit schema/table
target, inspects a bounded preview, edits source-to-destination mappings,
chooses append/truncate/delete and COPY/batched insert behavior, validates the
plan, then runs or cancels the transfer. Plan versions are invalidated when
source, target, mapping or error-policy inputs change. Outcomes expose rows
read/written/rejected, errors, cancellation state and transfer history.

## Export workflow

`Results > Export` opens the same workspace in Export mode for the active
retained result set. CSV, TSV, JSON and SQL-insert output, delimiter/header
options, explicit destination, validation, streaming progress, cancellation
and transfer history are available. The scope is intentionally labelled as
currently loaded rows; database/object export is not implied.

## Verification

- `ShellWorkflowTests.Sprint53_TransferWorkspacesExposeImportMappingAndExportReviewSurfaces`
  passes on the WPF STA test host.
- Full solution test run: 331 passed, 60 PostgreSQL integration tests skipped
  because the integration environment is not configured, 0 failed.
- Release build: 0 warnings, 0 errors.

## Deliberate limits

PostgreSQL-to-PostgreSQL transfer is deferred because no safe reusable
source/target transfer service is currently composed. JSON import remains
service-capable but is rejected by the current delimited-file preview surface
until a structured JSON mapping UI is added. Import target-column metadata is
currently supplied by the existing destination adapter; the workspace does
not invent catalog metadata or label a partial transfer as complete.
