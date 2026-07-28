# Sprint 37 connection contract

## Configuration ownership

`EffectiveConnectionConfigurationBuilder` is the authoritative boundary for
provider connections. It normalises connection-string and profile input,
validates required fields and supported options, resolves defaults, disables
unsafe provider settings, and produces an immutable snapshot. Connection tests
derive their short-lived, non-pooled test snapshot from that same model.

The shared `INpgsqlConnectionFactory` is the only production constructor for
Npgsql connections. Query execution, object browsing, metadata, monitoring,
maintenance, security, transfer, planning, search, and administrative adapters
therefore receive the same validation, pooling, timeout, SSL, application-name,
and reset policy.

Profiles are copied into effective snapshots. Editing a registry entry cannot
mutate an in-flight operation. A changed identity clears only the old matching
provider pool; deleting a profile does the same. No fallback profile or
fallback database is selected.

## Lifecycle

Connection owners use these states:

`Disconnected`, `ResolvingProfile`, `Connecting`, `Connected`,
`Disconnecting`, `Reconnecting`, `Failed`, and `Disposed`.

Every attempt has a new correlation ID. A disconnect, replacement attempt, or
dispose invalidates the old ID and cancels its token. Late results are ignored.
Connect/disconnect are idempotent, invalid transitions do not mutate state,
and a disposed owner cannot reconnect.

Only explicitly idempotent work may be retried, at most twice, and only for
DNS, network, or server-unavailable failures. SQL execution is never replayed
automatically.

## Credentials and diagnostics

Passwords, client-key paths, certificate passwords, and provider connection
strings are excluded from profile serialisation, debugger display, `ToString`,
test results, lifecycle telemetry, and user-facing failures. Validation errors
are field-specific and do not echo rejected values. The application has no
saved-profile credential store, so Sprint 37 does not add plaintext
persistence or repopulate password UI state.

Structured lifecycle telemetry includes attempt/profile IDs, operation,
endpoint identity, SSL mode, timing, final state, failure category, retry
count, pool wait, and SQLSTATE. It contains neither SQL nor credential
material.

## Pool and session policy

- Provider session reset is mandatory; `No Reset On Close=true` is rejected.
- Detailed provider errors are disabled to reduce value leakage.
- Default maximum pool size is 20; a profile may request at most 50.
- Registered profile reservations may total at most 200 connections.
- Background and administrative work are each capped by policy at 4.
- Pool waits accept cancellation.
- Profile edits and deletion invalidate only the affected pool identity.
- Broken provider connections are discarded by Npgsql and tested by backend
  termination.
- Returned sessions rely on Npgsql reset, which rolls back aborted
  transactions and resets role, settings, temporary objects, and prepared
  statements before reuse.

The current application does not persist profiles or run periodic health
checks. The probe and lifecycle controller are the reusable connection-test
and health/reconnect primitives for those existing workflows when composed.

