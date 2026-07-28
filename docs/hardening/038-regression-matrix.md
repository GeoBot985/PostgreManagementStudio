# Sprint 38 regression matrix

| Area | Evidence | Result |
|---|---|---|
| Request lifecycle | unique IDs, generations, supersession, out-of-order completion, cancellation, terminal disposal | Pass |
| Lazy loading | root returns schemas only; schema/relation expansion is on demand; repeated expansion shares one task | Pass |
| Stable identity | rename equality, schema separation, column attribute numbers, drop/recreate OID difference | Pass |
| Refresh | OID reconciliation preserves renamed node instance and removes dropped nodes | Pass |
| Cache isolation | profile/configuration/database/visibility/node keys, credential identity change, bounds, expiry and scoped invalidation | Pass |
| Cache failure safety | failed and cancelled requests never populate caches; returned completion collections are frozen | Pass |
| System filtering | catalog, information schema, toast, temporary and user classifications; visibility survives refresh context | Pass |
| Object correctness | partitioned table, partition, materialized view, sequence, foreign-table mapping, functions, procedures and aggregate | Pass |
| Routines | function overload signatures, procedure distinction, aggregate classification and deterministic signature ordering | Pass |
| Columns | relation OID plus attribute number identity and ordinal ordering | Pass |
| Permissions | read-only role sees granted schema/relations; restricted role does not see revoked schema | Pass |
| Concurrent changes | live rename, drop, recreate, and missing-object classification | Pass |
| Search consistency | live search result identity equals browser identity for the same relation | Pass |
| Identifier handling | spaces, mixed case, Unicode and quoted qualified names in seeded/live objects | Pass |
| Large catalog | 500-table schema loads in one deterministic bounded batch under the measured ten-second guard | Pass |
| Failure handling | permission, missing object, missing database, connection, cancellation, timeout and disposal categories | Pass |
| UI/shutdown | expansion runs asynchronously; refresh cancels older work; browser disposal is included in window shutdown | Pass |
| Privacy | context display, errors, telemetry and UI notifications exclude connection strings and credentials | Pass |

The existing browser has no property panel or dependency view, so Sprint 38
does not add either feature. Their shared identity, lifecycle, cache and
diagnostic primitives are ready for those existing product workflows when
they become reachable.

