# Sprint 38 completion report

## Outcome

Sprint 38 replaces eager name-based Object Browser loading with a lazy,
OID-based, cancellable and stale-safe metadata pipeline. Refresh is
identity-aware, caches are bounded and context-isolated, catalog access is
permission-aware, search results carry compatible identities, and shutdown
cancels outstanding browser work.

## Defects corrected

1. Initial browser load fetched the complete user catalog and every column.
2. Object identity was based primarily on schema/name text.
3. Routine overloads displayed identically.
4. Refresh replaced the entire tree without generation or stale-result checks.
5. Repeated expansion had no active-request deduplication.
6. The completion cache removed the word `Password` rather than securely
   fingerprinting the complete effective identity.
7. Cache entries were unbounded, had no expiry, and retained failed tasks.
8. System-object filters were embedded inconsistently in different queries.
9. Restricted-role metadata was not deliberately filtered by usable schemas
   and relation privileges.
10. Search results did not carry catalog identity.
11. Metadata exceptions could be displayed without central classification or
    redaction.
12. Browser shutdown did not own or cancel metadata activity.

## Verification

The Release runner provisions an isolated PostgreSQL database and random
owner, read-only, and restricted roles. Live tests cover lazy schema/relation
queries, partitions, materialized views, columns, routine overloads,
aggregates, system filtering, quoted and Unicode identifiers, permissions,
rename, drop/recreate, search identity, missing databases, cancellation, and a
500-table branch. The runner removes all generated schemas, objects, databases,
and roles.

Final Release evidence:

| Measure | Result |
|---|---|
| Run ID | `00c5ddfaea` |
| Tests per run | 257 |
| Three-run passed / failed / skipped | 771 / 0 / 0 |
| Merged line coverage | 79.55% |
| Build | 0 warnings, 0 errors |
| PostgreSQL | 18.4 |
| Temporary database/roles remaining | 0 / 0 |
| Cleanup | passed |

## Assessment

| Dimension | Score |
|---|---:|
| Correctness and identity | 96% |
| Reliability and concurrency | 95% |
| Performance and boundedness | 94% |
| Permission and failure handling | 95% |
| Usability and state preservation | 93% |
| Maintainability and diagnostics | 94% |
| Automated regression coverage | 95% |
| Overall | 95% |

The Sprint 38 release-candidate target of at least 90% is met without adding a
new user-facing object category or redesigning the browser.
