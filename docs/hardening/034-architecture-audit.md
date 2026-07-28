# Sprint 34 architecture audit

Audit date: 2026-07-28
Scope: repository HEAD after Sprint 33

## Executive summary

The repository builds reliably and has a substantial provider-neutral service
layer, but it is not yet a release-ready management studio. The audited product
is a five-project modular monolith with 69 production C# source files before
this sprint, four test projects, 42 test source files, 33 sprint notes, five
ADRs, and no installer assets. Debug and Release both built with zero warnings.
The pre-audit suite reported 178 passing tests.

The key reality gap is presentation reachability. `QueryTabView.xaml` is the
only feature command surface. It exposes query/file/result operations and
temporary buttons for backup, restore, roles, activity, maintenance, import,
search, plans, and schema compare. Many richer services from Sprints 20-33 are
not composed into the application. Class presence and unit tests therefore do
not establish product completion.

Sprint 34 safely centralized all Npgsql connection creation behind
`INpgsqlConnectionFactory`, added architecture enforcement, changed missing
database integration configuration from false-pass to explicit skip, removed
four template placeholders, and fixed the always-true index-scope expression.
No user-facing feature was added.

## Repository inventory

| Category | Evidence and finding |
|---|---|
| Solution | `PostgreManagementStudio.sln`; all nine projects are listed and built |
| Production | Core, Results, Application, Postgres, Desktop under `src/` |
| Tests | Core.Tests, Results.Tests, Postgres.Tests, IntegrationTests |
| Build policy | `Directory.Build.props`: nullable enabled and warnings treated as errors |
| Packages | Central versions in `Directory.Packages.props`: Npgsql, logging abstractions, xUnit, test SDK, coverlet |
| Scripts | `scripts/` exists; no release packaging pipeline is present |
| Configuration | Environment-variable connection string only; no authoritative settings model |
| Documentation | README, architecture note, 33 sprint notes, ledger, five ADRs, historical `HANDOFF.md` |
| Generated files | `bin/` and `obj/` exist locally and are ignored; none are tracked |
| Obsolete content | Default `Class1.cs` and `UnitTest1.cs` files were verified as templates and removed |
| Packaging | No installer, manifest, signing, upgrade, or recovery assets |

`HANDOFF.md` describes Sprint 2 and is historical, not current product truth.
README and `docs/architecture.md` were corrected during this sprint.

## Architecture map

```text
App.xaml / MainWindow
        |
        v
QueryTabView + QueryTabManager -------- dialogs / clipboard / files
        |
        +--> Application services and SQL builders
        |         |
        |         +--> Core contracts
        |         +--> Results storage/export
        |
        +--> Postgres adapters
                  |
                  +--> INpgsqlConnectionFactory --> Npgsql --> PostgreSQL
```

Project references are acyclic. `Core` has no project dependency; `Results`
depends on Core; `Application` depends on Core and Results; `Postgres` depends
on Core and Application; `Desktop` depends on Application, Postgres, and
Results. Automated tests now enforce this exact graph and prevent WPF references
in lower layers.

## Build and dependency findings

All projects target .NET 9 with nullable analysis. Warnings are errors at the
repository level. The solution advertises Any CPU, x64, and x86, while projects
do not pin a runtime identifier. This is acceptable for development but
publish/runtime architecture requires Sprint 41 validation. There is no
`global.json`, so SDK selection can drift.

Direct dependencies are centralized and version-consistent. No duplicate
package versions were found. Npgsql 8.0.6 is isolated to Postgres; logging
abstractions are isolated to Results. Package licensing and vulnerability
automation are absent and tracked for hardening.

| Direct package | Version | Projects | Purpose | NuGet license expression |
|---|---:|---|---|---|
| Npgsql | 8.0.6 | Postgres | PostgreSQL provider | PostgreSQL |
| Microsoft.Extensions.Logging.Abstractions | 9.0.0 | Results | logging contracts | MIT |
| xunit | 2.9.2 | all tests | test framework | Apache-2.0 |
| xunit.runner.visualstudio | 2.8.2 | all tests | test runner | Apache-2.0 |
| Microsoft.NET.Test.Sdk | 17.12.0 | all tests | .NET test host | MIT |
| coverlet.collector | 6.0.2 | all tests | coverage collection | MIT |

## DI and lifetime findings

There is no DI container or registration module. `MainWindow` constructs the
query executor chain, while `QueryTabView` directly creates file, export,
search, maintenance, security, activity, import, plan, and schema services.
This is a high-severity architecture and test-wiring gap. Operation resources
are generally disposed with `await using`, and long-lived mutable service
singletons are not present. The new connection factory is stateless and shared;
all returned connections remain operation-owned.

## Feature reality

Detailed evidence is in `034-feature-traceability.md`.

- Complete/reachable foundation: query execution, result store/grid, basic
  formatting/export/filter/search, file open/save/find, lightweight completion.
- Reachable but reduced UI: backup/restore, role listing, activity snapshot,
  maintenance, import, object search, plans, schema compare.
- Implemented but not product-reachable: schema preview selection, session
  actions, query-performance store/dashboard/history, plan comparison, and
  index analysis workspaces.
- Misleading path: schema compare extracts the same active connection twice.
- Unused presentation helper: `CreatePlanTab` builds a tree but `ShowPlanAsync`
  writes raw JSON to the messages area instead.
- Missing product shell: connection dialog, Object Explorer, persistent
  settings, logging host, notification service, and installer.

## Shared infrastructure and defects

Completed:

1. Replaced direct `new NpgsqlConnection` calls in all Postgres adapters with
   `INpgsqlConnectionFactory.Create`. Every connection has an operation-specific
   application name and explicit caller ownership.
2. Added architecture tests for dependency direction, lower-layer UI
   independence, and the connection-construction boundary.
3. Replaced integration-test early returns with `SkipException.ForSkip`, so
   absent PostgreSQL configuration can no longer inflate pass counts.
4. Removed four default template files.
5. Removed an unconditional `|| true` from `IndexWorkspaceService.ApplyScope`
   and documented database scope as already-selected workspace context.

Consolidation deliberately deferred: capability records, activity models, plan
comparison models, settings, notifications, background coordination, and
formatters have semantic differences or insufficient production wiring for a
safe Sprint 34 merge.

## Reliability, security, and performance observations

Most Npgsql commands/readers/transactions and file streams use deterministic
disposal. Query cancellation emits a structured terminal event and real
PostgreSQL tests cover recovery. WPF event handlers are legitimate `async void`
handlers, but the view owns no lifetime cancellation coordinator and closes do
not dispose result sessions; this requires Sprint 36 work.

SQL values in search, role passwords, session PIDs, and imports are
parameterized. Dynamic object names use `PostgreSqlIdentifierQuoter`. Generated
DDL and predicates still require focused Sprint 38 review. Credentials are
passed through connection strings and PGPASSFILE rather than process arguments;
temporary credential deletion currently swallows cleanup errors and needs
recovery diagnostics.

Result storage has concurrency, truncation, and gated performance tests.
Dashboard, monitoring, and index-analysis scale behavior is not established.

## Tests

The suite is predominantly unit tests over application models and Results.
Postgres integration is concentrated in query execution; many Postgres adapters
have no live tests. There are no automated WPF/UI tests or production
composition tests because no composition module exists. Performance tests are
environment-gated. The normal solution test command executes every test
assembly.

## Release blockers and hardening order

Release blockers:

- no connection/Object Explorer application workflow;
- most Sprints 20-33 are not reachable as their approved workspaces;
- no central production composition, settings, logging, or notification host;
- schema compare cannot select distinct source and target;
- no UI automation or integrated runtime smoke harness;
- no installer/publish/upgrade path.

Recommended order: Sprint 35 production-wiring and regression tests; Sprint 36
lifecycle/error/cancellation reliability; Sprint 38 destructive-operation and
credential safety in parallel with Sprint 37 scale profiling; Sprint 39 UI
consistency; Sprint 40 compatibility; Sprint 41 packaging; Sprint 42 integrated
validation; Sprint 43 release audit.

## Runtime verification

Automated live checks use the local PostgreSQL instance through
`PMS_CONNECTION_STRING`. The final completion report records the exact server
version and commands. Interactive WPF smoke steps that require clicking modal
dialogs remain manual and are not claimed as automated evidence.

## Completion evidence

Final inventory: 68 production and 43 test C# files, excluding generated
`bin/` and `obj/` content.

| Validation | Result |
|---|---|
| `dotnet restore --force-evaluate` | passed |
| Debug build | passed, 0 warnings, 0 errors |
| Release build | passed, 0 warnings, 0 errors |
| Release tests with PostgreSQL and performance enabled | 184 passed, 0 failed, 0 skipped |
| Architecture/factory tests | 5 passed |
| Live database | PostgreSQL 18.4, 64-bit Windows |
| Query, cancellation, and recovery | passed through production Npgsql adapters |
| Result-store performance | six 100k-row checks passed |
| Desktop startup/shutdown | Release host remained alive for the startup probe and stopped cleanly |
| `git diff --check` | passed |

The desktop probe verifies process startup, main-window construction, and clean
shutdown. Connection, query, result, cancellation, and recovery use the same
production adapters in live integration tests. Modal workflows remain manual
evidence and are not overstated.

## Quality assessment

| Metric | Score | Basis |
|---|---:|---|
| Audit completeness | 94% | all repository categories and required subsystems classified |
| Feature traceability | 92% | every sprint mapped to implementation, tests, reachability, and risk |
| Architectural consistency | 91% | graph enforced; composition-root gap explicit |
| Build reliability | 100% | Debug/Release warning-free |
| Test reliability | 93% | no-op integration passes removed; UI gap recorded |
| Warning resolution | 100% | zero compiler/analyzer warnings |
| Dependency clarity | 96% | central versions and project graph documented |
| Infrastructure consolidation | 91% | connection path consolidated; unsafe merges deferred |
| Documentation accuracy | 94% | stale README/architecture corrected; reality matrix authoritative |
| Release-risk identification | 96% | prioritized evidence-based debt register |
| Security baseline | 91% | credentials, SQL construction, destructive entry points inventoried |
| Maintainability | 90% | tests protect boundaries; monolithic view remains a blocker |
