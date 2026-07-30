# Sprint 60 Completion Report

## Outcome

Sprint 60 replaces the thin transfer dialog with production import and export
wizards. It delivers bounded source inspection, conservative type inference,
existing/new-table mapping, conversion and error policies, streaming
PostgreSQL execution, relation and result exports, atomic output, progress,
cancellation, rejected-row evidence, transfer history, and traditional shell
entry points.

## Architecture and files

- `ProductionDataTransfer.cs`: strict encoding inspection, streaming delimited
  records, preview, header normalization, type inference, mapping/preflight,
  safe new-table DDL, rejected rows, and relation-export contracts.
- `NpgsqlTransferMetadataProvider.cs`: PostgreSQL schemas, relation
  capabilities, permissions, and destination column metadata.
- `NpgsqlDataTransferService.cs`: client-side COPY, validated parameterized
  batches, transactions/savepoints, conversion, cancellation, and terminal
  outcomes.
- `NpgsqlRelationExportService.cs`: direct relation streaming to CSV, TSV,
  JSON, JSON Lines, and PostgreSQL INSERT output.
- `ResultExportService.cs`: JSON Lines, selected/reordered/renamed columns,
  completeness warnings, configurable serialization, and PostgreSQL literals.
- `DataTransferWorkspaceWindow.cs`: nine-step import and eight-step export
  desktop workflows.
- `MainWindow` and `QueryTabView`: Database, Object Explorer Tasks, and
  result-grid reachability.
- Production composition and Core, Results, Postgres, Integration, and Desktop
  tests cover the new seams.

The implementation keeps file parsing and PostgreSQL operations outside the
window. The UI owns review state and progress presentation; services own
validation, streaming, transactions, and serialization.

## PostgreSQL behavior

Existing-table metadata reads PostgreSQL catalogues and privilege functions to
distinguish tables, partitions, foreign tables, views, and materialized views,
and to identify writable/generated/defaulted columns. Imports use binary
`COPY FROM STDIN` where the reviewed strategy permits it or typed,
parameterized inserts with PostgreSQL casts and savepoints for row-level error
collection.

New-table DDL quotes identifiers and accepts only validated type syntax.
Timestamptz input is normalized to UTC. Numeric, boolean, UUID, JSON, bytea,
date/time, and timestamp conversion is culture-independent unless an explicit
rule supplies separators or formats.

Relation exports use an asynchronous reader and a temporary destination. The
final path is replaced only after success. SQL fragments reject semicolons and
comments, while identifiers remain catalogue-derived and quoted.

## Verification

- Release build: zero warnings and zero errors.
- Full disposable PostgreSQL 18.4 regression: 450 passed, 0 failed; three
  million-row/large-schema performance tests remained explicitly gated.
- Core tests: encoding detection, multiline parsing, malformed records, header
  normalization, type inference, mappings, preflight, DDL, and rejected files.
- Results tests: CSV/TSV compatibility, JSON Lines, selection, column rename,
  truncation warnings, and PostgreSQL SQL literals.
- PostgreSQL integration: metadata, complex typed import, new-table creation,
  atomic rollback, batched partial commit, relation CSV/JSONL/SQL export, and
  cancellation.
- Desktop tests: wizard steps, menu and Object Explorer Tasks reachability,
  service composition, and existing shell regression.
- Live WPF/PostgreSQL walkthrough: created `public.sprint60_live`, imported a
  multiline/NULL sample, appended a valid row while rejecting an invalid
  smallint row, generated a rejected-row CSV, queried the three committed rows,
  and exported the complete result to CSV.

The disposable regression database and roles were removed by the harness.
Live walkthrough files and the temporary table were removed after evidence was
captured.

## Evidence

- [Import source](../screenshots/sprint-60/import-source.jpg)
- [Detected format](../screenshots/sprint-60/import-format.jpg)
- [Bounded multiline/NULL preview](../screenshots/sprint-60/import-preview.jpg)
- [New-table destination](../screenshots/sprint-60/import-new-table.jpg)
- [Inferred column mapping](../screenshots/sprint-60/column-mapping.jpg)
- [Execution and error rules](../screenshots/sprint-60/import-rules.jpg)
- [Preflight and generated DDL](../screenshots/sprint-60/import-review.jpg)
- [Execution progress](../screenshots/sprint-60/import-progress.jpg)
- [Successful import summary](../screenshots/sprint-60/import-completion.jpg)
- [Rejected-row summary](../screenshots/sprint-60/rejected-row-summary.jpg)
- [Result-grid export entry](../screenshots/sprint-60/result-grid-export.jpg)
- [Export format controls](../screenshots/sprint-60/export-format-options.jpg)
- [Export completion](../screenshots/sprint-60/export-completion.jpg)
- [Imported relation in Object Explorer](../screenshots/sprint-60/object-explorer-relation.jpg)

## Performance and safety

Preview is bounded to 200 records and execution streams from disk. COPY is the
fast path; validated inserts are batched. Relation and result exports stream to
an atomic temporary file. Error collection is bounded, cancellation propagates
through file and database operations, and transaction status is explicit.

The live two-row import and three-row export completed in well under one second;
these timings are smoke observations rather than throughput claims. Automated
tests exercise 10,000-row-style bounded behavior and cancellation. The three
million-row/large-schema performance tests are intentionally gated and were not
enabled for this final functional regression run.

Values are parameterized or sent through binary COPY, identifiers are quoted,
type syntax is restricted, and credentials are excluded from transfer history
and output. Existing destination files remain intact after cancellation or
failure.

Cancellation tests verified that active atomic work rolls back and that export
temporary files do not replace the requested destination. Batched import tests
verified that already committed batches are retained and reported as partial
rather than complete.

## Known limitations

Delimited text is the import scope; Excel and JSON import remain deferred.
Inference samples are bounded and therefore proposals require review. Result
selection is rectangular, retained results are not silently re-executed, and
advanced PostgreSQL arrays/domains/enums rely on validated casts rather than
specialized visual editors. These limits are explicit in the wizard and feature
guide.

Deferred work includes Excel/JSON import, disjoint result-row selection,
multi-point inference sampling beyond the bounded leading sample, a visual
relation-filter builder, and specialized PostgreSQL array/domain/enum editors.

## Regression risks

The highest-risk surfaces are malformed records crossing stream-buffer
boundaries, PostgreSQL type/version differences, locale-sensitive source
conventions, cancellation at transaction boundaries, and WPF state becoming
stale after connection recovery. Strict decoder/parser tests, disposable
PostgreSQL integration, invariant conversions, generation-aware metadata,
atomic destinations, and the full shell regression suite bound those risks.

## Independent review

The acceptance review found and closed gaps between service capabilities and
UI exposure for export encoding/delimiter/quote/line endings/NULL text and
import numeric/boolean/time-zone/invalid-value rules. It also hardened
Object Explorer selection recovery for keyboard context actions. No release
blocker remains within Sprint 60 scope.
