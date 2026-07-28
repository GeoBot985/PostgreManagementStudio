# Sprint 002 — Result Storage and Virtualisation Foundation

## Objective

Introduce a reusable, provider-neutral, in-memory result-storage layer that sits between
the Sprint 001 async query-execution pipeline and the future production result grid.
The layer accepts streamed row batches, retains typed values, exposes random-access
reads, preserves multiple result sets independently, and avoids creating UI objects for
every cell.

This sprint does **not** implement the final visual grid.

## Prerequisites

Sprint 001 (`QueryRequest`, `IQueryExecutor`, `QueryExecutionEvent` stream,
`ResultRow`, `ResultCell`, `ResultSetSchema`, `DatabaseNotice`, `DatabaseError`,
`ResultStorageOptions` defaults). Sprint 001 contracts were honoured without
modification. The Sprint 002 contracts are additive and live in new files.

## Store contracts

### Public (`PostgreManagementStudio.Core`)

- `IResultSession` — one query execution. Aggregates result sets, notices,
  error, elapsed, memory, received/retained row counts, truncation flag.
- `IResultSetStore` — read-only view of a single retained result set. Provides
  `LoadedRowCount`, `ReceivedRowCount`, `FinalRowCount`, `EstimatedMemoryBytes`,
  `WasTruncated`, `TruncationReason`, and `GetRowAsync`/`GetRowsAsync` reads.
- `IResultSetWriter` (`internal`) — append-only mutator: `AppendBatchAsync`,
  `CompleteAsync`, `CancelAsync`, `FailAsync`. The visual layer never sees this.
- `IResultSessionBuilder` — `ExecuteAndBuildAsync(QueryRequest, CancellationToken)`
  returning a fully populated `IResultSession`.
- `ResultSessionStatus`, `ResultSetStatus`, `ResultTruncationReason`,
  `ResultStorageOptions` — enums and options record.
- Exception types: `ResultStoreException`, `ResultRowUnavailableException`,
  `InvalidBatchException`, `ResultSetTerminalException`,
  `DuplicateResultSetIndexException`, `ObjectDisposedResultStoreException`.

### Implementation (`PostgreManagementStudio.Results`)

- `ResultSession` — aggregate owning stores, notices, error, elapsed, status.
- `ResultSetStore` — single result set with batch index, append-only writer,
  read-only store, lifecycle.
- `ResultSetIndex` — atomic-snapshot batch index with binary-search lookup.
- `BatchSegment` — immutable retained batch (start index, rows, memory).
- `ResultSizeEstimator` — deterministic, type-aware memory accounting.
- `LifecycleGuards` — valid-transition rules for both state machines.
- `ResultSessionBuilder` — public builder that drives stores from the event
  stream and applies lifecycle transitions.

## Lifecycle rules

```
ResultSessionStatus:
  Created   -> Running | Completed | Cancelled | Failed | Disposed
  Running   -> Completed | Cancelled | Failed | Disposed
  Completed -> Disposed
  Cancelled -> Disposed
  Failed    -> Disposed

ResultSetStatus:
  Created    -> Receiving | Completed | Cancelled | Failed | Disposed
  Receiving  -> Completed | Cancelled | Failed | Disposed
  Completed  -> Disposed
  Cancelled  -> Disposed
  Failed     -> Disposed
```

Invalid transitions throw `InvalidOperationException` in development and are
covered by `LifecycleGuards` tests.

## Batch indexing approach

`ResultSetIndex` keeps a reference-typed `BatchSegment[] _snapshot` that is
swapped atomically on each append. Readers take a `Volatile.Read` snapshot
reference and binary-search the segments without blocking the writer. The
writer holds a short `lock` only to update the snapshot pointer and counters;
the lock is never held across awaits.

Complexity:

| Operation | Complexity |
|---|---|
| Append batch | O(1) amortised (one array-snapshot allocation per append) |
| Get single row by index | O(log n) (binary search on `StartRowIndex`) |
| Range of k rows crossing b batches | O(log n + k + b) |
| Memory accounting | O(rows × cells), constant-time per cell |

## Concurrency model

- One logical writer per store; multiple concurrent readers.
- Atomic snapshot pointer published via `Volatile.Read`/`Volatile.Write`.
- Counter updates use `Interlocked` so reads observe consistent totals.
- A short `_stateLock` on each store and on the session serialises lifecycle
  transitions; no lock is held across awaits.

Reads during execution are safe and do not block the writer. Disposal is
idempotent and does not require a UI thread.

## Memory estimation method

`ResultSizeEstimator` produces deterministic, type-aware estimates:

- Per-cell cost: `NullCellBytes` for `NULL`, boxed sizes for common value
  types (`int`/`long`/`double`/`bool`/`Guid`/`DateTime`/`TimeSpan`/`decimal`),
  `StringHeaderBytes + length * 2` for `string`, `ByteArrayHeaderBytes + length`
  for `byte[]`, header + reference slots for arrays.
- Per-row overhead: 24 B (`ObjectHeaderBytes`) + 8 B padding.
- Per-batch overhead: 24 B container + 32 B inner array + 8 B per row slot.
- Per-schema cost: `ObjectHeaderBytes` per `ResultColumn` plus string headers
  for `Name` and `PostgreSqlTypeName`.

The estimate is monotonic while rows are appended and reduces to zero after
disposal. It is documented as an estimate; it does not match Windows process
memory exactly.

## Configured defaults

`ResultStorageOptions.Default`:

| Limit | Value |
|---|---|
| `MaximumSessionMemoryBytes` | 256 MiB |
| `MaximumResultSetMemoryBytes` | 128 MiB |
| `MaximumRowsPerResultSet` | 1 000 000 |

Validation throws `ArgumentOutOfRangeException` for non-positive values.
Production tuning is a future concern.

## Truncation policy

When a configured limit is reached the store:

1. Stops retaining further rows.
2. Sets `WasTruncated` to `true` and records `TruncationReason`
   (`MaximumRowsReached`, `ResultSetMemoryLimitReached`, or
   `SessionMemoryLimitReached`).
3. Continues updating `ReceivedRowCount` so callers can distinguish retained
   from received rows.
4. Never drops rows silently and never misreports retained as total.

The session mirrors the flag; the temporary UI displays a clear truncation
banner with the reason and the received vs retained counts.

## Disposal behaviour

- Idempotent: repeated `DisposeAsync` is a no-op.
- Releases retained row batches and large references.
- Sets `EstimatedMemoryBytes` to zero on the session and on every store.
- Throws `ObjectDisposedResultStoreException` for any subsequent read or write.
- Does not require a UI thread; safe from any thread.

## Test coverage

### Unit (`tests/PostgreManagementStudio.Results.Tests`)

41 tests across seven files covering batch-append validation, random access
(first/middle/last row, cross-batch range, invalid indices, post-disposal),
multiple result-set independence and duplicate-index rejection, lifecycle
transitions (valid, invalid, idempotent disposal), memory limits
(row / result-set / session / post-truncation, post-disposal accounting),
memory monotonicity, and concurrent reader/writer stress.

### Integration (`tests/PostgreManagementStudio.IntegrationTests`)

7 tests against live PostgreSQL 18.4 covering incremental arrival (10 000 rows),
multiple result sets (10 + 20), mixed command + result sets (temp table +
insert + select), cancellation with partial data (`pg_sleep(0.01)` cross-join),
failure after earlier successful result (missing table), row-limit truncation,
large values (10 000-char strings).

### Performance (`tests/PostgreManagementStudio.IntegrationTests/ResultStoragePerfTests.cs`)

6 tests gated by `PMS_RUN_PERF=1`:

- First batch readable before completion (channel-level observation).
- Memory bounded under 100 MiB for 100 000 scalar rows.
- Median lookup latency under 200 µs for 100 000 rows × 1 000 samples.
- 100-row range retrieval under 100 ms.
- Disposal under 1 000 ms.
- Summary report written to `PMS_PERF_REPORT_DIR/perf-report.txt`.

When run under `dotnet test`, xUnit's vstest sandbox cleans up files after
each test; the assertion verifies the file was successfully written during
the test run. To inspect the report, set `PMS_PERF_REPORT_DIR` to a path
outside the sandbox (e.g. the repo root).

## Performance results

Recorded against PostgreSQL 18.4 on the developer workstation with the
integration suite; metrics captured during the gated perf run:

- 100 000 scalar rows retained in well under the configured 256 MiB session
  budget; median lookup ~tens of microseconds.
- 100 000 mixed-type rows (`int` + `md5(string)` + `repeat('x', 100)`): same
  bounds hold; first/middle/last random access served in O(log n).
- Disposal of a 100 000-row session completes in tens of milliseconds and
  reduces `EstimatedMemoryBytes` to zero.
- No WPF objects are allocated in the Results project — view-model objects are
  created in `PostgreManagementStudio.Desktop` only.

## Known limitations

- The perf report file may be removed by `dotnet test`'s sandbox after the
  test completes. Use `PMS_PERF_REPORT_DIR` to redirect to a persistent path.
- Memory accounting is an estimate; it does not equal Windows process memory.
- Cancellation latency depends on the executor emitting an
  `ExecutionCancelled` event; the builder only treats the session as cancelled
  after that event arrives or after `OperationCanceledException` is raised
  on the consumer side.
- The session limit is enforced after each retained batch (writer path); very
  fast bursts may briefly exceed the limit by one batch's worth of memory.

## Deferred work

- Disk-backed spill storage (Sprint 003+).
- Production visual grid.
- Monaco editor.
- Object Explorer, plans, history.
- Column pinning, sorting, filtering.

## Final status

Sprint 002 is complete. All 41 unit tests, all 12 integration tests, and all
6 gated perf tests pass against local PostgreSQL 18.4. The temporary WPF UI
uses the result store with no per-cell observable and a paged 100-row preview.