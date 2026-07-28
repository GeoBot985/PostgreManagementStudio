# Hardening backlog

## Sprint 35 — Regression and integration coverage

- Introduce a production composition module and test that it builds/resolves.
- Add command-routing tests for every reachable desktop action.
- Wire and test distinct schema-compare source/target selection.
- Add live PostgreSQL fixtures for metadata, activity, plans, security reads,
  search, maintenance-safe cases, and connection factory.
- Establish approved workspaces for currently unreachable Sprint 20-33 services.

## Sprint 36 — Reliability and failure handling

- Give documents/workspaces explicit async disposal and cancellation ownership.
- Consolidate background refresh sequencing and observe all task failures.
- Implement error classification, logging correlation, and redacted diagnostics.
- Add atomic settings/history persistence and corrupt-file recovery.
- Resolve activity/session model duplication after behavioral characterization.

## Sprint 37 — Performance, memory, and scale

- Profile large result grids, exports/imports, plan trees, metadata, activity
  histories, and index inventories.
- Verify UI responsiveness, allocation bounds, collection virtualization, and
  event-subscription release.

## Sprint 38 — Security and destructive-action safety

- Threat-model all generated SQL and add malicious identifier/value tests.
- Standardize confirmation, target display, permission failures, partial
  outcomes, and audit records.
- Harden temporary credential cleanup and diagnostic redaction.
- Review restore overwrite, import truncate/delete, termination, role mutation,
  schema sync, maintenance, statistics reset, and index scripts.

## Sprint 39 — UI consistency and accessibility

- Replace the monolithic query-tab feature shell with testable workspaces.
- Standardize busy/cancel/error/progress states, keyboard navigation, focus,
  scaling, contrast, accessible names, and destructive wording.

## Sprint 40 — PostgreSQL compatibility

- Produce one capability snapshot service and remove scattered version literals.
- Test the supported version matrix and safe behavior for unknown newer servers.
- Cover extension/catalog/permission-dependent capabilities.

## Sprint 41 — Installation, upgrade, and recovery

- Pin SDK, define RID/publish mode, create signed installer assets, and validate
  install/upgrade/uninstall/repair.
- Define settings/data migration, backup, and startup recovery.

## Sprint 42 — Integrated system validation

- Automate startup-to-shutdown smoke workflows and representative feature flows
  against disposable PostgreSQL.
- Run reliability, scale, security, compatibility, and recovery suites together.

## Sprint 43 — Release-candidate audit

- Re-audit feature traceability and every open blocker.
- Validate clean-machine installation, documentation, licenses, warnings,
  test evidence, known limitations, and release artifacts.
