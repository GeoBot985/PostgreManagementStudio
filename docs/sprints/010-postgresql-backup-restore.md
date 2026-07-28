# Sprint 010 — PostgreSQL Backup and Restore

Status: Complete with documented limitations.

Implemented provider-neutral backup and restore command models and builders for custom, plain SQL, and directory formats. Commands use structured process argument lists, never place passwords in previews, and pass credentials only through the child-process environment. Added PostgreSQL executable discovery across configured paths, the application directory, PATH, and common Windows installation locations. Added a reusable streamed external-process runner with progress capture and process-tree cancellation. The temporary WPF query surface now exposes Backup Database and Restore Database actions with format selection, validation, confirmation, and progress output.

Validation: Release build completed with zero warnings/errors; 89 automated tests passed. Command-builder tests cover format routing, structured paths, password exclusion, and invalid option combinations.

Limitations: the existing application has no object-explorer/database-node model, so the actions are attached to the current query database context. Directory backups and full UI dialog option matrices are represented by the service models but need a richer object-explorer surface for final UX polish. A real backup/restore smoke test requires `pg_restore` and `psql` to be available alongside the detected `pg_dump`; `pg_dump` was found at the PostgreSQL 18 installation, while the remaining executables were not exposed by the current PATH lookup.
