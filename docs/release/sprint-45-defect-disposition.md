# Sprint 45 defect disposition

## Scope and baseline

Sprint 45 revalidated the Sprint 44 defect register and its evidence rather
than introducing new product functionality. The baseline revision was
`283b8e7a7a2942e356a7142e577f2d1f0c8c58b3` (`Sprint 044: stabilise full-system
regression`). The verification environment was Windows `10.0.26200`, .NET
SDK `10.0.302`, Npgsql `8.0.6`, and PostgreSQL `18.4` x64. The test database
and disposable roles were created and cleaned by the release harness.

## Sprint 44 register

| ID | Severity | Disposition | Owner/root cause | Verification |
|---|---|---|---|---|
| S44-001 | High | Resolved | Settings/recovery state was not isolated from an alternate profile path | `ProductionCompositionTests.AlternateSettingsPath_IsolatesProfilesAndRecoveryFromUserState` |
| S44-002 | Critical | Resolved | Startup failure boundary emitted raw nested exception text | Startup redaction coverage and native disconnected launch |
| S44-003 | Critical | Resolved | Recovery snapshots were not composed into the shell lifecycle | `ShellWorkflowTests.RecoveredWorkspace_RestoresUnsavedSqlWithoutConnectionOrExecution` plus atomic snapshot tests |
| S44-004 | High | Resolved | Object Explorer accepted late results without logical-session/generation identity | Object Explorer context/cancellation tests and multi-session integration suite |
| S44-005 | Medium | Resolved | Database commands were not represented by the canonical menu structure | `ShellWorkflowTests` menu/accelerator/accessibility checks |
| S44-006 | High | Resolved | Resource test measured process handles while parallel tests were active | Nonparallel resource collection, warmup, pool cleanup, and repeated Release run |

No item was reopened, duplicated, or closed as “cannot reproduce”. The
environment-specific Sprint 44 qualification limitations remain tracked
separately below; they are not hidden code defects.

## Root-cause groups

The six defects reduce to four reusable causes:

1. State ownership: alternate settings, recovery files, logical sessions,
   physical generations, and Object Explorer results now have explicit
   ownership boundaries.
2. Trust boundaries: startup diagnostics use recursive secret redaction;
   credentials and connection strings are not used as diagnostic payloads.
3. Lifecycle/disposal: recovery writes are atomic and debounced, and resource
   measurements isolate test ownership from unrelated parallel work.
4. Desktop command composition: the menu, shortcuts, toolbar, and context
   actions use the established shared command surface.

## Regression coverage

The Sprint 44 regression additions remain the permanent coverage for every
resolved blocker/critical/high defect. Sprint 45 found no missing regression
case requiring a production change. The affected groups were rerun in the
full Release harness: Core 188, Results 63, PostgreSQL 54, Desktop 19, and
Integration 60 tests per iteration.

The high-risk scenarios covered are multi-session execution and cancellation,
transaction/reconnect state transitions, recovery without auto-execution,
Object Explorer stale-result rejection, backup/restore process safety, large
result/resource lifecycle, diagnostics redaction, and native menu/accessibility
reachability.

## Counts

| Severity | Before Sprint 45 | After Sprint 45 |
|---|---:|---:|
| Blocker | 0 | 0 |
| Critical | 0 | 0 |
| High code defects | 0 | 0 |
| Medium implemented-surface defects | 0 | 0 |
| Low UI/roadmap items | 2 | 2 |

The starting zeroes are intentional: Sprint 44 had already fixed S44-001
through S44-006. Sprint 45 verified that integration did not regress them.

## Release decision

The implemented release surface is suitable for installer, deployment, and
packaging qualification. Final shipment remains conditional on PostgreSQL 14,
remote TLS/password and certificate authentication, connected native
exploration, and a multi-hour target-workstation soak. Those are environment
and product-scope gates, not unresolved wrong-target, data-loss, corruption,
credential-exposure, or crash defects.
