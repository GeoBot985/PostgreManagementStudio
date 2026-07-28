# ADR 0003 — Result-store batch indexing and concurrency model

## Status

Accepted (Sprint 002).

## Context

The result store must retain streamed row batches, expose random-access reads,
and allow concurrent reads during active execution. Three candidate models were
considered:

1. Single global lock around the batch list.
2. Reader/writer lock per store.
3. Atomic snapshot pointer with a short per-store write lock.

## Decision

Adopt model 3: a reference-typed `BatchSegment[] _snapshot` is the single source
of truth for readers. The writer holds a short `lock` only to register a new
segment and atomically publish the next snapshot via `Volatile.Write`. Readers
`Volatile.Read` the snapshot pointer and binary-search it. Counters use
`Interlocked` so observers see monotonic totals without taking the lock.

## Consequences

- Lock-free reads; reads during execution never block the writer.
- Append is amortised O(1): one allocation per appended segment plus the
  array-copy of the snapshot.
- Random access is O(log n); range retrieval is O(log n + k + b).
- The lock is never held across user code or awaits, which removes a class of
  deadlocks. Lifecycle transitions take a separate `_stateLock`.
- Slightly higher writer cost (one extra array allocation per append) than a
  single global lock; reads are dramatically cheaper.

## Alternatives considered

- **Single global lock**: trivial to reason about, but every read would block
  every other read and the writer. Rejected for not satisfying the concurrent-
  read requirement.
- **ReaderWriterLockSlim**: familiar, but easy to misuse (held locks +
  awaits, recursive acquisition). Rejected as more complex than necessary.
- **Fully lock-free immutable tree (e.g. persistent vector)**: lowest
  contention but adds a dependency and is overkill for the in-memory target.