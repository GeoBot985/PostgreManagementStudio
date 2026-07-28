# Sprint 21 — Data Import and Export Wizard

Implemented the UI-independent release-candidate foundation for a safe data-transfer wizard.

## Delivered

- Format detection for CSV, TSV, JSON arrays, and JSON Lines with confidence and editable warnings.
- Conservative type inference, including protection against converting leading-zero values to numbers.
- Immutable import-plan model and validation for mappings, generated columns, required targets, replace confirmation, and incompatible COPY/error strategies.
- Streaming JSON-array reader with cancellation propagation.
- Existing streamed result export supports CSV, TSV, JSON, and SQL INSERT output; existing delimited import supports CSV/TSV, COPY, batched parameterised inserts, progress, cancellation, and rejected-row reporting.
- Unit coverage for detection, inference, validation, JSON streaming, quoting, nulls, and cancellation foundations.

## Boundary

The desktop surface currently exposes the existing query-tab import/export commands rather than a dedicated multi-page WPF wizard. PostgreSQL target metadata discovery, new-table DDL, upsert execution, and full rejected-row file export remain follow-up UI/integration increments; no credentials or row data are persisted.
