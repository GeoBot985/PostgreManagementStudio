# Feature-claim discrepancies

This file preserves historical sprint claims while correcting the current
product description. Historical reports are not rewritten.

| Original claim | Source document | Observed current state | Reason for discrepancy | Corrected description |
|---|---|---|---|---|
| Sprint 11 is “complete” and the WPF surface includes a security roles action | `docs/sprints/011-role-permission-management.md` | `Database > Security Roles` only loads and prints roles as raw text; create/edit/grant/revoke is absent | Backend/security SQL work was counted as feature completion | Role metadata is diagnostic/temporary UI; role management remains deferred |
| Sprint 14 provides a reliable data import/export capability | `docs/sprints/014-data-import-export.md` | Import is a reduced file picker plus table input with fixed public schema, header mapping and batch strategy; no export wizard | Transfer engine and parser coverage were counted as a complete desktop workflow | Import is partially reachable; a production wizard remains future work |
| Sprint 15 exposes database object search | `docs/sprints/015-database-object-search.md` | Search is reachable but writes raw results to Messages and cannot navigate/open an object | Service and one-shot handler were treated as a finished workspace | Search is diagnostic/temporary UI and needs a durable workspace |
| Sprint 17 includes execution-plan analysis and “editor actions” | `docs/sprints/017-query-execution-plan-analysis.md` | Estimated/actual commands are reachable, but plan output is raw JSON plus a basic tree; no full explorer/comparison workspace | Analysis model and commands were conflated with complete plan UX | Plan analysis is partially reachable; explorer composition is still required |
| Sprint 18 implements server activity/session monitoring | `docs/sprints/018-server-activity-session-management.md` | View > Activity Monitor produces a single snapshot in Messages; no live grid, filters, or session actions | Presentation models and provider tests were counted as desktop monitoring | Activity is diagnostic/temporary UI; a live monitor is future work |
| Sprint 19/20 deliver schema compare/synchronisation | `docs/sprints/019-schema-comparison-synchronisation.md`, `docs/sprints/020-schema-compare-synchronisation-preview.md` | No schema command is present; desktop test explicitly asserts schema compare is absent from the release command surface | Service-level comparison and preview were reported as product delivery | Schema compare/preview is service-only and deferred |
| Sprint 21 delivers a data-transfer wizard foundation | `docs/sprints/021-data-transfer-wizard.md` | The current shell does not compose the wizard; Import Data uses a provisional one-shot flow | “Foundation” language is accurate, but later release summaries can read as user availability | Data transfer remains partial until the wizard is composed |
| Sprint 23/24/25/26/27/28 deliver query-performance, plan and index workspaces | Relevant sprint reports under `docs/sprints/` | The current shell has no commands/workspaces for performance history/dashboard, index analysis, plan comparison, or regression history | Application models/tests were mistaken for desktop reachability | These are service-only capabilities and must not be advertised as current UI |
| Sprint 31 says “Query Performance Dashboard” | `docs/sprints/031-query-performance-dashboard.md` | No dashboard command or menu route exists | Model availability and state classifications do not equal composition | Query performance dashboard is deferred and service-only |
| Sprint 32/33 describe explorer/recommendation workspaces | `docs/sprints/032-execution-plan-explorer.md`, `docs/sprints/033-index-analysis-workspace.md` | No corresponding desktop workspace or command is registered | UI-independent implementation was reported alongside desktop-facing language | Plan explorer and index recommendation are not current desktop features |
| Sprint 44 calls the result a “full-system” release baseline | `docs/release/sprint-44-qualification-report.md` | The baseline is a full-system test baseline for the implemented compact surface; many capabilities remain service-only or temporary | “Full-system” can be read as full product completeness | It is a regression baseline, not evidence that all advertised administration features are usable |

## Sprint 52 correction

Sprint 52 narrows the historical schema/index discrepancy: index inspection and
reindex, schema comparison, and non-executing synchronisation preview are now
end-to-end reachable. Automatic index recommendations, dependency-aware
synchronisation execution, and full snapshot/object coverage remain deferred.

## Corrective rule

Sprint 53 composes the file-to-PostgreSQL import and retained-result-to-file
export workflows into reusable WPF workspaces. The import surface supports
delimited files, bounded preview, editable mappings, validation, cancellation,
progress, rejected-row reporting and transfer history. The export surface
supports CSV, TSV, JSON and SQL-insert output with explicit destination,
validation, cancellation and history. PostgreSQL-to-PostgreSQL transfer,
database/object export, and JSON import in the current preview surface remain
deferred and are not claimed as complete.

The current source-of-truth description is the reachability matrix plus the
release-scope reset. Future sprint reports may retain historical status, but
must use “service-only”, “temporary UI”, “partial”, “end-to-end reachable”, or
“release quality” explicitly and must not infer UI completion from tests alone.
