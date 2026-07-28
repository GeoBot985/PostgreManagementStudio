# ADR 0005 — Result-store partial-results policy on cancellation and failure

## Status

Accepted (Sprint 002).

## Context

When a query is cancelled or fails after some rows were already retained, the
user expects the already-received data to remain readable. A blank result
panel after cancellation is hostile. Conversely, a half-broken writer that
keeps accepting new data after a terminal event is worse — it can corrupt
counts and confuse the UI.

## Decision

- On `ExecutionCancelled`: each store in `Created` or `Receiving` transitions
  to `Cancelled`. Completed stores remain completed. The session transitions
  to `Cancelled`. Already retained rows remain readable.
- On `ExecutionFailed`: each store in `Created` or `Receiving` transitions to
  `Failed`. Completed stores remain completed. The session transitions to
  `Failed`, capturing the structured `DatabaseError`. Already retained rows
  remain readable.
- On `OperationCanceledException` raised by the consumer before the executor
  emits a terminal event: the builder applies cancel semantics itself,
  transitioning all non-terminal stores to `Cancelled` and the session to
  `Cancelled`.
- A later `AppendBatchAsync` after a terminal state throws
  `ResultSetTerminalException`.
- Disposal is idempotent and never throws because of a terminal state.

## Consequences

- Multi-statement scripts retain data from earlier successful statements.
- The visual layer can show partial data alongside a clear status indicator
  without having to relaunch the query.
- The visual layer must respect `ResultSetStatus` when reading rows from a
  failed store.

## Alternatives considered

- **Clear retained data on terminal events**: rejected — partial data is
  valuable; the user explicitly chose to run the query.
- **Continue accepting writes after terminal events**: rejected — silently
  corrupts the store's invariants and confuses any consumer.
- **Throw on read after terminal events**: rejected — explicitly required to
  keep partial rows readable.