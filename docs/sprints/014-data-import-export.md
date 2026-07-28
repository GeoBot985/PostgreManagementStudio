# Sprint 014 — Data Import and Export

Status: Complete with documented limitations.

Implemented streaming CSV/tab/custom-delimited detection and parsing with quoted delimiters, escaped quotes, embedded newlines, UTF-8/BOM handling, null tokens, and whitespace rules. Added source-to-destination mapping and validation that protects generated columns, duplicate destinations, and required unmapped columns. Added common PostgreSQL value conversion validation, configurable import strategy/transaction models, streaming output formatting, and an Npgsql data-transfer service using a dedicated identifiable connection with COPY binary streaming or parameterized batch INSERT, destructive-preparation support, cancellation, progress, rejected-row limits, and partial-result reporting. The WPF query surface now exposes an Import Data entry point and the existing result export remains available.

Validation: Release build completed with zero warnings/errors; the full automated suite passed, including parser, mapping, conversion, CSV quoting, and regression tests.

Limitations: the current application has no full object-explorer wizard or destination metadata dialog, so the temporary WPF importer asks for a public-schema table and derives text mappings from the header. Full export wizard source/column/row selection, rejected-row file UX, verification/history screens, and richer PostgreSQL metadata-driven mapping remain follow-up UI work. The service APIs support those workflows without interpolating imported values into SQL.
