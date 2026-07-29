# Sprint 44 feature inventory

## Status legend

- **Release surface**: reachable through the production WPF shell and backed by
  production services.
- **Service only**: implemented and tested below the UI boundary, but not a
  complete end-user workspace.
- **Limited**: reachable, with the stated operational limitation.
- **Not supported**: deliberately absent from the release surface.

Automated coverage references the owning test project: Core, Results, Postgres,
Desktop, or Integration. Manual status refers to the Sprint 44 native Windows
session, not to an automated WPF test.

## Shell and document workflow

| Feature | Status | Primary / alternative entry points | Connection / privilege | Automated coverage | Manual status | Known limitations |
|---|---|---|---|---|---|---|
| Application startup and shutdown | Release surface | executable / File > Exit, Alt+F4 | none | Desktop P0 smoke, lifecycle and disposal | Pass at restored and maximised sizes | installer and signed package are outside this repository |
| Traditional menu bar | Release surface | File, Edit, View, Query, Database, Tools, Window, Help / accelerators | contextual | Desktop command-routing tests | Pass, including Alt+D | dynamic recent-file and window lists are absent |
| Standard and query toolbars | Release surface | toolbar / same routed menu and keyboard commands | contextual | Desktop UI integration | Pass | deliberate frequent-action subset only |
| Status bar | Release surface | shell | contextual | Desktop UI integration | Pass | no persisted status history |
| Docking and layout | Limited | fixed split shell / View toggles, Reset Layout | none | Desktop size and shutdown tests | Pass restored and maximised | no free docking, multi-window layout, or layout persistence |
| Multiple query tabs | Release surface | Ctrl+N, File > New Query, toolbar / Ctrl+Tab | each tab owns context | Core document isolation, Desktop tab tests, Integration concurrent editors | Pass for creation | split/duplicated editor views are not supported |
| SQL file open/save/save-as | Release surface | File menu, toolbar, shortcuts | none | Core filesystem tests, Desktop routing | command reachability checked | recent-file menu and external-change prompt are absent |
| Unsaved SQL recovery | Release surface | automatic crash snapshot and startup restoration | restored disconnected; no credential | Core atomic/corrupt-state tests, Desktop restart-state test | automated native shell construction | only unsaved editor text, database name, file identity, encoding, and caret are restored; results/layout are not |
| Find, replace, go-to-line | Release surface | Edit menu and shortcuts / editor controls | none | Core text tests, Desktop routing | not exhaustively clicked | regex search and bookmarks are not implemented |
| IntelliSense | Limited | Ctrl+Space in editor | local lexical completion; metadata service available | Core supersession/cache tests | not manually exercised | rich popup, metadata-aware UI refresh, and formatting are limited |

## Connections, metadata, and execution

| Feature | Status | Primary / alternative entry points | Connection / privilege | Automated coverage | Manual status | Known limitations |
|---|---|---|---|---|---|---|
| Connection profiles | Release surface | File > Connect, query connection button | password/certificate modes; profile policy | Postgres and Desktop tests | dialog not automated because authentication UI is excluded from Computer Use | one active logical session per connection object; no cloud auth |
| Test/connect/disconnect/reconnect | Release surface | connection dialog, File menu, toolbar | normal login | Postgres state-machine and live Integration tests | disconnected gating passed | remote network shaping remains an environment campaign |
| Multi-connection ownership | Release surface | connection per query tab | normal or restricted roles | immutable context, ten concurrent editors, mixed-role cancellation test | tab creation passed | no separate server-window model |
| Object Explorer | Release surface | left pane / View and refresh commands | metadata visibility | Core metadata coordination, Postgres, Integration hostile/large fixtures, Desktop reachability | empty disconnected state passed | context actions for create/rename/drop/properties are not exposed |
| Object search | Release surface | Tools > Search Objects | metadata visibility | Core and live Integration | not manually exercised | opens a temporary result presentation rather than a dedicated workspace |
| Query execution | Release surface | Query > Execute, F5, Ctrl+Enter, toolbar | query privileges and profile safeguards | Core lifecycle, Integration SQL/error/notice/timeout/cancellation | disconnected gating passed | no arbitrary automatic retry |
| Selection execution | Release surface | editor selection plus Execute | query privileges | Core immutable-context tests, Integration execution | not manually exercised | current-statement parser is not exposed separately |
| Cancellation | Release surface | Query > Cancel, Esc, toolbar | owning execution only | Core, Postgres generation tests, live cancellation and mixed-role isolation | disabled state passed | cancellation timeout reports abandonment safely |
| Transactions | Limited | query execution options in application layer | write privilege unless read-only | Core failure-window tests, live commit/rollback/abort recovery | no shell transaction controls | persistent user-managed transaction menu remains deliberately disabled |
| Messages, notices, errors | Release surface | Messages tab | none beyond operation | Core presentation/redaction, Integration notices/errors | visible shell tab passed | no separate diagnostics viewer |
| Execution plans | Release surface | Query menu and toolbar / output tab | estimated normal; actual may execute SQL | Core and live Integration, destructive guard | command gating passed | dedicated graphical explorer/comparison UI is service-only |
| Connection-loss recovery | Release surface | explicit Reconnect | normal login | Postgres state machine, live backend termination, Desktop state tests | not service-stop tested natively | full Windows service stop needs elevation unavailable to the test process |

## Results, files, and administration

| Feature | Status | Primary / alternative entry points | Connection / privilege | Automated coverage | Manual status | Known limitations |
|---|---|---|---|---|---|---|
| Results grid | Release surface | Results output tab | materialised result is local | Results unit/component, Integration typed values and large data, Desktop paging | empty state and layout passed | sort/filter/search apply to the displayed bounded page |
| Multiple result sets | Release surface | nested result tabs | query privilege | Results and Integration | not manually executed | none within configured storage limits |
| Copy and export | Release surface | result toolbar/context menu | local result | Results escaping, atomic/cancel/export tests | command gating passed | export contains retained rows, not rows omitted by storage bounds |
| Import | Limited | Database > Import Data | destination write privilege | Core validation/readers, Integration PostgreSQL transfer | not manually exercised | compact dialog; no full multi-step wizard |
| Data editing | Not supported | no release UI entry | — | none | not applicable | editable grid, row-identity/concurrency plans, conflict UI, and close prompt are absent |
| Object scripting | Service only | generated by object/application services | metadata visibility | Core quoting/script tests, live hostile identifiers | not applicable | Object Explorer context-menu entry points are absent |
| Object creation/modification | Service only | SQL editor is the supported route | DDL privilege | generated SQL and metadata tests | not applicable | no dedicated designers |
| Role and permission management | Limited | Database > Security Roles | administrative privilege | Core and restricted-role Integration | not manually exercised | read/list presentation is compact; broad role designer is absent |
| Backup | Release surface | Database > Backup | database access plus local utility | Core process safety, live pg_dump, Desktop routing | not manually run | no persistent operation history |
| Restore | Release surface | Database > Restore | create/write privilege and exact-target confirmation | Core hardening and live custom/plain restore | not manually run | service cancellation is covered; native long-running cancellation was not repeated |
| Maintenance | Limited | Database > Maintenance | owner/administrative privilege | Core SQL/guard tests | not manually run | compact action dialog |
| Activity monitor | Limited | View > Activity Monitor | `pg_stat_activity` visibility | Core workspace/presentation and live Integration | command gating passed | one-shot compact view; dedicated polling workspace and action UI are absent |
| Session cancellation/termination | Service only | no release UI action | `pg_signal_backend` or superuser | Core safety and live backend termination tests | not applicable | destructive session-action UI is deliberately absent |

## Analysis, settings, security, and diagnostics

| Feature | Status | Primary / alternative entry points | Connection / privilege | Automated coverage | Manual status | Known limitations |
|---|---|---|---|---|---|---|
| Schema comparison/synchronisation | Service only | no release command | metadata on two explicit endpoints | Core extractor/planner tests | not applicable | extractor/workspace does not yet qualify as faithful end-user schema compare |
| Query performance store/dashboard/history | Service only | no release command | `pg_stat_statements` where available | Core domain/workspace tests | not applicable | production collector, persistence, and WPF workspace are absent |
| Query execution history | Service only | no release command | none | Core fingerprint/retention/export tests | not applicable | settings exist, but execution recording, persistent repository, and history UI are absent |
| Plan comparison/regression | Service only | no release command | none for imported plans | Core tests | not applicable | no side-by-side WPF workspace |
| Index analysis/recommendations | Service only | no release command | catalog/statistics visibility | Core recommendation/workspace tests | not applicable | production collector and WPF workspace are absent |
| Application settings | Limited | startup settings store; Options disabled | none | Core persistence/migration/corruption tests, Desktop composition | not manually changed | no validated Options UI; secure defaults apply from stored configuration |
| Read-only and production safeguards | Release surface | connection profile classification and shared confirmation guard | profile policy | Core, Postgres, live restricted-role Integration, Desktop command state | disabled commands observed | requires accurate user classification of the profile |
| Credential storage and redaction | Release surface | connection dialog and Windows credential store | current Windows user | Core/Postgres/Desktop/Integration security suites | no authentication UI automation | certificate-auth environment not available in Sprint 44 |
| Logging and diagnostics | Limited | trace diagnostics / Help > Diagnostics | none | Core observer/redaction tests | diagnostics command present | no log viewer or support-bundle UI |
| Theme | Not supported | none | none | none | light theme observed | dark theme is not advertised |
| Multi-window | Not supported | none | none | none | not applicable | one main application window |

## Reconciliation conclusion

The production release surface is smaller than the sum of the service/domain
foundations created in Sprints 20–33. Those services remain valid tested
building blocks, but schema compare, query-performance workspaces, plan
comparison, index analysis, editable data grids, and destructive session
actions must not be described as complete desktop features. Sprint 44 makes
that distinction explicit and tests the workflows that are actually reachable.
