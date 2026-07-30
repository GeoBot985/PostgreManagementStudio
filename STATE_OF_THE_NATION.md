# State of the Nation

## Executive decision

**Approved with blocking conditions**

PostgreManagementStudio `0.9.0-rc.3` is a coherent, buildable, source-identifiable
Windows desktop candidate with a reliable Object Explorer, safe tree scripting,
strong Alt+F1 metadata, query execution, and valid result export. It is **not yet
eligible for final release qualification** because direct packaged-application
testing found critical failures in two of the four daily workflows:

1. the simple `SELECT * FROM schema.table` editor action did not replace the
   wildcard even after the table was successfully described; and
2. the import wizard failed a valid existing-table import containing `jsonb`
   with `XX000: unsupported jsonb version number 123`.

The import failure rolled back atomically: the destination contained zero rows
after the failure. No silent corruption, unsafe DML, crash, credential leakage,
or false success summary was observed. The package is tied to the tested source
revision, but two consecutive package builds produced different ZIP hashes.
Clean-machine execution without a development SDK, complete manual transfer
round trips, and every destructive/error-recovery branch were not completed and
remain explicit release conditions.

The next sprint must be **Sprint 62 — Core Workflow Defect Repair and Release
Qualification Closure**, with the exact scope listed below.

## Candidate identification

| Field | Recorded value |
| --- | --- |
| Repository | `D:\Projects\CURRENT\PostgreManagementStudio` |
| Branch | `master` |
| Tested source revision | `424133bff9684c962b93a71feab3ebdc49da46bd` |
| Initial working tree | Clean except pre-existing untracked `STATE_OF_THE_NATION.md` |
| Audit modifications | Report and evidence under `docs/audits/sprint-61`; no product source changed |
| Application version | `0.9.0-rc.3` |
| Configuration | `Release` |
| Target framework | `net9.0-windows` desktop; `net9.0` libraries |
| Runtime identifier | `win-x64` |
| Runtime mode | Self-contained |
| Package | `PostgreManagementStudio-0.9.0-rc.3-win-x64.zip` |
| Rebuilt package size | 62,374,510 bytes |
| Rebuilt package SHA-256 | `44ebdd10983468c9091a10cfa84907eedd5c334a6bd3ec4e8951ca5dded790d6` |
| First package SHA-256 | `8179e145d014fe5bc872a9def541d1d411f314283c259b6cdb861010238f3c61` |
| Manifest source revision | `424133bff9684c962b93a71feab3ebdc49da46bd` |
| Signing | Unsigned internal candidate |

The release manifest reports `dirtyWorkingTree: false` because the package was
created from the recorded revision before the audit evidence files were added.
The manifest, package inventory, and per-file hashes establish the tested
source/package identity. The differing archive hashes are defect `S61-M02`.

## Environment

| Component | Value |
| --- | --- |
| OS | Microsoft Windows 11 Pro, 64-bit, version `10.0.26200`, build `26200` |
| Current .NET SDK | `10.0.302` |
| Additional SDK | `9.0.315` |
| .NET runtime used by target | Microsoft.NETCore.App / WindowsDesktop.App `9.0.17` |
| PostgreSQL server | PostgreSQL `18.4`, x86_64-windows |
| `psql` | `18.4` |
| `pg_dump` | `18.4` |
| `pg_restore` | `18.4` |
| Database | Local disposable test objects in database `postgres` |
| Culture/time zone | Windows host, Africa/Johannesburg |

The host has development SDKs installed. This does not satisfy the required
clean-machine test despite the package being self-contained.

## Build and package validation

`scripts/release/build-release.ps1` completed restore, clean, Release build,
automated tests, self-contained publish, package inventory, manifest, checksums,
and ZIP creation in 49.3 seconds. Build output contained zero warnings and zero
errors. `scripts/release/verify-package.ps1` validated 407 packaged files.

| Check | Result | Evidence |
| --- | --- | --- |
| Restore | Pass | Release build pipeline |
| Clean | Pass | Release build pipeline |
| Build | Pass, 0 warnings/errors | Release build pipeline |
| Automated tests in packaging pipeline | Pass | Manifest `testStatus` |
| Publish | Pass | `stage-0.9.0-rc.3/app` |
| Self-contained runtime | Pass | Runtime files and manifest |
| Package verification | Pass, 407 files | Package verification script |
| Source identity | Pass | Manifest revision equals tested `HEAD` |
| Package recreation | Pass | Two complete builds produced same file count and size |
| Byte-for-byte reproducibility | **Fail** | ZIP hashes differ between consecutive builds |
| Signing | Condition | Candidate is unsigned |

The package currently present under `artifacts/release` has the rebuilt hash
recorded above. Build artifacts and `TestResults` are intentionally ignored and
are summarized in committed evidence.

## Automated test results

The complete Release suite was run twice against a disposable PostgreSQL
environment with large-dataset gates enabled:

| Iteration | Project | Passed | Failed | Skipped | Duration |
| ---: | --- | ---: | ---: | ---: | ---: |
| 1 | Core | 232 | 0 | 0 | 1.48 s |
| 1 | Results | 65 | 0 | 0 | 1.31 s |
| 1 | Postgres | 54 | 0 | 0 | 1.32 s |
| 1 | Desktop | 31 | 0 | 0 | 4.34 s |
| 1 | Integration | 71 | 0 | 0 | 29.35 s |
| 2 | Core | 232 | 0 | 0 | 1.09 s |
| 2 | Results | 65 | 0 | 0 | 0.90 s |
| 2 | Postgres | 54 | 0 | 0 | 0.84 s |
| 2 | Desktop | 31 | 0 | 0 | 3.16 s |
| 2 | Integration | 71 | 0 | 0 | 27.39 s |
| **Total** | **All projects** | **906** | **0** | **0** | **70.88 s harness elapsed** |

Run ID: `2f853cd4c5`. Database and role cleanup succeeded. There were no
intermittent, order-dependent, port-conflict, or state-leak failures across the
two runs. The test suites are unit/component, WPF composition, PostgreSQL
integration, release packaging, and manual packaged-app checks; there is no
full UI automation suite. The final packaged walkthrough therefore remains
essential and found defects not exposed by service-level tests.

## Core experience scorecard

| Experience | Completeness | Reliability | Predictability | Performance | Keyboard use | Release status |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Object Explorer | 92 | 91 | 90 | 89 | 88 | Conditional pass |
| Tree scripting | 90 | 92 | 91 | 90 | 90 | Conditional pass |
| Alt+F1 describe | 89 | 82 | 83 | 91 | 91 | Blocked by wildcard UI defect |
| Import/export | 79 | 62 | 70 | 82 | 82 | Blocked by valid `jsonb` import failure |

Scores are not averaged into an approval. Import/export and Alt+F1 are not
release-ready while their critical defects remain open.

### Wider product scorecard

The established release-candidate quality target is 85/100.

| Category | Score | Target met | Discussion |
| --- | ---: | ---: | --- |
| Installation and startup | 82 | No | Packaged startup passes; clean SDK-free VM and signing remain open |
| Connection management | 91 | Yes | Live connect and repeated integration recovery pass |
| Query editing | 86 | Yes | Daily editor works; wildcard UI action fails |
| Query execution | 93 | Yes | Live typed query and cancellation/recovery integration pass |
| Result handling | 91 | Yes | Retained-scope disclosure and independent JSONL parse pass |
| Object Explorer | 91 | Yes | 304-table schema remained responsive |
| Administration tools | 87 | Yes | Regression suites green; not every UI branch repeated manually |
| Backup and restore | 89 | Yes | Hardening integration and package utility checks pass |
| Monitoring | 88 | Yes | Production adapter regression coverage passes |
| Settings | 87 | Yes | Initialization/recovery pass; clean-user VM still required |
| Error recovery | 82 | No | Import rollback passes; full deliberate failure matrix incomplete |
| Performance | 86 | Yes | Bounded tests pass; complete per-format manual profiling incomplete |
| Accessibility | 82 | No | Keyboard paths work in sampled flows; screen-reader audit incomplete |
| Security | 91 | Yes | No vulnerable resolved packages or secret leakage found |
| Documentation | 88 | Yes | Feature guides and audit evidence present |
| Packaging | 78 | No | Non-deterministic ZIP and unsigned candidate |

## Sprint 58 reconciliation

Criterion-by-criterion results are in
[`docs/audits/sprint-61/SPRINT_58_RECONCILIATION.md`](docs/audits/sprint-61/SPRINT_58_RECONCILIATION.md).

The packaged app exposed the expected node-specific context menu via mouse and
`Shift+F10`. CREATE, DROP, DROP-and-CREATE, SELECT, INSERT, UPDATE, DELETE,
Properties, Refresh, Rename, Delete, Copy Name, and Copy Qualified Name were
visible with the selected `s61_audit.orders` node. Generated SELECT used 10
quoted, physically ordered columns and `LIMIT 10000`; it returned the two source
rows. Automated replay tests validate ordinary/partitioned tables, views,
materialized views, sequences, routines and overloads, procedures, indexes,
triggers, enums, domains, composites, identity, generated columns, defaults,
constraints, and comments. UPDATE and DELETE generation require a key-shaped
predicate and never emit unconditional DML.

Destructive execution was not performed during this audit because it requires
an action-time user confirmation. Confirmation/gating behavior is covered by
Desktop tests and prior evidence, but this remains a manual qualification
condition rather than an inferred pass.

## Sprint 59 reconciliation

Criterion-by-criterion results are in
[`docs/audits/sprint-61/SPRINT_59_RECONCILIATION.md`](docs/audits/sprint-61/SPRINT_59_RECONCILIATION.md).

The packaged Alt+F1 command resolved a caret adjacent to the semicolon only
after the caret moved directly adjacent to the table identifier. Resolution
took approximately 902 ms and returned catalogue-ordered identity, generated,
default, PK/FK/unique, domain, enum, array, JSON, index, trigger, and comment
metadata. Resolver and integration tests cover selected/caret precedence,
qualification, quoting, Unicode, aliases, CTEs, search path, overloads,
ambiguity, stale identity, presets, all copy formats, and bounded edits.

The packaged `Replace *` action failed on the canonical simple statement and
left SQL unchanged. This is `S61-C01`; service-level wildcard tests passing does
not override the end-to-end failure.

## Sprint 60 reconciliation

Criterion-by-criterion results are in
[`docs/audits/sprint-61/SPRINT_60_RECONCILIATION.md`](docs/audits/sprint-61/SPRINT_60_RECONCILIATION.md).

Packaged result export correctly declared a complete retained source of two
rows, exposed column selection/reordering, wrote JSON Lines via an atomic
temporary destination, and reported two rows, ten columns, 529 bytes. An
independent PowerShell JSON parser read exactly two valid records. SHA-256:
`6a6ceaa9b7ddeeb4ccee09f3b34e42d02a1da405204487b2b6934cac6ec395ea`.

The import wizard detected UTF-8, comma delimiter, header, explicit `\N`, and
multiline quoting; previewed three logical records; selected the existing
table; excluded identity/generated columns; mapped all 13 writable typed
columns; and reviewed atomic COPY/stop-on-first-error. It nevertheless failed
valid JSONB data with an unsupported binary version error. A PostgreSQL
catalogue query confirmed zero committed rows. New-table UI import and complete
manual round-trip matrices were not re-executed after this blocker; their
service/integration tests pass but they remain release conditions.

## End-to-end workflow results

| # | Packaged Release workflow step | Result | Evidence / limitation |
| ---: | --- | --- | --- |
| 1 | Launch packaged application | Pass | Exact staged executable launched |
| 2 | Open connection | Pass | Local PostgreSQL profile/environment target |
| 3 | Connect | Pass | Status showed `postgres@localhost:5432` |
| 4 | Expand database | Pass | Schemas loaded lazily |
| 5 | Navigate schemas/tables | Pass | `s61_audit` and 304 tables navigated |
| 6 | Refresh hierarchy | Partial | Refresh command reached; full timed refresh not captured |
| 7 | Open table properties | Covered/partial | Command reachable; replay evidence primarily automated |
| 8 | Generate CREATE script | Covered/partial | Menu and replay suite pass; sample not retained from this run |
| 9 | Generate explicit SELECT | Pass | Quoted ten-column script with limit |
| 10 | Open query editor | Pass | Connected unsaved tab |
| 11 | Execute generated SELECT | Pass | Two rows, typed data |
| 12 | Alt+F1 table | Pass | Catalogue-backed description in ~902 ms |
| 13 | Copy ordered column list | Covered/partial | Formatter tests/prior screenshot; not independently pasted this run |
| 14 | Replace `*` | **Fail** | `S61-C01`: no matching wildcard reported |
| 15 | Execute modified query | Blocked | Step 14 failed |
| 16 | Export results to CSV | Covered/partial | CSV tests pass; this run used JSONL |
| 17 | Export table to JSON Lines | Partial/pass | Result JSONL passed; direct table path covered by integration |
| 18 | Import CSV into existing table | **Fail safely** | `S61-C02`; zero rows committed |
| 19 | Import into new table | Not run | Blocked after valid existing-table failure |
| 20 | Verify imported values | Not run | No successful rows |
| 21 | Generate DROP script | Reachability pass | Menu present; automated replay passes |
| 22 | Confirmed tree deletion | Not run | Action-time destructive confirmation not granted |
| 23 | Refresh after delete | Not run | Step 22 not performed |
| 24 | Disconnect cleanly | Partial | Connection remained usable after import error; explicit disconnect not repeated |
| 25 | Close without unhandled error | Partial | Close produced expected unsaved-query prompt; cancellation preserved data |

The scenario was genuinely run, but it was not fully successful. Failed and
unexecuted steps are release conditions and are not represented as passes.

## Object Explorer assessment

The tree was usable immediately after connection. Expanding the audit schema
took approximately 1.14 seconds. Expanding the 304-object Tables folder took
approximately 2.23 seconds. The UI remained responsive and correctly targeted
the selected `orders` node for keyboard context actions. Naming, schema/folder
grouping, collapse/selection, lazy loading, and keyboard context menus were
predictable in the sampled flow.

Integration tests cover one bounded deterministic catalogue batch, stale
database identity rejection, dropped/renamed OID identity, cancellation,
missing database handling, permission filtering, and large-schema loading.
The audit did not separately time a complete selected-node refresh or force a
connection drop while the tree was loading.

## Scripting assessment

Scripting is the strongest of the four core experiences. Structured catalogue
metadata is separated from rendering and WPF. Identifiers are quoted, scripts
are database-bound, read-only/permission state gates mutations, and generated
scripts open without automatic execution.

The observed SELECT was explicit and safe. Unit tests prove INSERT excludes
defaults/generated/identity-always columns; UPDATE and DELETE use primary-key
conditions or safe placeholders; DROP is qualified, terminated, and never adds
`CASCADE`. PostgreSQL integration replay covers the required object families
and compares catalogue effects. Deferred fidelity limits—ownership/grants,
identity sequence options, row-security policies—are documented and must not be
misrepresented as complete reconstruction.

## Alt+F1 assessment

The description surface is fast, useful, and catalogue-truthful. It stages
identity/columns before secondary definitions, does not read user rows, and
rejects ambiguous search-path candidates instead of arbitrarily choosing.
Presets and formatting are deterministic in unit tests.

Resolution matrix detail is in the Sprint 59 reconciliation. The principal
release issue is UI composition: the same described object that populated the
column grid did not permit the canonical `SELECT *` replacement. The command
failed safely without changing unrelated SQL, but habitual SSMS-style use is
broken.

## Import assessment

The parser, preview, inference, metadata, mapping, transaction, rejection, and
streaming services have broad automated coverage. The adversarial fixture
contains empty/duplicate/reserved headers, leading zeros, malformed records,
invalid conversions, Unicode, quotes, delimiters, and multiline text.

The packaged existing-table path is not production-ready. A first deliberately
invalid attempt rolled back after an OID mismatch, but exposed only a driver
error rather than a useful source-row/column conversion error (`S61-H01`). The
corrected fixture then failed valid JSONB binary COPY (`S61-C02`). The review
also estimated five rows for a file containing three logical multiline records
(`S61-M01`). No rows were committed after either atomic failure.

Because the valid import failed, the audit cannot claim empty/NULL fidelity,
successful new-table inference, transaction-mode summaries, rejected-row
output, or large-import behavior as packaged end-to-end passes even though
their service/integration tests are green.

## Export assessment

The directly tested JSON Lines result export is valid, complete, and
independently parsed. The wizard accurately distinguished retained result rows
from server completeness and used an atomic temporary-file workflow.

Automated tests cover CSV, TSV, JSON array, JSON Lines, SQL INSERT, PostgreSQL
literals, selected/reordered/renamed result columns, truncation warnings,
relation streaming, cancellation, and non-replacement of final output on
failure. This audit did not independently replay every format, perform CSV/TSV
round trips, profile every large streaming format, or test disk-full/unwritable
destinations manually; these remain qualification conditions.

## Regression assessment

All 906 automated executions passed. The suite covers startup/composition,
connections and recovery, query execution/cancellation/notices, result storage,
transactions, plans, history-adjacent services, backup/restore, maintenance,
monitoring, activity, search, permissions, object metadata, menus, transfer
services, and packaging.

Packaged manual regression confirmed startup, connection, Object Explorer,
keyboard context menu, query tabs, execution, result grid, Alt+F1, export,
controlled import failure, connection survival, and unsaved-close prompting.
It did not repeat every administrative dialog or multi-server workflow.

## Performance observations

| Observation | Result |
| --- | --- |
| Schema expansion | ~1.14 s |
| 304-table folder expansion | ~2.23 s |
| Alt+F1 description | ~0.90 s |
| Two-row JSONL export | ~0.014 s reported |
| Full two-pass test harness | 70.88 s |
| Integration iteration 1 / 2 | 29.35 s / 27.39 s |

The 304-table UI remained responsive. Automated performance tests cover lazy
large-schema loading, bounded million-row result retention, repeated connection
stability, and cancellation. A complete manual peak-memory/throughput matrix
for every import/export format was not measured and is a release condition.

## Accessibility assessment

Keyboard access was confirmed for menu navigation, `Shift+F10`, Alt+F1,
description-grid actions, wizard navigation, and the unsaved-query prompt.
Focus was visible and wizard warnings were textual rather than color-only.

The audit did not complete a screen-reader inspection, all-dialog tab-order
walkthrough, or destructive confirmation without mouse. The wildcard command
has a keyboard path but its core action fails. Accessibility therefore remains
below the release target until a focused pass follows the fixes.

## Security and logging assessment

No password or supplied local test credential was found in repository files,
the connection profile store, or recovery files. The packaged UI displayed a
sanitized target identity and never displayed the password. Authentication and
connection-failure integration tests are secret-safe. Static review found no
hard-coded production connection strings or test credentials.

`dotnet list package --vulnerable --include-transitive` reported no known
vulnerable resolved package from the configured NuGet source. Direct production
dependencies are Npgsql `8.0.6`, Microsoft.Extensions.DependencyInjection
`9.0.0`, and Microsoft.Extensions.Logging.Abstractions `9.0.0`. Test
dependencies include xUnit `2.9.2`, runner `2.8.2`, Test SDK `17.12.0`, and
coverlet `6.0.2`; xUnit v2 is reported as legacy and is acceptable test debt.
The project owner must complete final licence/notices review before public
distribution.

No application log directory or emitted log file was present for this run.
Local profile/recovery data contained no credential pattern, exception dump, or
connection-string leakage. The absence of durable logs limits diagnostics for
the import failures and is known observability debt.

## Architecture assessment

Responsibilities are appropriately separated:

- WPF composes commands and state; catalogue SQL remains in PostgreSQL adapters.
- Script generation is independent of Object Explorer controls.
- editor resolution/formatting is independent of presentation.
- import parsing/inference is separate from PostgreSQL execution.
- export formatting is separate from retrieval.
- cancellation flows through metadata, transfer, query, and export services.
- quoting and structured metadata contracts are shared.
- file and database resources use deterministic disposal and atomic destinations.

Static review found no `TODO`, `FIXME`, `HACK`, `NotImplementedException`,
temporary feature flags, debug-only branches, hard-coded local paths, SQL
Server syntax, or placeholder commands. Apparent `.Result` was a domain
property; `Gate.Wait(0)` is an intentional non-blocking probe. Empty catches
are primarily best-effort cleanup/cancellation. Swallowed recent-file
persistence errors and absent durable transfer diagnostics are acceptable
known debt for this candidate but should gain structured logging.

The packaged-import defect indicates a gap between UI-selected COPY strategy,
conversion/type binding, and the integration-test path. Sprint 62 must add a
packaged/composed regression test at that seam.

## Defects

Full records are in
[`docs/audits/sprint-61/DEFECT_REGISTER.md`](docs/audits/sprint-61/DEFECT_REGISTER.md).

| ID | Severity | Title | Release impact |
| --- | --- | --- | --- |
| S61-C01 | Critical | Packaged simple `SELECT *` replacement cannot find wildcard | Blocks Alt+F1 daily workflow |
| S61-C02 | Critical | Packaged valid JSONB existing-table import fails in binary COPY | Blocks import daily workflow |
| S61-H01 | High | Import conversion/COPY failure lacks source row and destination column | Unacceptable diagnostics; workaround is manual file isolation |
| S61-M01 | Medium | Multiline CSV review estimates physical lines, not logical records | Misleading estimate; final counts still authoritative |
| S61-M02 | Medium | Rebuilt ZIP is not byte-for-byte reproducible | Hash cannot be predicted from identical source |
| S61-RC01 | Release condition | SDK-free clean-machine validation not executed | Prevents unconditional approval |
| S61-RC02 | Release condition | Candidate is unsigned | Must sign or explicitly approve internal-only distribution |
| S61-RC03 | Release condition | Full manual transfer/failure/accessibility matrices incomplete | Must close evidence gaps after fixes |

## Release blockers

The following must be closed before final release qualification:

1. Fix `S61-C01` and prove simple, alias, and multi-alias wildcard replacement
   plus one-step undo/redo in the packaged application.
2. Fix `S61-C02` and prove valid JSONB, enum, domain, arrays, identity/generated,
   NULL, empty string, multiline, Unicode, timestamp, UUID, and numeric import
   from the packaged wizard.
3. Add source-row/destination-column diagnostics for conversion/COPY failures.
4. Re-run the complete 25-step packaged workflow without a failed or blocked
   core step.
5. Execute SDK-free Windows 11 x64 package validation.
6. Complete independent CSV/TSV round trips, JSON parsing, SQL replay, large
   transfer measurements, transaction/cancellation modes, rejected rows, and
   result-scope variants.
7. Decide deterministic-package policy; make archives reproducible or document
   and control the build timestamp/source of nondeterminism.
8. Resolve signing for public distribution.

## Conditions for final release qualification

Final qualification requires zero open Blocker/Critical defects, no unsafe DML
or silent transfer mismatch, two clean full-suite repetitions after fixes,
package/source identity, package verification, clean-machine smoke, complete
manual daily workflow, and explicit disposition of every High defect. All
temporary test objects and files must be removed, and logs/profile stores must
remain credential-free.

## Deferred work

The following may remain deferred if accurately documented:

- ownership/grant, row-security policy, and identity-sequence-option fidelity;
- richer editable properties;
- perfect nested-scope CTE projection inference;
- guaranteed temporary-table backend affinity;
- Excel/JSON import and specialized PostgreSQL type editors;
- disjoint result selection and visual relation-filter builders;
- xUnit v3 migration;
- richer structured diagnostic logging, provided critical transfer errors gain
  useful user-facing detail now.

## Recommended next sprint

### Sprint 62 — Core Workflow Defect Repair and Release Qualification Closure

Sprint 62 must contain only:

1. reproduce and repair the `QueryTabView`/description-state wildcard command
   binding for simple, aliased, quoted, Unicode, joined, incomplete, and
   ambiguous SQL;
2. repair import COPY/type binding for JSONB and all supported complex types,
   with automatic fallback to validated typed inserts where binary COPY cannot
   safely represent the reviewed mapping;
3. attach source logical row, source value policy, destination column/type, and
   safe PostgreSQL diagnostics to transfer failures;
4. correct multiline logical-row estimation;
5. add composed desktop and PostgreSQL regressions that exercise the exact
   wizard strategy selected by the packaged UI;
6. make release ZIP output deterministic or define a controlled
   non-reproducible-build policy with signed manifest provenance;
7. repeat all automated suites twice and rerun the complete packaged workflow;
8. complete clean-machine, signing, accessibility, failure-recovery, round-trip,
   SQL replay, rejected-row, and large-transfer qualification evidence;
9. update this report with one final decision and no unresolved critical
   uncertainty.

No unrelated features, visual redesign, new import formats, dashboards, or
cloud/AI work belong in Sprint 62.

## Evidence index

| Evidence | Location / identifier |
| --- | --- |
| Candidate, environment, build, package, test record | [`CANDIDATE_EVIDENCE.md`](docs/audits/sprint-61/CANDIDATE_EVIDENCE.md) |
| Sprint 58 matrix | [`SPRINT_58_RECONCILIATION.md`](docs/audits/sprint-61/SPRINT_58_RECONCILIATION.md) |
| Sprint 59 matrix and resolution coverage | [`SPRINT_59_RECONCILIATION.md`](docs/audits/sprint-61/SPRINT_59_RECONCILIATION.md) |
| Sprint 60 matrix | [`SPRINT_60_RECONCILIATION.md`](docs/audits/sprint-61/SPRINT_60_RECONCILIATION.md) |
| Detailed defects | [`DEFECT_REGISTER.md`](docs/audits/sprint-61/DEFECT_REGISTER.md) |
| Generated SQL samples and replay mapping | [`GENERATED_SQL_SAMPLES.sql`](docs/audits/sprint-61/GENERATED_SQL_SAMPLES.sql) |
| Clean import fixture | [`import-existing.csv`](docs/audits/sprint-61/import-existing.csv) |
| Adversarial import fixture | [`import-adversarial.csv`](docs/audits/sprint-61/import-adversarial.csv) |
| Test run summary | `TestResults/2f853cd4c5/release-summary.json` (ignored build evidence; summarized above) |
| Release manifest | `artifacts/release/release-manifest.json` (ignored build evidence; summarized above) |
| Package checksums | `artifacts/release/checksums.sha256` (ignored build evidence; summarized above) |
| Sprint 59 UI screenshots | [`docs/screenshots/sprint-59`](docs/screenshots/sprint-59) |
| Sprint 60 UI screenshots | [`docs/screenshots/sprint-60`](docs/screenshots/sprint-60) |
| Sprint 58 implementation report | [`docs/sprints/SPRINT_58_REPORT.md`](docs/sprints/SPRINT_58_REPORT.md) |
| Sprint 59 implementation report | [`docs/sprints/SPRINT_59_REPORT.md`](docs/sprints/SPRINT_59_REPORT.md) |
| Sprint 60 implementation report | [`docs/sprints/SPRINT_60_REPORT.md`](docs/sprints/SPRINT_60_REPORT.md) |

Temporary schema `s61_audit` and its 314 contained test objects were dropped
after validation. No unrelated product source or user file was modified.
