# Sprint 37 regression matrix

| Area | Automated evidence | Result |
|---|---|---|
| Profile validation | required fields, bounds, control characters, unsupported advanced settings, authentication and certificate combinations | Pass |
| Secret safety | profile JSON, display, validation, effective configuration, probe messages, and lifecycle diagnostic assertions | Pass |
| Effective configuration | normalisation, defaults, database override, unsafe-option rejection, shared probe/live identity | Pass |
| Lifecycle races | unique attempts, stale completion rejection, connect/disconnect idempotence, dispose terminality | Pass |
| Profile edits | immutable snapshots, duplicate names, targeted pool invalidation, delete invalidation | Pass |
| Resource protection | per-profile maximum, aggregate reservation ceiling, cancellable exhaustion, 20 callers against pool size 10 | Pass |
| Session isolation | search path, time zone, role, temp table, prepared statement, aborted transaction, and successful reuse | Pass |
| Failure classification | password, missing role, missing database, SSL hostname mismatch, pool wait, cancellation, backend termination | Pass |
| Recovery | post-exhaustion open, post-aborted-transaction query, and post-backend-termination replacement connection | Pass |
| Retry safety | idempotence requirement, eligible failure classes, two-retry maximum, no SQL replay | Pass |
| Production composition | central factory plus probe, diagnostics, invalidator, and profile registry registrations | Pass |
| Architecture | direct Npgsql construction remains confined to the factory and deliberate integration-test controls | Pass |

## Environment-equivalent scenarios

The isolated suite safely simulates password rejection, role rejection,
database removal, certificate/hostname verification failure, pool exhaustion,
backend death, and concurrent tabs. It does not mutate the developer machine's
`pg_hba.conf`, replace certificates, stop the Windows PostgreSQL service, or
configure external GSS/LDAP/SSPI infrastructure. Those are host compatibility
campaigns rather than safe repository tests.

Manual inspection confirms connection errors are actionable and credential
free, the connection test reports server/database/user/SSL/timing fields, and
no new password persistence or clipboard path was introduced.

