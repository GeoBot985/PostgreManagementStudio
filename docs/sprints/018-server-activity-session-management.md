# Sprint 018 — Server Activity Monitor and Session Management

Status: Complete with documented limitations.

Extended the activity-monitor foundation with composable database/user/application/state/query/PID/duration filters, bounded 15-minute in-memory sampling, CSV sample export, connection-capacity severity classification, redacted Markdown/JSON session export, structured 100-entry action history, query-preview truncation, stale-PID identity validation, protected monitoring/current-session checks, and sequential bulk cancel/terminate orchestration with per-PID outcomes. Added an isolated Npgsql session-management service that re-fetches PID identity (backend start, database, user, application) immediately before each action and uses parameterized PostgreSQL functions.

Validation: Release build completed with zero warnings/errors; all 119 automated tests passed, including filtering, sampling retention, capacity thresholds, redaction, history bounding, identity reuse protection, and regression coverage.

Limitations: the current application still presents activity data through a temporary WPF surface rather than a dedicated sortable/filterable monitor window. Full lock grid, bulk-action confirmation dialogs, refresh interval controls, charts, capacity panel, and persisted action-history UI remain follow-up work. The safety and service APIs are independent of those views.
