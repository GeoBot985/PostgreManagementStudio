# Sprint 39 backup and restore contract

## Operation ownership and lifecycle

Each operation is assigned a unique ID and executes from one immutable
`BackupOperationPlan` or `RestoreOperationPlan`. A controller owns at most one
operation and exposes state-derived start/cancel capability. Validating,
preparing, confirmation, process start, running, cancellation, finalisation,
and terminal states are explicit. Invalid transitions throw; stale completion
for a disposed or superseded owner is ignored. Cancellation is idempotent.

Closing the owning view disposes its controller. Disposal cancels the owned
operation; the process runner first requests graceful closure and then kills
the complete process tree after a bounded grace period. No process task is
fire-and-forget.

## Tools, arguments, and credentials

`PostgreSqlToolDiscoveryService` resolves `pg_dump`, `pg_restore`, and `psql`
from an explicitly configured directory, application directory, supported
Windows installation paths, or `PATH`, in that order. It validates expected
filenames, executes `--version`, records major versions, caches results for
five minutes, and supports explicit invalidation.

Commands use `ProcessStartInfo.ArgumentList` and never invoke a shell.
Passwords are supplied only in the child process environment, with
`--no-password` preventing hidden interactive prompts. The application
environment is not modified. Command previews, records, diagnostics, model
serialization, and captured output omit or redact secrets. The legacy
temporary pass-file helper creates a unique hidden file, restricts its Windows
ACL to the current user where supported, and provides deterministic deletion.

## Backup safety

Before process start the destination is resolved to an absolute path, its
parent and type are validated, overwrite policy is explicit, reparse-point
parents are rejected, and writability is probed. A destination lock prevents
overlapping writers.

`pg_dump` writes to an operation-specific partial path in the destination
directory. A zero-byte or format-mismatched output fails verification.
Custom, tar, and directory archives must also pass `pg_restore --list`.
Verified output is renamed or atomically replaced; an unsupported atomic
replacement falls back to the safest platform move and produces a warning.
Failure and cancellation clean partial output without deleting a pre-existing
valid destination.

## Restore safety

Input format is detected from archive signatures, tar metadata, directory
table-of-contents, or a bounded plain-SQL prefix—not from the extension.
Archives are inspected with `pg_restore --list`. Empty, mismatched, corrupt,
and unsupported archives fail before restore execution.

Every restore is treated as destructive because restoring into an existing
database can overwrite data or definitions. The confirmation is bound to the
operation ID, server, port, database, source, and destructive options; it can
be consumed only once. Any changed target or option requires a fresh plan and
confirmation.

Create-database restores are accepted only for archives whose recorded
database name exactly matches the confirmed target. They connect deliberately
through `postgres`; protected maintenance databases and plain-SQL
create-database mode are rejected.

Target validation uses a short-lived, non-pooled connection. Existing-target
mode requires the exact database to be reachable. Create mode uses the
maintenance database and requires the exact target not to exist. Successful
restore is followed by a fresh target connection. No automatic destructive
correction or retry occurs.

Single-transaction restore is incompatible with parallel jobs. A cancelled or
failed non-transactional/partially transactional restore reports that the
target may contain partial changes. Transactional failure does not claim
partial application.

## Process, output, diagnostics, and concurrency

Standard output and error are drained asynchronously and tagged with source
and timestamps. The in-memory console retains the latest 2,000 lines by
default; truncation is recorded. Exit code zero may complete with warnings,
but non-zero exit is always classified as failure. Authentication,
permissions, connection/database, archive, dependency, process-start,
cancellation, and unknown failures have separate categories.

At most two external PostgreSQL processes run concurrently by default.
Destination and target locks are operation-scoped and released on every exit
path. Operations against distinct resources remain independent.

Structured diagnostics record operation/profile IDs, server and tool major
versions, format, timestamps, state, exit code, output size, warning count,
cancellation/escalation, validation/verification, and failure category. They
do not record connection strings, passwords, secret environment variables,
dump contents, or full filesystem paths.

## Residual platform risks

PostgreSQL Windows client binaries can be limited by the active system code
page even though .NET preserves Unicode argument values. Code-page-safe
Unicode paths are live-tested and arbitrary Unicode/shell metacharacters are
covered at the argument boundary. Network-share interruption, free-space
prediction, crash recovery after abrupt OS termination, and server-process
termination during a restore remain environment-dependent manual scenarios.
Basic structural verification proves inspectability, not full restorability or
data correctness.
