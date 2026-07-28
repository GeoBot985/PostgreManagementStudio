# Sprint 012 — Server Activity and Session Monitoring

Status: Complete with documented limitations.

Implemented typed activity snapshots and backend-state classification, blocking relationship/tree analysis with cycle protection, bounded in-memory snapshot history, non-overlapping refresh coordination with sequence rejection, redacted JSON/CSV diagnostics export, configurable detection model, and termination self-protection. Added an isolated Npgsql activity service that reads `pg_stat_activity` and blocking relationships, and executes parameterized `pg_cancel_backend(@process_id)` and `pg_terminate_backend(@process_id)` actions with sanitized outcomes. The WPF query surface now exposes an Activity Monitor action showing current session metrics and activity text.

Validation: Release build completed with zero warnings/errors; the full automated suite passed, including activity classification, blocking chains, bounded history, redaction, and self-termination tests.

Limitations: the current application still lacks a dedicated server/object-explorer window, so the temporary WPF integration presents the snapshot in the existing results/message surface. Lock-detail queries, full refresh interval controls, a persistent sortable session grid, and dedicated cancel/terminate confirmation dialogs remain follow-up UI work. PostgreSQL version-specific optional columns are currently handled through the baseline `pg_stat_activity` query and should be expanded as older-server compatibility is required.
