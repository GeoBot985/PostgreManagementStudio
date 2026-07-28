# ADR 0004 — Result-store memory-limit policy

## Status

Accepted (Sprint 002).

## Context

The in-memory result store must enforce configurable limits so a runaway query
cannot exhaust process memory. The limits are: maximum session memory, maximum
result-set memory, maximum rows per result set.

## Decision

- Limits are validated to be positive integers in `ResultStorageOptions`.
- Defaults: 256 MiB per session, 128 MiB per result set, 1 000 000 rows per
  result set. These are documented as development defaults; production tuning
  is out of scope.
- When a limit fires, the affected store stops retaining further rows but
  continues to count `ReceivedRowCount`. `WasTruncated` and `TruncationReason`
  are set on the first firing and never reset for the lifetime of that store.
- The session limit is checked after each retained batch; the first batch
  that would exceed it is not retained and the session is marked truncated.
- The retention stop is silent in storage (rows are dropped) but explicit in
  the public contract (`WasTruncated`, `TruncationReason`,
  `ReceivedRowCount` vs `RetainedRowCount`).
- Memory accounting is approximate (`ResultSizeEstimator`) — it does not need
  to equal Windows process memory, but it must be deterministic, monotonic
  while rows are appended, and reduced to zero on disposal.

## Consequences

- The user can never accidentally exhaust process memory through a single
  query.
- The visual layer must show a clear truncation banner; the temporary WPF
  UI already does.
- The executor continues consuming batches after retention stops; memory
  remains bounded.

## Alternatives considered

- **Hard error on first limit breach**: would discard the partial result set
  the user already paid for. Rejected — partial data is more useful than
  nothing.
- **Cancel the executor on first limit breach**: violates the spec's
  "allow query execution to be cancelled or completed safely" requirement to
  let the executor finish naturally.
- **Disk-backed spill**: explicitly deferred until in-memory contracts
  stabilise (Sprint 003+).