# Sprint 58 reconciliation

Candidate: `424133bff9684c962b93a71feab3ebdc49da46bd`

Legend: “Automated” means the named Release tests passed in both Sprint 61
iterations. “Packaged” means directly observed in the staged executable.

| Requirement | Implemented | Tested | Passed | Evidence | Defect |
| --- | ---: | ---: | ---: | --- | --- |
| Context-menu availability | Yes | Packaged + automated | Yes | Node-specific menu on `s61_audit.orders`; Desktop composition | — |
| Right-click targets clicked node | Yes | Automated/prior live | Yes | Sprint 58 live review and Desktop tests | — |
| `Shift+F10` | Yes | Packaged | Yes | Menu opened for visibly selected `orders` | — |
| Context-menu keyboard key | Yes | Automated | Yes | Shared keyboard routing tests | — |
| Script-generation commands | Yes | Packaged + automated | Yes | CREATE/DROP/DROP+CREATE/DML submenu | — |
| CREATE scripts | Yes | PostgreSQL integration | Yes | `SupportedCatalogueObjectsProduceFidelityScriptsAndSequenceRoundTrips` | Deferred fidelity limits documented |
| DROP scripts | Yes | PostgreSQL integration | Yes | Qualified replay; no implicit `CASCADE` | — |
| DROP and CREATE | Yes | Automated | Yes | Deterministic composition and replay tests | — |
| SELECT template | Yes | Packaged + automated | Yes | Ten explicit quoted columns, `LIMIT 10000`, two rows | — |
| INSERT template | Yes | Automated | Yes | Defaults/generated/identity-always excluded | — |
| UPDATE template | Yes | Automated | Yes | Primary-key-shaped condition, no unconditional update | — |
| DELETE template | Yes | Automated | Yes | Primary-key-shaped condition, no unconditional delete | — |
| Properties | Yes | Reachability/automated | Partial | Menu present and metadata service covered; dialog not fully walked in this audit | RC evidence condition |
| Refresh | Yes | Packaged + automated | Partial | Command present; lazy hierarchy tests pass; timed full refresh not captured | RC evidence condition |
| Rename | Yes | Automated | Yes | OID-safe rename/drop/recreate integration | Manual confirmation not repeated |
| Delete | Yes | Automated | Partial | Exact-DROP confirmation contract covered | Action-time destructive confirmation not granted |
| Copy Name | Yes | Packaged + automated | Yes | Context command present; deterministic labels | — |
| Copy Qualified Name | Yes | Packaged + automated | Yes | Context command present; PostgreSQL quoting service | — |
| Connection/database binding | Yes | Packaged + integration | Yes | Connected `postgres`; stale database identity rejected | — |
| Read-only gating | Yes | Integration | Yes | PostgreSQL enforcement and UI composition tests | — |
| Permission gating | Yes | Integration | Yes | Restricted/read-only role metadata tests | — |
| Destructive confirmation | Yes | Automated | Partial | Confirmation contract and exact SQL tests | Manual execution intentionally not performed |
| Identifier quoting | Yes | Packaged + automated | Yes | Observed quoted script; hostile identifier integration test | — |

## Scripting replay coverage

The two-pass Release run executed the following PostgreSQL-backed tests:

- `SeededTableProducesExecutableCreateAndSafeTemplates`
- `SupportedCatalogueObjectsProduceFidelityScriptsAndSequenceRoundTrips`
- `RenameAndDropRecreateUseOidIdentityAndRoutineOverloadsRemainDistinct`
- `HostileIdentifier_RemainsOneQuotedObjectAndMetadataIsInert`

Together they cover ordinary and partitioned tables, identity/generated
columns, defaults, comments, constraints, views, materialized views, sequences,
functions and overloads, procedures, indexes, triggers, enums, domains, and
composite types. Deferred ownership/grant, row-security, and identity sequence
options are documented limitations, not silently claimed fidelity.
