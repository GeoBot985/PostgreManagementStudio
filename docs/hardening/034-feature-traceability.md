# Sprint 34 feature traceability

Classification is based on production reachability, not sprint completion
labels. “Unit” means deterministic service coverage; “PG” means a live
PostgreSQL integration test; “manual” means no automated UI evidence.

| Sprint | Major accepted capability | Implementation evidence | Test evidence | Reachability / manual status | Confidence | Blocker |
|---:|---|---|---|---|---|---|
| 1 | streaming query, structured terminal events, cancellation | `Core/QueryContracts.cs`, `Postgres/NpgsqlQueryExecutor.cs` | `QueryExecutionIntegrationTests` (PG) | Execute/Cancel buttons; manual UI | High | No |
| 2 | typed, bounded result storage | `Results/ResultSession*`, `ResultSetStore.cs` | Results + integration storage tests | used by query tab | High | No |
| 3 | formatting and clipboard serialization | `DefaultResultValueFormatter.cs`, `ResultSerializers.cs` | formatting/serialization tests | display/copy reachable | High | No |
| 4 | SQL document execution workflow | `QueryDocument.cs`, `QueryTabManager.cs` | `QueryDocumentTests` | query tabs reachable; close lifecycle partial | Medium | Yes |
| 5 | SQL file productivity and recovery | file/recent/recovery/find services | `FileManagementTests` | open/save/find reachable; recent/recovery not composed | Medium | No |
| 6 | metadata completion | completion engine/cache and Npgsql metadata provider | `CompletionTests` | Ctrl+Space reachable; production metadata not composed | Medium | No |
| 7 | multi-result grid | `QueryTabView.CreateResultTab`, Results store | Results tests | reachable, capped display | High | No |
| 8 | result export | `ResultExportService.cs` | `ResultExportTests` | export dialog reachable | High | No |
| 9 | non-destructive result transforms | `ResultViewTransformation.cs` | transformation tests | sort/search reachable; full filter UI absent | High | No |
| 10 | backup/restore commands | `BackupRestore.cs` | `BackupRestoreTests` | temporary buttons; no manager/history UI | Medium | No |
| 11 | roles/permissions | `SecurityManagement.cs`, `NpgsqlSecurityService.cs` | security unit tests | role listing only; mutation UI absent | Medium | Yes |
| 12 | activity snapshots | `ActivityMonitoring.cs`, `NpgsqlActivityService.cs` | activity tests | one-shot text output | Medium | No |
| 13 | maintenance operations | `DatabaseMaintenance.cs`, Npgsql adapter | maintenance tests | VACUUM-only temporary action | Medium | No |
| 14 | import/export services | `DataTransfer.cs`, Npgsql adapter | data-transfer tests | reduced CSV import; result export separate | Medium | No |
| 15 | database object search | `ObjectSearch.cs`, Npgsql adapter | object-search tests | temporary prompt/text results | Medium | No |
| 16 | backup/restore manager | `BackupRestoreManager.cs` | manager tests | not used by desktop backup buttons | Medium | Yes |
| 17 | plan execution/parsing | `ExecutionPlans.cs`, Npgsql plan adapter | execution-plan tests | estimated/actual reachable; raw JSON only | Medium | No |
| 18 | session management | `SessionManagement.cs`, Npgsql session service | session tests | actions not reachable | Medium | Yes |
| 19 | schema compare/sync | `SchemaComparison.cs`, schema extractor | schema tests | compares active connection to itself; no execution | Low | Yes |
| 20 | synchronization preview | preview types in schema comparison | schema tests | not reachable | Medium | Yes |
| 21 | transfer wizard | `DataTransferWizard.cs` | data-transfer tests | wizard not reachable; reduced import is | Medium | Yes |
| 22 | activity/session workspace | activity/session services | activity/session tests | snapshot only; workspace/actions absent | Medium | Yes |
| 23 | pg_stat_statements analysis | `QueryPerformance.cs` | query-performance tests | no collector or desktop entry point | Medium | Yes |
| 24 | plan analysis/visualization | `ExecutionPlanAnalysis.cs` | plan-analysis tests | diagnostics not shown in desktop | Medium | Yes |
| 25 | index analysis/recommendations | `IndexAnalysis.cs` | index-analysis tests | no collector or desktop entry point | Medium | Yes |
| 26 | query plan explorer | `ExecutionPlanExplorer.cs` | explorer tests | tree helper exists but is unused | Medium | Yes |
| 27 | plan comparison/regression | `PlanComparisonRegression.cs` | comparison tests | not reachable | High (service) | Yes |
| 28 | performance history/baselines | `QueryPerformanceHistory.cs` | history tests | repository contract only; not persisted/reachable | Medium | Yes |
| 29 | live session monitor | `SessionMonitorWorkspace.cs` | workspace tests | not composed; one-shot activity remains | Medium | Yes |
| 30 | activity monitor presentation | `ActivityMonitorPresentation.cs` | presentation tests | dedicated workspace not composed | Medium | Yes |
| 31 | query performance dashboard | `QueryPerformanceDashboard.cs` | dashboard tests | no collector or desktop entry point | Medium | Yes |
| 32 | execution plan explorer extension | plan explorer/analysis services | explorer tests | raw JSON path used; structured tab unused | Medium | Yes |
| 33 | index workspace | `IndexAnalysisWorkspace.cs` | workspace tests | no PostgreSQL collector or desktop entry point | Medium | Yes |

## Acceptance interpretation

The service implementations above preserve useful approved work. They should
not be deleted or marked “complete and reachable” without production
composition and workflow tests. Sprint 35 should turn this matrix into
command-level regression tests, starting with rows marked as blockers.

## Sprint 35 automated coverage update

`A` automated, `M` manual, `B` blocked, `—` not applicable. Classifications
apply to the major capability represented by each Sprint 34 row.

| Sprint | Unit/component | PostgreSQL/OS integration | UI integration | Result |
|---:|---|---|---|---|
| 1 | A | A | A reachability | Verified |
| 2 | A | A + performance | A materialization | Verified |
| 3 | A | A typed seed | M clipboard | Partial |
| 4 | A | A execution | A shell/editor lifecycle | Verified except persistent transaction |
| 5 | A | filesystem A | M commands | Partial |
| 6 | A | A metadata | A Object Explorer path | Verified |
| 7 | A | A typed seed | A grid creation | Verified |
| 8 | A | filesystem A | M dialog | Partial |
| 9 | A | A typed data | M interactions | Partial |
| 10 | A | A pg_dump | A command reachability | Backup verified; restore B |
| 11 | A | A role permissions | M mutations | Partial |
| 12 | A | A pg_stat_activity | A reachability | Verified snapshot |
| 13 | A | B destructive execution | M | Blocked safety |
| 14 | A | A import/query and file export | M wizard | Partial |
| 15 | A | A object search | M navigation | Partial |
| 16 | A | A tool/process | M manager | Partial |
| 17 | A | A estimated plan | A reachability | Verified estimated |
| 18 | A | B destructive session action | M | Blocked safety |
| 19 | A | A extractor | B distinct endpoints | Blocked |
| 20 | A | — | B workspace | Blocked |
| 21 | A | partial | B wizard | Blocked |
| 22 | A | A activity | B workspace/actions | Blocked |
| 23 | A | B collector | B workspace | Blocked |
| 24 | A | A plan | B diagnostics UI | Blocked |
| 25 | A | seeded metadata indirect | B collector/workspace | Blocked |
| 26 | A | A plan | B explorer UI | Blocked |
| 27 | A | — | B comparison workspace | Blocked |
| 28 | A | — | B persistence/workspace | Blocked |
| 29 | A | A activity | B live workspace | Blocked |
| 30 | A | A activity | B dedicated monitor | Blocked |
| 31 | A | B pg_stat_statements | B dashboard | Blocked |
| 32 | A | A plan | B structured explorer | Blocked |
| 33 | A | seeded metadata indirect | B collector/workspace | Blocked |

## Sprint 36 SQL editor hardening update

Sprint 4 query execution and Sprint 7 result presentation now have explicit
execution-ID lifecycle control, immutable connection/database snapshots,
bounded 10,000-row production retention, structured PostgreSQL errors,
provider-level cancellation, tab-scoped user transactions, and deterministic
shutdown disposal. Live coverage includes ten concurrent executions, streaming
cancellation, transaction abort/rollback, backend termination, missing
database, timeout, unusual values, multi-statement ordering, and truncation.
