# Sprint 34 technical-debt register

Sorted by release blocker, severity, likelihood, and user impact.

| ID | Blocker | Severity | Likelihood | Area | Evidence | User impact | Recommended fix / sprint | Scope |
|---|---|---|---|---|---|---|---|---|
| TD-034-001 | Yes | Critical | Certain | Feature reachability | `QueryTabView.xaml` is the only command surface; matrix rows 20-33 are mostly absent | advertised workflows cannot be used | compose approved workspaces and add command tests / 35,39 | Large |
| TD-034-002 | Yes | High | Certain | Connection workflow | startup reads `PMS_CONNECTION_STRING`; no connection dialog/Object Explorer | product cannot establish/manage normal connections | production connection workspace / 35 | Large |
| TD-034-003 | Yes | High | Certain | Schema compare | `SchemaCompare_Click` extracts the same connection twice | comparison always reports its own database | distinct source/target selection and integration tests / 35,38 | Medium |
| TD-034-004 | Yes | High | Certain | Composition | `MainWindow` and `QueryTabView` manually instantiate services | lifetimes, test wiring, logging and settings are inconsistent | central composition module and command services / 35 | Medium |
| TD-034-005 | Yes | High | Certain | Packaging | no installer/publish/signing/upgrade assets | cannot ship or recover a release | packaging and upgrade work / 41 | Large |
| TD-034-006 | Yes | High | High | UI verification | no UI/smoke automation | broken commands may pass all tests | WPF smoke harness and command routing tests / 35,42 | Large |
| TD-034-007 | Yes | High | High | Lifecycle | tab close does not cancel/dispose sessions; view owns async work | leaks or orphaned operations | disposable document/workspace ownership / 36 | Medium |
| TD-034-008 | Yes | High | High | Safety | destructive operations have inconsistent confirmation and audit behavior | wrong target or irreversible change | inventory-driven safety policy/tests / 38 | Large |
| TD-034-009 | No | Medium | Certain | Capability detection | separate version capability records and literals across features | newer/older servers may behave inconsistently | unified capability snapshot / 40 | Medium |
| TD-034-010 | No | Medium | Certain | Monitoring models | four overlapping activity/session presentation layers | fixes can diverge | map semantics and consolidate shared snapshot/filter primitives / 36 | Medium |
| TD-034-011 | No | Medium | Certain | Plan models | execution, analysis, explorer, and comparison layers overlap | inconsistent diagnostics/UI adaptation | canonical immutable plan model with adapters / 35 | Medium |
| TD-034-012 | No | Medium | Certain | Persistence | history/settings contracts lack production repository | user choices/history are lost | versioned atomic local persistence / 35,36 | Medium |
| TD-034-013 | No | Medium | High | Credential cleanup | `TemporaryCredentialFile.Delete` swallows failures | credential temp file may remain | retry/report cleanup and startup scavenging / 38 | Small |
| TD-034-014 | No | Medium | High | Integration coverage | live tests focus on query execution | adapter/catalog regressions escape | real-PostgreSQL fixture suite / 35,40 | Large |
| TD-034-015 | No | Medium | High | UI architecture | `QueryTabView.xaml.cs` contains all feature handlers | high change coupling and weak testability | extract commands/view models incrementally / 35,39 | Large |
| TD-034-016 | No | Medium | High | Diagnostics | no host logging configuration or correlation | support failures lack evidence | structured logging/redaction policy implementation / 36 | Medium |
| TD-034-017 | No | Low | Certain | SDK reproducibility | no `global.json` | developer SDK drift | pin reviewed SDK / 41 | Small |
| TD-034-018 | No | Low | Certain | Documentation | historical `HANDOFF.md` remains Sprint 2 focused | onboarding confusion | archive or replace after hardening workflow is agreed / 35 | Small |

## Sprint 40 disposition

| ID | Disposition after Sprint 40 | Evidence |
|---|---|---|
| TD-034-001 | Reduced, not closed | Existing verified workflows are organised in native menus/toolbars; incomplete Schema Compare is removed. Rich uncomposed Sprint 20–33 workspaces remain outside the release surface |
| TD-034-002 | Closed for the Sprint 40 scope | `ConnectionDialog` provides session-only Test Connection and Connect; environment configuration is only a visibly labelled development fallback |
| TD-034-003 | Mitigated | Schema Compare is absent from `ShellCommands` and the release menus until faithful extraction and two-endpoint selection exist |
| TD-034-004 | Reduced, not closed | All shell surfaces route through `ShellCommands`; some feature adapters remain directly owned by `QueryTabView` |
| TD-034-006 | Reduced | `ShellWorkflowTests` cover command state, shared routing, menu discovery, resizing, document titles, and release-surface exclusions; full packaged UI automation remains future work |
| TD-034-007 | Closed | Closing a tab confirms unsaved state, protects/cancels running execution, disposes the `QueryDocument`, and removes it from `QueryTabManager` |

No Critical finding is represented as deferred without a release-blocking
status. High findings have an assigned hardening sprint.

## Sprint 35 updates

- `TD-034-004` is resolved: the App and tests share validated
  `ProductionServices.Build` registrations, with no production test doubles.
- `TD-034-002` is reduced: Object Explorer is reachable and uses the production
  metadata adapter, but normal saved-connection management remains a blocker.
- `TD-034-006` is reduced: production composition and STA shell lifecycle are
  automated; full command/view automation remains.
- `TD-034-012` is reduced: atomic version-tolerant application settings now
  exist; query-performance history persistence remains.
- `TD-034-014` is reduced through isolated live coverage for metadata,
  transactions, permissions, activity, search, plans, timeout, and backup.

New evidence-based blockers:

| ID | Blocker | Severity | Area | Evidence | Recommended sprint |
|---|---|---|---|---|---|
| TD-035-001 | Yes | High | Transactions | query documents open a new executor connection per run and cannot retain explicit editor transaction state | 36 |
| TD-035-002 | No | High | Destructive confirmation | shared injectable guard now covers restore, maintenance, and actual-plan execution; extend it as session/schema mutation UIs become reachable | 38 |
| TD-035-003 | Yes | High | Connection lifecycle | no automated server-loss/disconnect/reconnect workspace state machine | 36 |
| TD-035-004 | Yes | High | Restore | no disposable real restore end-to-end regression | 36/38 |
| TD-035-005 | Yes | High | Query performance | no production pg_stat_statements collector/workspace | 35 follow-up/36 |

## Sprint 36 updates

- `TD-034-007` is resolved: query documents own cancellation, result sessions,
  tab-scoped provider sessions, and deterministic shutdown disposal.
- `TD-034-016` is reduced: query execution emits structured privacy-bounded
  correlation telemetry; host-wide logging configuration remains later work.
- `TD-035-001` is resolved: opt-in user-managed editor scopes preserve
  PostgreSQL transaction state, expose `25P02`, require explicit rollback, lock
  connection context, and dispose/reset on close.
- `TD-035-003` is resolved for the SQL editor: backend termination, missing
  databases, timeout, invalid profiles, pool discard, controlled recovery, and
  no automatic replay are automated.

## Sprint 37 updates

- `TD-035-003` is fully resolved at the reusable connection boundary through
  an explicit attempt-correlated lifecycle, stale-result rejection, safe
  reconnect policy, backend-death recovery, and structured diagnostics.
- Connection creation now has one effective configuration and validation path,
  with targeted pool invalidation and immutable profile snapshots.
- Credential persistence remains intentionally absent. A future saved-profile
  feature must provide OS-backed secure storage and must never fall back to
  plaintext.
- Physical server restart, certificate rotation, and GSS/LDAP/SSPI validation
  remain deployment compatibility campaigns, not code-release blockers.

## Sprint 38 updates

- `TD-034-002` is further reduced: the reachable Object Explorer now loads
  lazily through a production OID-based provider with refresh reconciliation,
  permissions, cancellation, cache isolation, and live PostgreSQL coverage.
  Saved-connection management remains separate from metadata navigation.
- The old unbounded completion metadata cache is replaced by a bounded,
  expiring, failure-evicting, credential-identity-aware cache.
- Property and dependency panels remain unreachable prototype scope; Sprint 38
  provides their shared identity/lifecycle primitives without adding features.
