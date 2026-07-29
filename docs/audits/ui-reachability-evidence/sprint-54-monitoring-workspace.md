# Sprint 54 monitoring workspace evidence

Sprint 54 composes the existing PostgreSQL activity service into a durable,
server-scoped WPF monitoring workspace.

## Reachable workflows

- `View > Performance Dashboard` and `View > Blocking Diagnostics` open the
  same connection-bound workspace and focus the existing instance when it is
  already open.
- Activity shows the latest sessions with state, duration, transaction age,
  wait event, blocked state and bounded query preview.
- Blocking and waits show the factual relationships returned by
  `pg_blocking_pids`; the lock tab remains a truthful empty/limited state when
  the current adapter returns no lock rows.
- Cancel query and terminate session are separate actions. Both require
  confirmation; the selected PID and identity are revalidated against a fresh
  snapshot before an action is sent, and the workspace refreshes afterward.
- Automatic refresh is opt-in, bounded to one in-flight load, cancellable on
  close, pauseable and interval-controlled.
- Diagnostic output can be saved as JSON or CSV. Query previews are omitted by
  default, can be explicitly included with a bounded length, and output states
  omitted sources. Credentials, connection strings and raw table data are not
  included.

## Deliberate limits

The repository has query-performance models and `pg_stat_statements` capability
semantics but no composed PostgreSQL statistics adapter. Database statistics,
query-performance grids, time-series counter rates and a full support bundle
are therefore reported as unavailable/deferred rather than represented by
placeholder values.

## Verification

- `ShellWorkflowTests.Sprint54_MonitoringWorkspaceExposesActivityBlockingLocksAndPrivacyControls`
  passes on the WPF STA test host.
- Full solution test and build results are recorded in the Sprint 54 report.
