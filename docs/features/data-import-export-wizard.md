# Production Data Import and Export

## Overview

PostgreManagementStudio provides owned, multi-step desktop wizards for importing
local delimited files and exporting either query results or PostgreSQL
relations. The wizards keep source interpretation, destination mapping,
execution policy, completeness, progress, cancellation, and final outcomes
explicit.

Entry points:

- `Database > Import Data`
- `Database > Export Data` for the active result
- `Object Explorer > table/view > Tasks > Import Data / Export Data`
- the result-grid Export command

No workflow relies on a server-side file path. Files are read or written by the
desktop application and PostgreSQL data moves through the active connection.

## Import workflow

The import wizard has Source, Format, Preview, Destination, Column Mapping,
Data Types and Rules, Review, Execution, and Results steps.

Supported source controls include:

- CSV, TSV, semicolon, pipe, or a one-character custom delimiter;
- UTF-8, BOM-aware UTF-8, UTF-16 LE/BE, and Windows-1252;
- automatic encoding, delimiter, quote, and line-ending inspection;
- optional headers, header-space normalization, skipped rows, trimming,
  multiline quoted fields, and an explicit NULL marker;
- bounded preview with source row numbers, explicit NULL display, and malformed
  row status.

The parser is asynchronous and streaming. Preview and type inference inspect a
bounded sample; execution does not load the complete source into memory.
Inference is deliberately conservative and proposes PostgreSQL boolean,
integer, numeric, floating-point, date/time, timestamp, UUID, or text types.
Users review and may edit every new-table proposal.

Existing-table metadata identifies generated, identity-always, defaulted,
nullable, and required columns. Mappings support exact, case-insensitive, and
ordinal modes. Generated and identity-always destinations are not writable, and
preflight rejects duplicate mappings or omitted required columns.

Per-column conversion rules cover:

- trimming and empty-string-to-NULL;
- explicit date, time, and timestamp formats;
- decimal and thousands separators;
- configurable true/false spellings;
- currency-symbol stripping and parenthesized negatives;
- time-zone assumptions;
- reject-row or substitute-NULL handling where the destination allows NULL.

Execution supports fast client-side `COPY FROM STDIN` and parameterized
row-validated inserts. Transactions may cover the complete import or individual
batches. Collect-errors mode uses savepoints, bounded error collection, and an
atomic rejected-row report containing source row, original fields, destination
column, category, message, and SQLSTATE. Cancellation rolls back the active
atomic transaction; batched mode labels partial commits explicitly.

## Export workflow

Exports can read:

- all retained result-grid rows;
- a rectangular result-grid selection;
- a table, partitioned table, partition, foreign table, view, or materialized
  view directly from PostgreSQL.

Column inclusion, order, and output headers are editable. Relation exports can
apply a positive row limit and separately reviewed `WHERE` and `ORDER BY`
fragments; statement separators and SQL comments are rejected.

Supported formats are CSV, TSV, JSON array, JSON Lines, and PostgreSQL `INSERT`
statements. Delimited export exposes encoding, delimiter, quote, line ending,
NULL text, and header controls. JSON array output can be pretty-printed. SQL
output supports configurable rows per statement and optional `BEGIN`/`COMMIT`.
PostgreSQL identifiers and values are serialized with type-aware quoting,
including booleans, numbers, bytea, dates, timestamps, and NULL.

Result exports state whether the retained client result is complete. A
truncated result is never presented as a complete export. Database-relation
exports stream directly with an `NpgsqlDataReader`. Every format writes to a
temporary file and replaces the requested destination only after successful
completion, so cancellation or failure does not leave a misleading partial
final file.

## Operational behavior

The Review step is the execution contract. It identifies the source,
server/database, relation, mappings, inferred DDL, execution strategy,
transaction policy, error policy, export completeness, and destination path.

Progress reports phase, rows, batches, elapsed time, and export bytes where
available. Results distinguish completed, completed-with-rejections,
cancelled, failed, and partially committed outcomes. Transfer history receives
the same terminal facts without connection strings or passwords.

## Security and trust boundaries

- Source data, headers, relation names, and file paths are untrusted input.
- Identifiers are quoted; new-table type syntax is restricted to a safe
  PostgreSQL type grammar.
- Relation filters reject statement separators and comments.
- Import values use binary COPY or parameters, never SQL string concatenation.
- Credentials and connection strings are not written to transfer output,
  history, screenshots, or diagnostics.
- Overwrite happens only at the user-selected local destination and only after
  successful serialization.

## Current limitations

- Input is delimited text; Excel and JSON import are not included.
- Preview and inference are bounded and may not encounter later incompatible
  values. The Review step warns about this and execution applies the selected
  error policy.
- A result-grid selection is rectangular. Arbitrary disjoint rows are not a
  separate export scope.
- Result export uses retained client data and does not silently re-run the
  original query.
- Relation filters are deliberately constrained SQL fragments, not a visual
  query builder.
- PostgreSQL arrays, enums, and domains use PostgreSQL casts in validated insert
  mode rather than a dedicated visual value editor.
