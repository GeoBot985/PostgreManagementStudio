# Sprint 44 regression and compatibility matrix

## Release-focused suite

| Category | Trigger | Coverage | Sprint 44 result |
|---|---|---|---|
| Fast commit | normal `dotnet test` | deterministic Core, Results, Postgres, Desktop component tests | Pass |
| Standard CI | solution Release build/test | all projects, warning-as-error | Pass |
| Integration | `PMS_CONNECTION_STRING` and isolated roles/database | live query, metadata, permissions, recovery, transfer, backup/restore | Pass |
| PostgreSQL version | externally selected admin endpoint | capability/version contracts | PostgreSQL 18.4 pass; PostgreSQL 14 environment unavailable |
| Performance | `PMS_RUN_PERF=1` | budgets, large result, lifecycle trends | Pass |
| Endurance | accelerated deterministic loops | documents, sessions, connections, caches, timers, disposal | Pass |
| Manual release | native Release executable | shell, menus, shortcuts, layout, accessibility names, shutdown | Pass for disconnected shell; connected native journey remains pending |

The authoritative Sprint 44 run was:

```powershell
.\scripts\test-release.ps1 -Repeat 3 -SkipCoverage -IncludeLargeDataset
```

Result: 384 tests passed in each of three consecutive iterations (1,152 test
executions), 0 failed, 0 skipped; Release build 0 warnings and 0 errors;
isolated PostgreSQL resources removed successfully. Evidence:
`TestResults/eb184b17b4/release-summary.json`.

## Critical workflow coverage

| Required workflow | Unit/component | PostgreSQL/OS | WPF/E2E | Result |
|---|---|---|---|---|
| Startup and shutdown | lifecycle | — | native + Desktop | Pass |
| Main menu command routing | command state | — | shared routed commands | Pass |
| Connection creation/connect | profile/configuration | live probe | dialog composition | Pass |
| Multiple sessions | immutable context | app/read-only roles | tab ownership | Pass |
| New query/file open | document/file | filesystem | menu/toolbar/shortcut | Pass |
| Execute/cancel/multiple result sets | lifecycle/result store | live SQL/cancel | routed commands/output tabs | Pass |
| Commit/rollback | failure-window policy | live transaction | transaction menu intentionally absent | Pass at supported boundary |
| Connection loss/reconnect | state machine | live backend termination | stale/disabled state | Pass |
| Object Explorer refresh | generation coordinator | hostile/large metadata | active-context refresh | Pass |
| Object scripting | quoting/generation | hostile identifiers | no context entry point | Service only |
| Data editing | — | — | no editable grid | Not supported |
| Search/IntelliSense supersession | latest-request coordinators | live object search | command/editor entry | Pass |
| Backup/restore | plans/guards/process | live custom/plain | command entry | Pass |
| Export/import | atomic file/reader/plans | live import | command entry | Pass, limited UI |
| Monitoring and session actions | presentation/action safety | activity/termination | monitor view only | Service-only destructive actions |
| Settings persistence | corrupt/migration/save | filesystem | startup composition | Pass; Options UI absent |
| Workspace recovery | atomic/corrupt snapshots | filesystem | disconnected restoration | Pass |
| Credential redaction | nested/structured redaction | live auth failure | startup/diagnostics redaction | Pass |
| Read-only/production guard | policy/confirmation | restricted roles | command gating | Pass |
| Large-result limits/disposal | memory/page contracts | 100k/1m fixtures | paged grid | Pass |

## High-risk interaction dimensions

| Dimension | Evidence | Status / residual risk |
|---|---|---|
| Connected / disconnected | state-machine, command-state, native disconnected shell | Pass |
| Local / remote | local TCP only | Remote latency/failure campaign pending |
| Single / multiple connections | concurrent editors, mixed app/read-only roles | Pass |
| Read-write / read-only | profile policy plus PostgreSQL restricted role | Pass |
| Development / production-classified | profile safeguard unit/desktop tests | Pass; no separate production server used |
| Normal / restricted role | three isolated roles | Pass |
| Empty / large database | empty states plus large-schema and million-row fixtures | Pass |
| Fast / slow connection | deterministic delay/cancellation coordinators | Pass; no network shaping |
| Idle / active transaction | transaction state/failure-window tests | Pass at application boundary |
| Small / large result | scalar through million-row fixture | Pass |
| Normal / hostile names | Unicode, quotes, reserved/long identifiers | Pass |
| Clean / restored startup | Desktop startup and recovery tests | Pass |
| Light / dark theme | light only | Dark theme not supported |
| Restored / maximised / multi-monitor | restored and 3840-wide maximised native shell | Multi-monitor removal not tested; layout is not persisted |
| PostgreSQL versions | 18.4 | intended minimum 14 remains unqualified |

## PostgreSQL/deployment compatibility

| Target | Result | Evidence / limitation |
|---|---|---|
| PostgreSQL 18.4, local Windows, password auth | Pass | complete isolated suite and utilities |
| PostgreSQL 14 intended minimum | Not run | no PostgreSQL 14 service or container runtime is installed |
| Docker-hosted PostgreSQL | Not run | Docker engine unavailable |
| Remote PostgreSQL | Not run | no user-authorised remote endpoint |
| TLS | Not run | local server reports `ssl=off` |
| Certificate authentication | Not run | no certificate-auth test endpoint |
| Restricted role | Pass | read-only and restricted isolated roles |
| Superuser administration | Pass | fixture provisioning, database create/drop, restore cleanup |
| `pg_dump` / `pg_restore` | Pass | PostgreSQL 18 client utilities matched server major |

Version-specific catalog SQL uses capability/version guards where implemented.
Unavailable server capabilities must remain recoverable failures; this matrix
does not claim PostgreSQL 14, TLS, Docker, remote, or certificate qualification.

## Journey results

| Journey | Automated evidence | Manual evidence | Result |
|---|---|---|---|
| A — daily development | startup, metadata, execute/results, file/recovery, shutdown | shell/new-query/layout | Pass at automated production boundaries |
| B — multi-environment | mixed roles, independent contexts, cancellation isolation, production guard | disconnected tab routing | Pass without a real remote production endpoint |
| C — schema change | disposable hostile fixtures, metadata refresh, generated SQL tests | — | Pass at service boundary; Object Explorer mutation UI absent |
| D — failure recovery | live backend termination, explicit reconnect, old-result preservation | — | Pass; full Windows service stop is ACL-blocked |
| E — backup/restore | live custom/plain backup and restore, validation/cleanup | — | Pass |
| F — long session | accelerated lifecycle, million-row fixture, handle/memory budgets | clean native shutdown | Pass accelerated; multi-hour soak pending |
