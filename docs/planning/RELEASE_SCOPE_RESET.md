# Release scope reset

This is the current product scope derived from the Sprint 49 reachability audit.
The release should be coherent and reliable rather than claim broad
administration coverage that is not composed into the desktop.

| Feature area | Decision | Current state | User value | Dependency | Complexity | Principal risk | Order |
|---|---|---|---|---|---|---|---:|
| Connection management | KEEP_FOR_CURRENT_RELEASE | END_TO_END_REACHABLE | Establishes the trusted PostgreSQL session used by every supported workflow | Provider, recovery, profile store | medium | Remote/authentication/version scope is narrower than a general-purpose client | 1 |
| Object Explorer | KEEP_FOR_CURRENT_RELEASE | END_TO_END_REACHABLE | Provides familiar database orientation and lazy metadata browsing | Metadata provider and shell | medium | No object designers or object actions | 2 |
| SQL editor and files | KEEP_FOR_CURRENT_RELEASE | END_TO_END_REACHABLE | Core query-authoring workflow | Query document, file service, recovery | medium | Basic editor lacks a full completion/editor surface | 3 |
| Query execution/cancellation | KEEP_FOR_CURRENT_RELEASE | END_TO_END_REACHABLE | Primary reliable database interaction | Query executor, lifecycle, recovery | medium | Concurrency and cancellation regressions | 4 |
| Results/export | KEEP_FOR_CURRENT_RELEASE | END_TO_END_REACHABLE | Makes query output useful and portable | Result store, serializers, paging | medium | Output is compact and not editable | 5 |
| Session restoration | KEEP_FOR_CURRENT_RELEASE | END_TO_END_REACHABLE | Protects unsaved SQL after restart/failure | Recovery snapshot store | small | No selective recovery manager | 6 |
| Query plans | KEEP_FOR_CURRENT_RELEASE | END_TO_END_REACHABLE | Helps users understand query cost and access paths | Plan provider plus explorer workspace | large | Actual-plan side effects and plan comparison remain deferred | 7 |
| Backup/restore | KEEP_FOR_CURRENT_RELEASE | END_TO_END_REACHABLE | Essential data-safety workflow | External tools, validators, destructive guard | large | Target/tool/version qualification and post-restore refresh | 8 |
| Object search | KEEP_FOR_CURRENT_RELEASE | END_TO_END_REACHABLE | Fast navigation across database objects | Search service and shell workspace | medium | Object activation/history remain deferred | 9 |
| Live activity/session monitor | DEFER_TO_LATER_RELEASE | SERVICE_ONLY | Operational visibility and safe session intervention | Activity/session services and routed actions | large | Requires refresh/filter/grid and stale-selection safeguards | 10 |
| Data-transfer wizard | COMPLETE_BEFORE_RELEASE | PARTIALLY_REACHABLE | Safe import/migration with review and progress | Transfer service, mapping and transaction policy | large | Partial import, constraints and rejected-row handling | 11 |
| Settings/layout | DEFER_TO_LATER_RELEASE | SERVICE_ONLY/PARTIALLY_REACHABLE | User control over defaults and shell state | Settings store and shell persistence | medium | Persistence semantics not yet user-visible | 12 |
| Transaction workspace | DEFER_TO_LATER_RELEASE | SERVICE_ONLY | Explicit transaction control for advanced users | Query executor and recovery policy | large | Misrepresenting commit/rollback state | 13 |
| Security roles | DEFER_TO_LATER_RELEASE | DIAGNOSTIC_OR_TEMPORARY_UI | Administration of access and privileges | Security service and destructive guard | large | Permission mistakes and privilege escalation | 14 |
| Maintenance | KEEP_FOR_CURRENT_RELEASE | END_TO_END_REACHABLE | Routine database care | Maintenance service, version detection and destructive guard | medium | Current target scope is the active database; lock duration remains workload-dependent | 10 |
| Query performance dashboard/history | DEFER_TO_LATER_RELEASE | SERVICE_ONLY | Find regressions and expensive queries | Performance models and `pg_stat_statements` | large | Extension availability and interpretation | 16 |
| Index analysis/recommendations | DEFER_TO_LATER_RELEASE | SERVICE_ONLY | Improve access paths safely | Index models, plan evidence and script preview | large | Unsafe or low-value recommendations | 17 |
| Schema compare/synchronisation | DEFER_TO_LATER_RELEASE | SERVICE_ONLY | Review and migrate schema differences | Extractor, planner, preview | large | Cross-version/privilege/destructive changes | 18 |
| Object scripting | REMOVE_FROM_UI_AND_CLAIMS | NOT_IMPLEMENTED | Useful only after a safe object-specific workflow exists | Requires new scripting composition | large | Claiming support without implementation | — |
| SQL IntelliSense | DEFER_TO_LATER_RELEASE | SERVICE_ONLY | Faster and safer query authoring | Completion engine plus visible editor control | medium | Incorrect/stale metadata suggestions | 19 |
| Query history | DEFER_TO_LATER_RELEASE | SERVICE_ONLY | Reuse prior work safely | Recent-files/history persistence | medium | Sensitive SQL retention and recovery semantics | 20 |
| Diagnostics | KEEP_FOR_CURRENT_RELEASE | DIAGNOSTIC_OR_TEMPORARY_UI | Supportability and safe failure investigation | Redaction/diagnostic services | small | Must remain credential-safe and bounded | 21 |

## Current-release claims

The current release should advertise a compact SQL editor, PostgreSQL
connection/session, Object Explorer browsing, query execution/cancellation,
result viewing/export, basic file operations, recovery snapshots, restore
review/execution, and database object search. It should not advertise a full
administration suite, live monitoring, role management, schema synchronisation,
index recommendations, query-performance dashboards, or visual plan analysis.
