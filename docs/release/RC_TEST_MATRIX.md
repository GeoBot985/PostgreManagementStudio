# Release-candidate test matrix

Supported claims are deliberately limited to Windows 11 x64 and PostgreSQL
18.4. “Pass” means the stated evidence exists; “not tested” is not a pass.

| Area | Scenario | Preconditions / risk | Type | Expected result | Status | Evidence / defect |
|---|---|---|---|---|---|---|
| Packaging | Build self-contained ZIP | Clean source, none | Packaging | No test/debug/secrets; manifest and hashes | Pass | `build-release.ps1`, package verification |
| Installation | Extract, install, shortcut, launch | Isolated profile, low | Installation | Per-user install and executable present | Pass | `test-installer.ps1` |
| Repair | Run installer repair | Installed package, low | Installation | App files restored; user state retained | Pass | `test-installer.ps1` |
| Removal | Uninstall and reinstall path | Installed package, low | Installation | Binaries removed; user state policy honoured | Pass | `test-installer.ps1` |
| First launch | Launch with empty state | Isolated profile, none | Manual/package | Visible disconnected shell; no developer dependency | Conditional pass | Prior RC shell qualification; clean VM still open RC-002 |
| Connections | Create/test/save/connect/reconnect/disconnect | PostgreSQL 18.4, metadata | Integration/UI | Correct target and credential-safe failure | Pass in prior isolated suite | Sprint 47 evidence; rerun requires DB |
| Object Explorer | Load, lazy expand, refresh, stale context | PostgreSQL 18.4, metadata | Integration/UI | Bounded metadata and correct selection | Pass in prior isolated suite | existing tests; context fix in Sprint 55 |
| SQL editor | New/open/save/save-as/recovery | Local files, none | Automated/UI | Durable SQL and safe close prompts | Pass | Desktop/Core tests |
| Execution | Query, notices, errors, multi-result, cancellation | PostgreSQL 18.4, data | Integration | Structured terminal state and recovery | Pass in prior isolated suite | Sprint 47 evidence |
| Results/export | Format, paging, copy, cancel, temp cleanup | Result set, metadata | Automated | Typed output and no partial success claim | Pass | Results tests; Sprint 53 |
| Plans | Estimated/actual plan safety | PostgreSQL 18.4, query | Integration/UI | Plan shown with side-effect warning | Pass in prior isolated suite | plan tests/report |
| Search | Search objects and filter results | PostgreSQL 18.4 | UI/integration | Cancellable bounded results | Pass in prior isolated suite | Sprint 51 evidence |
| Backup/restore | Backup, inspect, restore confirmation | Disposable DB/tools, high | Destructive integration | Exact target and safe outcome | Pass in prior isolated suite | Sprint 47 evidence |
| Maintenance/index | Review and reindex | Disposable DB, high | Destructive integration | Version-aware target action | Pass in prior isolated suite | Sprint 51/52 evidence |
| Schema | Compare and preview synchronisation | Two disposable DBs, high | Integration/UI | Review-only script; no implicit execution | Pass in prior isolated suite | Sprint 52 evidence |
| Import/export | CSV import and result export | Disposable DB/files, medium | Integration/UI | Bounded mapping, progress, cancellation | Pass in prior isolated suite | Sprint 53 evidence |
| Activity | Monitor, blocking, locks, session actions | PostgreSQL 18.4, high | Integration/UI | Fresh target revalidation and safe action | Conditional pass | Sprint 54; live DB rerun required |
| Query history | Browse/filter/restore history | Feature deferred | UI | Not exposed as current scope | Deferred | Matrix / RC-005 |
| Query/database statistics | `pg_stat_statements` and DB metrics | Adapter not composed | UI/integration | Explicit unavailable state | Deferred | Sprint 54 report |
| Settings | Edit and persist user preferences | Settings UI deferred | UI | No false route | Deferred | Known limitations |
| Upgrade | Upgrade prior stable package | Prior package/clean profile | Upgrade | State preserved and migration idempotent | Not tested | RC-002/RC-003 |
| Recovery | Corrupt optional settings/profile | Isolated profile, low | Automated | Defaults load; original preserved | Pass | Core security/settings tests |
| Shutdown | Close with active operations | UI/integration | UI/recovery | No hang; work cancelled/detached | Pass in existing lifecycle tests | Sprint 54/55 tests |
| Compatibility | PostgreSQL 14–17 | Servers unavailable | Compatibility | Version result documented, no claim | Not tested | RC-003 |
| Compatibility | PostgreSQL 18.4 | Disposable environment required | Integration | Supported matrix passes | Pass in prior qualification | Sprint 47 evidence |
| Windows | 100/125/150% DPI, multi-monitor, standard user | Clean machines required | Manual | No clipping/off-screen state | Not tested | RC-002 |
| Security | Package/log/snapshot credential scan | Package and tests | Security/packaging | No secrets or dev strings | Pass | `verify-package.ps1`, security tests |
