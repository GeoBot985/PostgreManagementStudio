# Sprint 49 — UI Reachability Audit and Release-Scope Reset

## Outcome

Sprint 49 is complete as an audit and planning sprint. The current source and
running desktop host now have a strict feature-to-UI traceability record. The
audit found 28 significant capability areas and awarded `RELEASE_QUALITY` to
none of them. Eight primary workflows are end-to-end reachable, but they are
not all release-quality; the remaining capabilities are service-only,
temporary, partial, or not implemented.

This is an honest reset of product scope, not a claim of broad PostgreSQL
administration completeness.

## Repository state inspected

- Current branch: `master`.
- Starting revision: Sprint 48 release-candidate hardening commit.
- Inspected the Core, Application, Results, Postgres and Desktop projects;
  production dependency registration; routed shell commands; WPF menus,
  toolbars, context menus, dialogs, editor, result tabs and tree; tests; prior
  sprint reports; release qualification reports; and current release notes.
- The pre-existing untracked `STATE_OF_THE_NATION.md` was not changed or
  staged.

## Application build and run status

- Release solution build: passed with 0 warnings and 0 errors.
- Desktop host launched from
  `src/PostgreManagementStudio.Desktop/bin/Release/net9.0-windows/PostgreManagementStudio.Desktop.exe`.
- Live inspection confirmed the traditional menu-bar-first shell, Standard and
  Query toolbars, status bar, Object Explorer tree, SQL editor, result tabs,
  routed disabled states, and the File > Connect PostgreSQL dialog.
- No credentials or connection strings were entered during the live audit.
- The first launch attempt exposed a stale RC47 process; it was closed and the
  current Release executable was launched directly and verified by process
  executable path before inspection.

## Audit methodology

1. Inventory significant services, domain models, handlers, commands, views,
   dialogs, tests and historical claims.
2. Trace each capability to a discoverable desktop entry point and exact user
   navigation path.
3. Run the current WPF host and inspect the actual shell, command enablement,
   connection dialog and output surfaces.
4. Classify each capability using the six exact UI-state definitions from the
   sprint brief.
5. Record workflow gaps, orphaned services, temporary UI, misleading controls
   and duplicate composition.
6. Reset current-release scope and sequence future composition work.

## Inventory counts

| UI state | Count |
|---|---:|
| NOT_IMPLEMENTED | 1 |
| SERVICE_ONLY | 9 |
| DIAGNOSTIC_OR_TEMPORARY_UI | 5 |
| PARTIALLY_REACHABLE | 5 |
| END_TO_END_REACHABLE | 8 |
| RELEASE_QUALITY | 0 |
| **Total** | **28** |

## Proven release-quality features

None. The release-quality bar requires a complete, discoverable, consistent,
tested, recoverable workflow. The current compact editor/query surface has
several end-to-end workflows, but no feature has satisfied that full bar.

## Service-only capabilities

Transactions, query history, session cancellation/termination, query
performance monitoring/history, index analysis, schema comparison,
synchronisation preview, settings/options, and SQL IntelliSense have no
credible completed desktop route. These are explicitly listed in the matrix;
they are not current-release features.

## Temporary or partial UI

Database object search, maintenance, role metadata, activity monitoring and
diagnostics are one-shot/raw or provisional surfaces. Query plans, backup,
restore, data transfer and layout persistence have real routes but remain
partial. The matrix records the exact blocking composition gaps.

## Orphaned commands and services

- Service registrations exist for activity/session management, performance
  history/dashboard, index analysis, schema comparison/synchronisation and
  several administrative services without corresponding shell commands.
- Completion requests are coordinated in `QueryTabView`, but no visible
  completion list is composed into the editor.
- `RecentFilesService` and settings persistence exist while Recent Files and
  Options are deliberately disabled.

## Misleading or dead UI found

The disabled controls are not silently dead: Recent Files, Change Database,
Transaction Options and Options each have explanatory tooltips. They are still
scope signals that must not be mistaken for working features. The more serious
misleading surfaces are the raw Messages-based object search, activity,
security and maintenance actions, and the fixed one-shot import/maintenance
flows whose labels imply broader workspaces than they provide.

## Duplicate composition

`ProductionServices` registers the Npgsql activity, security, maintenance,
transfer, plan, search and backup services, while `QueryTabView` directly
constructs several of those services in one-shot handlers. This bypasses the
shared dependency graph and makes later workflow composition inconsistent. It
is recorded for a future composition sprint; no broad refactor was made here.

## Small fixes applied

No code fixes were necessary or safe to infer from the audit. Existing disabled
controls have accurate explanatory wording, command routing is shared across
the current menu/toolbar/editor surfaces, and the current shell is already
truthfully compact. Sprint 49 made documentation-only changes so the audit
would not accidentally turn into a feature implementation sprint.

## Files changed

- `docs/audits/UI_REACHABILITY_MATRIX.md`
- `docs/audits/FEATURE_CLAIM_DISCREPANCIES.md`
- `docs/audits/ui-reachability-evidence/README.md`
- `docs/planning/RELEASE_SCOPE_RESET.md`
- `docs/planning/UI_COMPOSITION_BACKLOG.md`
- `docs/architecture/DESKTOP_FEATURE_COMPOSITION_STANDARD.md`
- `docs/sprints/SPRINT_49_REPORT.md`

## Tests and validation

- `dotnet build PostgreManagementStudio.sln --configuration Release --no-restore`: passed, 0 warnings/errors.
- `dotnet test PostgreManagementStudio.sln --configuration Release --no-build`: 325 passed, 0 failed, 60 skipped by environment gating.
- `dotnet format analyzers --verify-no-changes --no-restore --verbosity quiet`: passed.
- `git diff --check`: passed.
- Live desktop host: launched and inspected; connection dialog route and shell
  command states were verified without credentials.
- Prior Sprint 48 live PostgreSQL qualification remains the relevant connected
  evidence: 1,152 passed, 0 failed, cleanup passed.

## Release-scope decisions

Keep the current release focused on connection/session setup, Object Explorer
browsing, SQL editing/files, execution/cancellation, results/export, recovery
and bounded diagnostics. Complete backup/restore, plan analysis, object search,
activity monitoring and data transfer before advertising them as finished.
Defer transaction, security, maintenance, performance, index, schema,
IntelliSense, settings and history work. Remove object scripting from current
claims until it has an actual implementation and workflow.

## Recommended Sprint 50 scope

The evidence-based recommendation is **Restore Workspace and Release-Shell
Persistence**: compose a durable backup/restore workspace, then close the
settings/layout persistence gaps that affect target identity, progress,
failure reconciliation and restart behaviour. A smaller alternative is to
split restore composition from shell persistence, but both should precede new
administration features because they address data-safety risk directly.

## Known uncertainties

- The connected evidence is strongest for the previously qualified local
  PostgreSQL 18.4 environment, not all PostgreSQL versions or remote TLS modes.
- Some WPF workflows require a live connection for full action-state testing;
  this audit intentionally did not enter credentials in the UI.
- Screenshots were inspected live but not retained; reproducible navigation and
  source/test evidence are recorded instead.
- Historical sprint terminology is not uniform; the discrepancy document
  provides the corrected current descriptions.

## Remaining product-completion risks

The principal risk is composition debt: many strong backend services remain
unreachable or are exposed as one-shot text handlers. The next implementation
sprints must choose one durable workflow at a time, preserve the menu-bar-first
shell, and apply the desktop composition standard before claiming completion.

## Definition of Done

The solution builds, the current desktop host was run and inspected, all 28
significant features appear in the matrix, every entry has a strict state,
reachable routes have reproducible evidence, service-only and temporary UI are
explicit, historical claim discrepancies are recorded, scope is reset, the
composition standard and sequenced backlog exist, and Sprint 50 has an
evidence-based recommendation. No major backend capability is described as a
complete desktop feature merely because tests pass.
