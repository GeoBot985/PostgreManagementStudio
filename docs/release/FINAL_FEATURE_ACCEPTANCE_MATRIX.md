# Final feature acceptance matrix

`Yes` evidence refers to the frozen candidate's Sprint 57 integration run,
package/UI inspection, or named earlier composition evidence. `Conditional`
means the feature is included but external clean-environment acceptance remains
open; it is not a hidden pass. PostgreSQL coverage is 18.4 unless stated.

| Feature | Decision | UI reachability | Primary / failure / cancellation / safety | Version | Evidence | Limitation | Final |
|---|---|---|---|---|---|---|---|
| Connection management | Included | END_TO_END_REACHABLE | Yes / Yes / Yes / N/A | 18.4 | 393-test run; packaged shell | TLS/SSPI matrix unqualified | Conditional pass |
| Object Explorer | Included | END_TO_END_REACHABLE | Yes / Yes / Yes / context-safe | 18.4 | Metadata integration; Sprint 55 | No designers/actions | Conditional pass |
| SQL editor/files | Included | END_TO_END_REACHABLE | Yes / Yes / N/A / N/A | N/A | Desktop/Core tests; UI launch | Basic editor only | Pass |
| Query execution | Included | END_TO_END_REACHABLE | Yes / Yes / Yes / transaction-safe | 18.4 | Integration suite | Clean-VM UI route pending | Conditional pass |
| Transactions | Included | END_TO_END_REACHABLE | Yes / Yes / Yes / Yes | 18.4 | Integration suite | SQL-authoring surface | Pass |
| Result grids | Included | END_TO_END_REACHABLE | Yes / Yes / N/A / N/A | 18.4 | Results/integration tests | Bounded, non-editable | Pass |
| Result export | Included | END_TO_END_REACHABLE | Yes / Yes / Yes / atomic output | 18.4 | Results/transfer tests | Retained rows only | Pass |
| Plans | Included | END_TO_END_REACHABLE | Yes / Yes / Yes / review warning | 18.4 | Integration/Sprint 51 | Actual plans may execute SQL | Conditional pass |
| Object search | Included | END_TO_END_REACHABLE | Yes / Yes / Yes / N/A | 18.4 | Integration/Sprint 50 | Scope is connected DB | Pass |
| Query history | Deferred | SERVICE_ONLY | No / No / N/A / N/A | N/A | Matrix | No browser/capture route | Deferred |
| Backup | Included | END_TO_END_REACHABLE | Yes / Yes / Yes / exact target | 18.4 | Integration | Needs local tools | Conditional pass |
| Restore | Included | END_TO_END_REACHABLE | Yes / Yes / Yes / confirm/revalidate | 18.4 | Integration/Sprint 50 | Disposable-target evidence only | Conditional pass |
| Maintenance | Included | END_TO_END_REACHABLE | Yes / Yes / Yes / confirm target | 18.4 | Integration/Sprint 51 | Version/permission-gated | Conditional pass |
| Roles/permissions | Removed | SERVICE_ONLY | No / No / N/A / N/A | N/A | Matrix | No role editor | Removed |
| Activity monitor | Included | END_TO_END_REACHABLE | Yes / Yes / Yes / session revalidation | 18.4 | Integration/Sprint 54 | Permissions required | Conditional pass |
| Session cancel/terminate | Included | END_TO_END_REACHABLE | Yes / Yes / N/A / explicit confirmation | 18.4 | Integration/Sprint 54 | Disposable sessions only | Conditional pass |
| Performance/query/database statistics | Deferred | SERVICE_ONLY | No / No / N/A / N/A | N/A | Matrix | No composed adapters | Deferred |
| Index management | Included | END_TO_END_REACHABLE | Yes / Yes / Yes / revalidate | 18.4 | Integration/Sprint 52 | Inspect/reindex only | Conditional pass |
| Schema comparison | Included | END_TO_END_REACHABLE | Yes / Yes / Yes / two targets explicit | 18.4 | Integration/Sprint 52 | Coverage is bounded | Conditional pass |
| Synchronisation preview | Included | END_TO_END_REACHABLE | Yes / Yes / N/A / no execution | 18.4 | Sprint 52 | Direct synchronisation absent | Pass |
| Import | Included | END_TO_END_REACHABLE | Yes / Yes / Yes / stale-plan validation | 18.4 | Integration/Sprint 53 | Delimited files only | Conditional pass |
| Export/transfer | Included / transfer deferred | END_TO_END_REACHABLE | Yes / Yes / Yes / atomic output | 18.4 | Integration/Sprint 53 | No PG-to-PG transfer | Conditional pass |
| Settings | Deferred | SERVICE_ONLY | No / No / N/A / N/A | N/A | Matrix | Defaults persist; no editor | Deferred |
| Layout | Deferred | PARTIALLY_REACHABLE | No / No / N/A / N/A | N/A | Matrix | Reset only; no coherent layout UX | Deferred |
| Recovery | Included | END_TO_END_REACHABLE | Yes / Yes / N/A / no auto-execution | N/A | Recovery tests | No recovery manager | Pass |
| Packaging | Included | RELEASE_QUALITY evidence | Yes / Yes / N/A / state preserved | Windows 11 x64 | Manifest, verify, lifecycle | Unsigned | Conditional pass |
| Upgrade | Included, narrow claim | END_TO_END_REACHABLE installer | Yes / N/A / N/A / external state | Windows 11 x64 | Prior-package installer upgrade | Stateful old-profile campaign pending | Conditional pass |

No service-only, temporary, or partially reachable feature is included in the
release scope. `RELEASE_QUALITY` is reserved for the packaging evidence only;
the UI matrix retains its conservative classifications.
