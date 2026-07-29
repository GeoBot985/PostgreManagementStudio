# Release scope reset

This is the current product scope derived from the Sprint 49 reachability audit.
The release should be coherent and reliable rather than claim broad
administration coverage that is not composed into the desktop.

| Feature area | Decision | Current state | User value | Dependency | Complexity | Principal risk | Order |
|---|---|---|---|---|---|---|---:|
| Connection management | KEEP_FOR_CURRENT_RELEASE | END_TO_END_REACHABLE | Establishes the trusted PostgreSQL session used by every supported workflow | Provider, recovery, profile store | medium | Remote/authentication/version scope is narrower than a general-purpose client | 1 |
| Object Explorer | KEEP_FOR_CURRENT_RELEASE | END_TO_END_REACHABLE | Provides familiar database orientation, lazy metadata browsing, scripts, and safe context actions | Metadata, scripting/action services and shell | medium | Advanced object designers and some complete DDL attributes remain deferred | 2 |
| SQL editor and files | KEEP_FOR_CURRENT_RELEASE | END_TO_END_REACHABLE | Core query-authoring workflow | Query document, file service, recovery | medium | Basic editor lacks a full completion/editor surface | 3 |
| Query execution/cancellation | KEEP_FOR_CURRENT_RELEASE | END_TO_END_REACHABLE | Primary reliable database interaction | Query executor, lifecycle, recovery | medium | Concurrency and cancellation regressions | 4 |
| Results/export | KEEP_FOR_CURRENT_RELEASE | END_TO_END_REACHABLE | Makes query output useful and portable | Result store, serializers, paging | medium | Output is compact and not editable | 5 |
| Session restoration | KEEP_FOR_CURRENT_RELEASE | END_TO_END_REACHABLE | Protects unsaved SQL after restart/failure | Recovery snapshot store | small | No selective recovery manager | 6 |
| Query plans | KEEP_FOR_CURRENT_RELEASE | END_TO_END_REACHABLE | Helps users understand query cost and access paths | Plan provider plus explorer workspace | large | Actual-plan side effects and plan comparison remain deferred | 7 |
| Backup/restore | KEEP_FOR_CURRENT_RELEASE | END_TO_END_REACHABLE | Essential data-safety workflow | External tools, validators, destructive guard | large | Target/tool/version qualification and post-restore refresh | 8 |
| Object search | KEEP_FOR_CURRENT_RELEASE | END_TO_END_REACHABLE | Fast navigation across database objects | Search service and shell workspace | medium | Object activation/history remain deferred | 9 |
| Live activity/session monitor | KEEP_FOR_CURRENT_RELEASE | END_TO_END_REACHABLE | Operational visibility and safe session intervention | Activity/session services and routed actions | large | Query/database statistics and richer filtering remain deferred | 10 |
| Data-transfer wizard | COMPLETE_BEFORE_RELEASE | PARTIALLY_REACHABLE | Safe import/migration with review and progress | Transfer service, mapping and transaction policy | large | Partial import, constraints and rejected-row handling | 11 |
| Settings/layout | DEFER_TO_LATER_RELEASE | SERVICE_ONLY/PARTIALLY_REACHABLE | User control over defaults and shell state | Settings store and shell persistence | medium | Persistence semantics not yet user-visible | 12 |
| Transaction workspace | DEFER_TO_LATER_RELEASE | SERVICE_ONLY | Explicit transaction control for advanced users | Query executor and recovery policy | large | Misrepresenting commit/rollback state | 13 |
| Security roles | DEFER_TO_LATER_RELEASE | DIAGNOSTIC_OR_TEMPORARY_UI | Administration of access and privileges | Security service and destructive guard | large | Permission mistakes and privilege escalation | 14 |
| Maintenance | KEEP_FOR_CURRENT_RELEASE | END_TO_END_REACHABLE | Routine database care | Maintenance service, version detection and destructive guard | medium | Current target scope is the active database; lock duration remains workload-dependent | 10 |
| Query performance dashboard/history | DEFER_TO_LATER_RELEASE | SERVICE_ONLY | Find regressions and expensive queries | Performance models and `pg_stat_statements` | large | Extension availability and interpretation | 16 |
| Index analysis/recommendations | KEEP_FOR_CURRENT_RELEASE | END_TO_END_REACHABLE | Inspect and safely review access paths | Index metadata, analysis, maintenance and destructive guard | large | Create/drop/editor actions and object-level targeting remain deferred | 17 |
| Schema compare/synchronisation | KEEP_FOR_CURRENT_RELEASE | END_TO_END_REACHABLE | Review and export schema differences safely | Extractor, comparison, planner and preview | large | Direct execution, dependency extraction and snapshot sources remain deferred | 18 |
| Object scripting | KEEP_FOR_CURRENT_RELEASE | END_TO_END_REACHABLE | Generates reviewable PostgreSQL DDL/DML and provides safe object actions | Object metadata, scripting/action services, query documents | large | Unsupported advanced DDL attributes must remain explicit and must not be represented as complete | 19 |
| SQL IntelliSense | DEFER_TO_LATER_RELEASE | SERVICE_ONLY | Faster and safer query authoring | Completion engine plus visible editor control | medium | Incorrect/stale metadata suggestions | 19 |
| Query history | DEFER_TO_LATER_RELEASE | SERVICE_ONLY | Reuse prior work safely | Recent-files/history persistence | medium | Sensitive SQL retention and recovery semantics | 20 |
| Diagnostics | KEEP_FOR_CURRENT_RELEASE | END_TO_END_REACHABLE for activity snapshots; Help > Diagnostics remains temporary | Supportability and safe failure investigation | Redaction/diagnostic services | small | Full support bundle and query/database sections remain deferred | 21 |

## Current-release claims

The current release should advertise a compact SQL editor, PostgreSQL
connection/session, Object Explorer browsing, query execution/cancellation,
result viewing/export, basic file operations, recovery snapshots, restore
review/execution, database object search, index inspection/reindex, and schema
comparison with synchronisation preview/script review, and server activity,
blocking/lock diagnostics with privacy-aware activity snapshots. It should not
advertise a full administration suite, query-performance/database-statistics
dashboards, role management, automatic schema synchronisation, automatic index
recommendations, or visual plan analysis.

## Sprint 57 decision

This scope is approved only for the frozen internal RC identified in
`docs/release/FINAL_RC_CANDIDATE.md`. It remains unchanged pending clean-Windows
and stateful-upgrade qualification; no deferred feature is restored to scope.

## Sprint 58 scope change

The source tree now includes Object Explorer scripting and context actions for
the supported scope in `docs/features/object-explorer-scripting.md`. The frozen
Sprint 57 `0.9.0-rc.3` package predates this change. A new package and release
qualification are required before release claims may include Sprint 58.
