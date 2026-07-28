# Sprint 36 SQL execution contract

## Lifecycle

Every editor owns a `QueryExecutionLifecycle` and a stable tab ID. Every run
gets a new execution ID and follows:

```text
Idle -> Preparing -> Executing -> Completed
                              \-> Failed
                              \-> ConnectionLost
Preparing/Executing -> Cancelling -> Cancelled
terminal -> Idle -> Preparing
```

Transitions are validated under a lock. Terminal updates must carry the active
execution ID, so an old completion cannot change a newer run. A cancelling run
cannot become completed. One editor permits one run; separate editors remain
independent.

## Immutable execution context

Before provider work starts, the document captures execution ID, tab ID,
connection profile ID, server/port, intended database, username, SSL mode,
transaction mode, SQL/selection, and start time. The intended database is
written into a copied connection string before execution. A missing profile,
connection, or database fails explicitly; no fallback is selected.

Changing editor fields after start cannot redirect a running query. A
user-managed transaction scope rejects connection-context changes until that
scope is disposed.

## Cancellation and disposal

The document owns a linked cancellation source. Cancel is idempotent and moves
the lifecycle to `Cancelling` once. Npgsql receives the token on open, execute,
read, next-result, and channel writes; a token registration also requests
provider-level `NpgsqlCommand.Cancel()`.

The provider-to-store channel is bounded to four events, preventing an
unbounded producer queue. If cancellation exceeds the configured timeout, the
document reports a controlled warning while retaining ownership of cleanup.
Editor and application close cancel active work, await bounded completion, and
dispose result sessions and tab-scoped connections.

## Transactions

Implicit runs own a connection for one execution. Disposal invokes Npgsql pool
reset, which rolls back any incomplete implicit transaction before reuse.

User-managed mode uses one serialized Npgsql connection per editor tab.
PostgreSQL's `25P02` aborted-transaction state remains visible until the user
executes `ROLLBACK`; it is never silently committed or replaced with another
connection. A context change is rejected while the scope exists. Closing the
editor disposes the scope, causing deterministic rollback/reset.

## Results and cells

Production retention defaults to 10,000 rows per result set, 64 MiB per result
set, and 128 MiB per session. The provider still consumes the server result so
the actual received/final row count is preserved; SQL is never rewritten with
`LIMIT`. Rows affected are tracked separately. The UI reports warnings at
5,000 rows and clearly labels truncation.

Cell previews default to 512 characters. Binary previews are bounded hex with
the byte count and are never decoded as text. `NULL` remains distinct from an
empty string. A formatter exception produces a cell-local marker and does not
escape the value into diagnostics.

## Errors and diagnostics

`DatabaseError` retains SQLSTATE, severity, primary message, detail, hint,
positions, object names, constraint, routine, and optional source information.
Source file/line are shown only in diagnostic mode. Error kinds distinguish
query, constraint, authentication, timeout, connection loss, provider, and
application failures.

Structured telemetry contains correlation IDs, profile/database, timestamps,
final state, row counts, cancellation, timeout category, and SQLSTATE. It
contains no connection string, password, SQL text, or result value. Arbitrary
SQL is never retried automatically.
