# Sprint 30 — PostgreSQL Activity Monitor

Completed the activity-monitor presentation and refresh-safety layer on top of the existing PostgreSQL activity collector.

## Delivered

- Centralized summary-card projection for total, active, idle, idle-in-transaction, blocked, waiting, long-running, and maximum transaction age metrics.
- Configurable refresh settings and cancellation-aware obsolete-refresh coordination.
- Stable session-selection identity using PID, backend start, database, user, application, and client address to reduce PID-reuse risk.
- Explicit cancel/terminate confirmation models with distinct warnings; termination is marked as strong confirmation and explains rollback/external-side-effect limits.
- Combined monitor filters and serializable local filter presets.
- Existing activity collector, blocking graph, session details models, protected backend checks, permission-aware actions, bounded histories, and privacy-aware exports remain intact.
- Unit coverage for summaries, thresholds, identity validation, confirmation wording, preset persistence, and refresh cancellation.

## Boundary

The desktop still exposes Activity Monitor through the existing query-tab command surface rather than a dedicated server/database context-menu workspace. Full multi-pane WPF layout, automatic timer controls, persistent settings/audit storage, richer lock catalog enrichment, and comprehensive UI automation remain follow-up work. No automatic cancellation or termination was added.
