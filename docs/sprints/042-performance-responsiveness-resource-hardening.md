# Sprint 42 — Performance, Responsiveness, and Resource Hardening

## Completion status

Complete. The production query-result path is paged and virtualised, previews
and caches are bounded, expensive editor requests are supersession-safe, Object
Explorer creates controls lazily, recurring work cannot overlap, and shutdown
has a bounded asynchronous lifecycle. A deterministic large PostgreSQL fixture
and resource-trend tests make these constraints repeatable.

## Performance budgets

Budgets are production P95 acceptance ceilings on the reference local Windows
development machine. Median targets are deliberately set below P95 so a single
fast run cannot conceal a poor trend. Network-dependent operations also report
round trips and cancellation instead of treating latency as UI blocked time.

| Workflow | Median target | P95 target | UI blocked | Memory / resource target |
|---|---:|---:|---:|---|
| Application startup | 1.2 s | 2 s | < 100 ms contiguous | 1 owned timer; no connection |
| Connection dialog | 100 ms | 250 ms | < 50 ms | no database round trip |
| Local connection | 1 s | 2 s | < 50 ms | 1 validation connection |
| New query editor | 75 ms | 150 ms | < 50 ms | no metadata load |
| First editor input | 25 ms | 50 ms | < 50 ms | completion is cancellable |
| Trivial query | 500 ms | 1 s | < 50 ms | 1 execution connection |
| First result page | 100 ms | 250 ms | < 50 ms | 250 rows, at most 1,000 |
| Object Explorer expansion | 1 s | 2 s | < 50 ms | 1 database round trip per uncached level |
| IntelliSense metadata | 1 s | 2 s | < 50 ms | cache maximum 256 |
| Database object search | 500 ms | 1 s | < 50 ms | debounced, limited server result |
| Tab switch | 50 ms | 100 ms | < 50 ms | no result reformat |
| Background monitor callback | 25 ms | 50 ms | < 50 ms | one in flight |
| Close editor with large result | 500 ms | 1 s | < 50 ms | releases page/session ownership |
| Reconnect | 1.5 s | 3 s | < 50 ms | one serialized attempt |
| Shutdown | 2 s | 5 s | < 100 ms | timer stopped; bounded cleanup |

The executable P95 values live in `PerformanceBudgets`. Structured operations
record duration, approximate process allocation, rows read/displayed, bytes,
round trips, cache outcome, cancellation/failure, logical session and
connection generation. SQL text and row values are never diagnostic fields.

## Before-and-after evidence

| Area | Before | After | Verification |
|---|---|---|---|
| Initial result bind with 10,000 retained rows | 10,000 rows copied and formatted eagerly | 250 rows read/formatted; 40x fewer formatting calls | deterministic formatter-call test |
| Million-row query | retained rows were all copied into the WPF binding path | 1,000,000 read, 10,000 retained, 250 displayed | 2.190 s integration; 0.737 s native shell |
| Large-value display | text, JSON, arrays and binary could expand before final clipping | value-aware bounded construction; 512-char default preview | multi-megabyte preview tests |
| Object Explorer | a loaded node recursively created controls for every descendant | one placeholder plus one visual level on expansion | 1,000 tables fixture; 2 round trips in 7.8 ms |
| Search/completion | independent late results could replace newer input | shared debounce/cancellation/version coordinator | supersession and document-version tests |
| Regex cache | process-lifetime unbounded key growth | maximum 64 entries | 200-pattern cache test |
| Health polling | dispatcher callbacks could overlap | one guarded one-second callback | desktop lifecycle test |
| Result/session cleanup | synchronous task bridging in disposal | asynchronous, idempotent disposal outside locks | lifecycle and cancellation tests |

The release run also measured 100,000 scalar result construction at 145 ms,
100,000 mixed rows at 407 ms, median indexed lookup at 0.3 microseconds,
100-row range retrieval below timer resolution, and million-row bounded
transfer at 2.190 s. The native shell executed and displayed the bounded
million-row query in 0.737 s and advanced to rows 251–500 without rebinding the
retained result. Main-window construction plus clean shutdown completed in 234
ms. These are trend samples, not replacements for the P95 budgets.

## UI-thread and asynchronous-work audit

The audit covered startup, shell construction, editor creation, result binding,
Object Explorer, metadata, completion, object search, tab lifecycle, export,
backup/restore, health checks and shutdown.

- Database, file, metadata, export and result-store reads remain asynchronous.
- Result formatting is limited to one requested page before one batched grid
  bind. WPF row/column virtualisation, recycling and deferred scrolling are
  enabled; column width is capped at 420 device-independent pixels.
- Object Explorer creates only the expanded visual level. Metadata remains
  lazy and batched by node.
- Completion, object search and result search cancel superseded requests and
  reject stale document/context versions. Search uses a 200 ms debounce.
- The only `async void` paths are true WPF event handlers.
- No `.Result`, `.Wait()` or `GetAwaiter().GetResult()` remains on critical
  database, result, metadata or disposal paths. The remaining nonblocking
  `SemaphoreSlim.Wait(0)` only elects the query-cancellation owner.
- Dispatcher use is restricted to bounded UI mutation. Exceptions from
  diagnostics and late cleanup are observed and isolated.

## Query-result memory and display policy

PostgreSQL rows are consumed incrementally by the existing batched execution
pipeline. `ResultStorageOptions` independently bounds session bytes, result-set
bytes and retained rows. Reaching a bound is an explicit truncation state;
received, final, retained/displayed and omitted counts are reported separately.

The desktop requests 250-row pages and never binds more than 1,000 rows per
page. Source cells stay typed in the result store; only the current page has
display strings. Text, JSON, binary and array previews are built within the
configured character limit rather than constructing an unbounded intermediate
representation. Arrays stop after 32 items, JSON stops after 32 children or
eight levels, binary previews include the full byte count, and execution-plan
messages stop at 64 KiB / 500 nodes / 24 levels. The UI identifies incomplete
previews.

Export continues to stream retained typed rows directly from the store and is
independent of the visual page. It does not claim to export rows omitted by the
configured storage bound. Cancellation disposes readers, commands and
connections; closing an editor cancels page/search/completion work and disposes
result tabs and sessions idempotently.

## Object Explorer and search analysis

The deterministic fixture contains five schemas, 1,000 tables, 250 views, 250
functions, 20 indexes, 16 partitions, long/quoted identifiers and large-value
and million-row sources. Root plus one schema expansion uses exactly two
uncached database round trips; the measured test completed in 7.8 ms. Cache
hits do not increment the round-trip diagnostic. Concurrent duplicate loads,
obsolete generations, collapse/refresh and disposal are covered by the
existing metadata hardening coordinator.

Object search remains parameterised and server-limited. The shell adds
debounce, cancellation and generation checks so rapid input cannot execute or
apply obsolete searches. Completion has the same ownership model and validates
the query-document version before applying results.

## Recurring work and ownership inventory

| Work | Owner | Cadence / trigger | Overlap policy | Stop condition |
|---|---|---|---|---|
| Shell status/health | `MainWindow` | 1 second | guarded single in-flight callback | disconnect, closing, disposal |
| Query execution | query tab execution coordinator | user command | one owned operation; explicit cancel | terminal event, tab unload |
| Result page load | result tab | page command | prior page cancelled | replacement, tab/editor unload |
| Completion | query tab | editor input | latest request wins | superseded, unload |
| Object search | query tab | search input after 200 ms | latest request wins | superseded, unload |
| Result search/sort | result tab | user input after 200 ms | latest request wins | tab replacement/unload |
| Object expansion | Object Explorer node/service | user expansion | duplicate suppressed | collapse, refresh, generation change, disposal |
| Backup/restore process | operation controller | user command | bounded global/resource leases | terminal state or cancel |

There is one recurring production timer. It is stopped before shutdown cleanup;
its callback cannot re-enter itself. Other work is event-triggered and has an
explicit cancellation/disposal owner.

## Cache inventory and policy

| Cache | Scope / key | Bound / expiry | Invalidation and disposal |
|---|---|---|---|
| Hardened metadata | app; profile, credential identity, database, object identity, kind | 256 entries; 5 minutes | refresh, logical context/generation change, failure eviction |
| Legacy completion metadata facade | instance; connection string identity | 32 entries; 5 minutes | explicit invalidation or owner disposal |
| Result regex | process; pattern plus options | 64 entries | FIFO pressure eviction; immutable regex values |
| PostgreSQL tool discovery | service; configured directory | one entry; 5 minutes | configured path change or explicit invalidation |
| Backup operation locks | operation service; target resource | active operations only, maximum two processes | lease disposal |
| Query execution scopes | executor; execution ID | active executions only | terminal event/cancellation/disposal |

Connection profiles, result sessions and document collections are owned state,
not caches; each is removed on its lifecycle close. Npgsql pooling remains
enabled with driver defaults. Connections/commands/readers are scoped with
deterministic disposal. Low-overhead counters report connection creation and
targeted pool clears without including connection strings.

## Memory-leak and endurance investigation

Automated lifecycle tests open, dispose and close 100 query documents in under
two seconds and confirm no more than one weak reference remains after forced
collection. Twenty real PostgreSQL connect/query/read/dispose cycles sample
managed heap and Windows handles every five cycles; the settled endpoint must
remain within 16 MiB and 32 handles of the first settled sample. The run passed
in 410 ms. Result-store tests cover 100,000-row memory bounds, cancellation,
partial results, fast disposal and post-disposal access. Result-tab unload
removes handlers and disposes completion, search, paging and backup owners.

The accelerated endurance matrix exercises connection/disconnection,
execution/cancellation, 100 editor lifecycles, result creation/disposal,
metadata refresh/supersession, searches, timer ownership and shutdown. The
release run showed no monotonic retained managed-memory or handle trend after
forced collections. A multi-hour interactive soak remains useful on each
target workstation but is not required to reproduce the enforced resource
contracts.

## Startup and shutdown

Shell construction is intentionally connection-free and avoids eager metadata
or hidden workspace creation. Only the visible shell and one low-frequency
status timer are created. The measured construction/clean-close lifecycle was
234 ms against a 2 s startup and 5 s shutdown P95 budget.

Closing first resolves unsaved-editor recovery. Once approved, it stops the
timer, cancels active work, and awaits session, Object Explorer, editor and
service cleanup without synchronously blocking the dispatcher. Cleanup has a
five-second ceiling; late completion is observed so shutdown cannot deadlock or
produce unobserved task failures.

## Verification

The release runner created a uniquely named disposable PostgreSQL 18.4 database
and roles, applied both normal and performance seeds, ran Release, and removed
all resources:

| Project | Passed |
|---|---:|
| Core | 170 |
| Results | 63 |
| PostgreSQL | 50 |
| Desktop | 14 |
| Live integration | 55 |
| **Total** | **352** |

Build output had zero warnings and zero errors. The suite covers bounded
batches/pages/previews/caches, visible truncation, cancellation/disposal,
obsolete expansion/search/completion, duplicate-load suppression, nonoverlap,
timer shutdown, event ownership, repeated lifecycle collection, million-row
transfer, large schema navigation and PostgreSQL resource stabilization.

## Known remaining bottlenecks

- Sorting and filtering intentionally operate on the current displayed page,
  not all omitted rows. Server-side full-result transformation is a future
  feature, not hidden materialisation.
- Export can include only rows retained by the configured result-store policy;
  exporting every server row would require a separate direct streaming query
  contract.
- The DataGrid still creates WPF containers for the visible viewport and very
  wide schemas remain constrained by WPF layout cost despite bounded widths.
- JSON is retained as a typed Npgsql value; opening a future full-value viewer
  may need deferred server reread for values larger than the result-store
  budget.
- P95 latency on slow remote links depends on server/network conditions.
  Cancellation and stale-response behavior are deterministic, but network
  shaping and multi-hour native UI soaks remain environment-level campaigns.

Closed editors/results are reclaimable under forced-collection tests, large
result processing remains bounded, and no critical database workflow performs
a synchronous wait on the UI thread.

## Native verification

The Windows release shell connected to local PostgreSQL 18, executed a
`generate_series(1, 1000000)` query with a text column, reported 1,000,000
received, 10,000 retained and `MaximumRowsReached`, rendered rows 1–250, and
paged to 251–500. The first native run exposed an empty nested result tab
because repopulation did not explicitly select its first item; this was
corrected and the complete scenario was repeated successfully. The shell
closed cleanly after releasing the result session and owned health timer.
