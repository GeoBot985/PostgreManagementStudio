# Sprint 015 — Database Object Search

Status: Complete with documented limitations.

Implemented a provider-neutral object-search subsystem with normalized search options, safe PostgreSQL LIKE wildcard conversion, parameterized catalogue-query generation, object-type filtering, system-schema exclusion by default, result limits, deduplication, bounded local search history, and navigation-service abstraction. Added an isolated Npgsql search connection with an identifiable application name, asynchronous cancellation, partial warning aggregation, and sanitized connection/database error reporting. The WPF query surface now exposes Search Objects and displays result counts, duration, result names, warnings, and limit status.

Validation: Release build completed with zero warnings/errors; the full automated suite passed, including wildcard escaping, parameterization, filtering, system-schema behavior, history retention, deduplication, and regression coverage.

Limitations: the current application has no dedicated server/object-explorer window, so the temporary UI presents search results in the existing message surface. Full definition-search catalogue unions for functions/procedures/triggers, sortable result grid/context actions, Ctrl+Shift+F wiring, persisted history, and object-explorer navigation remain follow-up UI work. Search APIs are structured to support those additions without moving SQL into views.
