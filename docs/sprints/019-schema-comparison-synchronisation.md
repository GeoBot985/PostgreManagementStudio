# Sprint 019 — Schema Comparison and Synchronisation

Status: Complete with documented limitations.

Implemented a provider-neutral canonical schema model, whitespace/line-ending canonicalization, deterministic object identity, added/removed/changed/rename-candidate classification, risk classification, dependency-aware synchronization ordering, destructive-action exclusion by default, human-readable qualified SQL generation, target/source metadata headers, and versioned JSON schema snapshots. Added a PostgreSQL extractor for user schemas and relations with server-version capture, plus a temporary WPF Schema Compare entry point that extracts and compares the active database without executing a synchronization script.

Validation: Release build completed with zero warnings/errors; all 120 automated tests passed, including canonicalization, classification, rename confidence, dependency ordering, destructive safety, script generation, snapshot round trips, and regression coverage.

Limitations: the current application has no dedicated two-connection comparison window, so the temporary UI compares the active connection against itself. Full catalogue coverage, live source/target selection, dependency extraction beyond the initial relation model, interactive review/selection, typed destructive confirmation, target-drift revalidation, execution through the query infrastructure, and report/package exports remain follow-up UI and integration work. Partial extraction is preserved and prevents plans from being marked safe to execute.
