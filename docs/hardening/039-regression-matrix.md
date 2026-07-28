# Sprint 39 regression matrix

| Area | Automated evidence |
|---|---|
| Lifecycle and command state | invalid transitions, disposal, idempotent cancellation, terminal state tests |
| Immutable plans | absolute/snapshotted destinations, target binding, secret-safe rendering and JSON |
| Discovery and versions | real PostgreSQL 18 discovery/version execution; matching, newer, older, unknown, and malformed version tests |
| Argument safety | structured arguments with spaces, quotes, ampersands, parentheses, leading hyphens, and Unicode |
| Credential safety | no password arguments/previews/serialization/history; redacted URI/output; temporary pass-file cleanup |
| Destination and atomic output | existing-file policy, writable probe, format validation, failed replacement preservation, successful atomic commit |
| Formats | signature-based plain/custom/tar/directory detection; tool routing and format-option validation |
| Archive inspection | live `pg_restore --list`, bounded metadata, corrupt/mismatch classification |
| Restore safeguards | exact target summary, single-use confirmation, changed-target rejection, create-database identity and maintenance context |
| Process hardening | redirected asynchronous streams, bounded output, timestamps, Windows cancellation/process-tree escalation |
| Errors and warnings | authentication, permission, database, corrupt/version archive, warning grouping, completed-with-warnings |
| Concurrency | conflicting resource rejection, independent resources, lock reuse after release, bounded process count |
| Live backup | full custom backup, schema-only plain backup, non-empty/format/inspection verification |
| Live restore | custom archive restore and plain SQL restore into disposable databases; object/data checks and fresh connection validation |
| Live failure | incorrect password, missing database, corrupt source |
| Existing regression | full Release solution runner, PostgreSQL integration/performance gates, cleanup |

Manual/environment-dependent evidence retained for release sign-off:

- network destination interruption and read-only removable media;
- insufficient disk space;
- PostgreSQL service termination during active dump/restore;
- abrupt application or operating-system termination;
- large production-scale verbose and parallel directory restores;
- non-active-code-page PostgreSQL tool paths on Windows.
