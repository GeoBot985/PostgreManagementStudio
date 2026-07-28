# Sprint 016 — Database Backup and Restore

Status: Complete with documented limitations.

Extended the backup/restore foundation with all required archive formats: custom, directory, tar, and plain SQL. Added archive inspection through `pg_restore --list`, plain-SQL detection metadata, destination safety and post-backup output verification, tool-version parsing, compatibility validation for parallel plain-SQL restore, unique temporary PostgreSQL credential files with cleanup, bounded 100-entry operation history, and reusable safety/inspection models. Existing structured process execution continues to provide asynchronous output capture and process-tree cancellation. Tar backup command generation and secure credential cleanup are covered by tests.

Validation: Release build completed with zero warnings/errors; the full automated suite passed.

Limitations: the current application still has a temporary WPF backup/restore surface rather than the full multi-step manager. Selective archive restore list generation, tool-version discovery UI, full archive metadata extraction, restart recovery, persisted operation history, and detailed restore confirmation screens remain follow-up UI work. The core APIs are structured for those workflows and never place passwords in argument lists, previews, or history.
