# Sprint 013 — Database Maintenance Operations

Status: Complete with documented limitations.

Implemented a provider-neutral maintenance plan model and SQL builder for VACUUM, ANALYZE, REINDEX, and CLUSTER. Plans support safely quoted database/schema/table/index targets, column-aware ANALYZE, vacuum options, concurrent/verbose reindex, cluster safeguards, version capability gating, operation-scoped statement and lock timeouts, high-impact classification, and validation of incompatible combinations. Added a dedicated Npgsql maintenance connection with an identifiable application name, streamed PostgreSQL notices/progress, sequential multi-target execution, cancellation, partial-target outcomes, sanitized errors, and disposal on every path. The WPF query surface now exposes a Maintenance action with preview and explicit confirmation.

Validation: Release build completed with zero warnings/errors; all 102 automated tests passed. Coverage includes SQL generation, identifier safety, option compatibility, version capability rules, high-impact safeguards, history bounding, and execution-model compilation.

Limitations: the current application has no full object-explorer maintenance centre, so the temporary UI starts a database-wide VACUUM ANALYZE workflow. Dedicated target-selection, progress dialogs, lock-wait/activity links, verification queries, and persisted maintenance-history UX remain follow-up UI work. PostgreSQL remains the final authority for privileges and server-version behavior.
