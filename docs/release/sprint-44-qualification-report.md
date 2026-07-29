# Sprint 44 release-candidate qualification report

## Outcome

Sprint 44 stabilised the implemented release surface and produced a truthful
full-system baseline. The complete isolated Release suite passes with no
blocker or critical product defect left open. The application is suitable for
continued release-candidate preparation, but not for an unconditional final
release declaration until the PostgreSQL 14/remote/TLS compatibility campaign
and connected native exploratory pass are completed.

## Resolved defects

| ID | Severity | Reproduction / actual result | Expected result | Resolution and regression |
|---|---|---|---|---|
| S44-001 | High | Build a provider with an alternate settings path; connection profiles still target the real user `connections.json` | test/portable state must stay isolated | derive an isolated state directory; Desktop P0 composition regression |
| S44-002 | Critical | a startup exception containing a connection secret could be displayed through raw `ex.Message` | startup UI must redact credentials | startup presentation now uses `SecretRedactor`; existing redaction suite protects the implementation |
| S44-003 | Critical | edit unsaved SQL, terminate the process, restart; recovery service existed but the shell never wrote or restored it | unsaved text must survive abnormal restart without credentials or execution | atomic debounced snapshots, corruption-tolerant loading, disconnected startup restore, normal-close cleanup; Core and Desktop P0 regressions |
| S44-004 | High | switch active query tabs after Object Explorer loaded; the prior tree could remain visible and a late load could replace the active context | tree must follow the owning logical session/generation/database | active-context identity, refresh on tab switch, cancellation and late-result rejection |
| S44-005 | Medium | database administration commands were grouped under Tools and no Database top-level menu existed | traditional command placement and unique accelerator | canonical Database menu added; menu/accelerator UI regression updated |
| S44-006 | High | third iteration of the release suite measured process handles while unrelated integration tests ran in parallel and reported 532→570 despite deterministic cleanup | resource trend must measure the product lifecycle, not concurrent test-host noise | non-parallel resource-stability collection, explicit pool baseline/cleanup and five-cycle warm-up; three subsequent full iterations passed |

All fixes are targeted. No validation was weakened, no exception was
suppressed to make a test pass, and no unrelated feature was added.

## Defect triage

### Blocker

None open.

### Critical

None open. S44-002 and S44-003 were resolved with regression coverage.

### High

No reproducible product defect is open on the qualified PostgreSQL 18.4 local
configuration. S44-001, S44-004, and the S44-006 acceptance-test defect were
resolved.

### Medium

None open in the implemented release surface. Unsupported workspaces and
environment coverage gaps are classified as explicit limitations/risks rather
than defects in code that is presented as complete.

### Low

- The maximised 3840-wide fixed layout uses the available space but remains
  visually sparse. This is cosmetic and does not clip controls.
- Several compact dialogs predate a unified dialog-layout pass; no overlap or
  inaccessible control was reproduced.

## Manual exploratory report

Environment: Windows desktop, Release executable, native WPF, light theme,
restored 1100×768 window and maximised 3840-wide host display.

| Scenario | Result |
|---|---|
| Clean disconnected startup | Pass; one query tab, coherent empty Object Explorer/results states |
| Traditional menu placement | Pass; eight canonical top-level menus |
| Keyboard menu navigation | Pass; Alt+D opens Database |
| Shared command routing | Pass; Ctrl+N created `SQLQuery2.sql`; same command is bound to menu/toolbar |
| Disconnected command gating | Pass; execute, cancel, plans, metadata and database operations disabled |
| Accessibility names | Pass for shell, toolbars, status, connection/database, editor, output tabs and frequent actions |
| Restored and maximised layout | Pass; no overlap or clipping observed |
| Close/shutdown | Pass; process exited and no application window remained |

The automation detected concurrent user input once, discarded the stale input
state, and re-observed before continuing. No ambiguous action was retried.
Authentication UI was not automated, and no password was entered through
Computer Use.

Connected native click-through, high-DPI values other than the host setting,
monitor removal, destructive confirmations, file dialogs, and multi-hour soak
remain manual release checks. Their production boundaries are automated.

## Menu, UI, and accessibility findings

- File, Edit, View, Query, Database, Tools, Window, and Help use unique
  top-level accelerators.
- Database owns import, backup, restore, maintenance, and security. Tools owns
  object search; View owns Activity Monitor.
- Menus, toolbars, shortcuts, editor/results context actions, and tab close
  controls route through shared commands where the action has alternatives.
- The toolbar remains a frequent-action subset and does not mirror the menu.
- Destructive commands remain disabled while disconnected and pass through the
  shared exact-target confirmation guard when enabled.
- Essential controls expose automation names; environment/read-only identity
  is textual and does not rely on colour alone.
- Dark theme, free docking, multi-window, and persisted multi-monitor layout
  are not supported and are not represented as tested features.

## Multi-connection isolation

The suite runs ten editor executions concurrently and now includes two query
documents using different login roles. The read-only editor completed against
its own user/database while a long application-role execution was cancelled.
Execution contexts retained distinct profile IDs and usernames; cancellation
did not alter the completed editor. Object Explorer now keys visible state by
logical session, physical generation, and database and rejects late results
after a tab switch.

## Resource stability

| Measurement | Acceptance | Result |
|---|---:|---|
| Release startup/shutdown build | 0 warnings/errors | Pass |
| Main-window construction and clean shutdown | startup < 2 s, shutdown < 5 s P95 budgets | Pass in automated lifecycle |
| Large transfer | 1,000,000 received; retained/displayed bounded | Pass |
| Large schema | 1,000 tables plus views/functions/partitions | Pass |
| Repeated document lifecycle | 100 opens/disposals, collectible | Pass |
| Live connection lifecycle | 20 cycles, settled heap/handles bounded | Pass |
| Recurring health work | one owned timer, no overlap, stopped on close | Pass |
| Native process shutdown | no remaining application window | Pass |

These are deterministic/accelerated resource checks. A workstation-specific
multi-hour soak remains a release-campaign task.

## Deferred limitations and rationale

| Item | Risk | Rationale / required closure |
|---|---|---|
| PostgreSQL 14 minimum-version run | High qualification risk | execute the same release script against a disposable 14 endpoint |
| Docker and remote deployment | Medium qualification risk | no Docker engine or authorised remote endpoint is available |
| TLS and certificate authentication | High qualification risk | local PostgreSQL reports `ssl=off`; provision a dedicated secure endpoint |
| Connected native exploratory journey | Medium qualification risk | credentials were intentionally not automated; perform supervised manual click-through |
| Multi-hour soak and monitor-removal layout | Medium qualification risk | deterministic lifecycle checks pass; environment time/hardware campaign remains |
| Service-only Sprints 20–33 workspaces | Product-scope risk | compose and qualify each workspace before advertising it as a desktop feature |
| Options UI, dark theme, free docking, multi-window | Low/roadmap | not part of the current supported release surface |

No deferral covers a known data-loss, wrong-target, credential-exposure,
crash, or corruption defect.

## Release recommendation

**Conditional GO for final release-candidate preparation; NO-GO for final
release shipment.**

The local PostgreSQL 18.4 release surface has a clean 384-test regression
baseline, safe workspace recovery, correct session ownership, live
backup/restore, bounded large-data behaviour, and no unresolved blocker or
critical defect. Final shipment should remain gated on:

1. PostgreSQL 14 qualification;
2. remote TLS/password and certificate-auth qualification;
3. one connected native daily/multi-environment exploratory pass;
4. a multi-hour resource soak on the intended release workstation;
5. a product decision that service-only workspaces remain unadvertised or are
   separately composed and qualified.

## Verification record

- Release build: 0 warnings, 0 errors.
- Tests per iteration: Core 188, Results 63, Postgres 54, Desktop 19,
  Integration 60.
- Total: 384 tests × 3 iterations = 1,152 passed, 0 failed, 0 skipped.
- PostgreSQL: 18.4 x64 Windows.
- Large fixture: enabled.
- Database/role cleanup: passed.
- Native exploratory shell: passed for the documented scenarios.
- Machine-readable run: `TestResults/eb184b17b4/release-summary.json`.
