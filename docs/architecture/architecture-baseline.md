# Architecture baseline

Status: Accepted for hardening from Sprint 34.

## Product shape

PostgreManagementStudio is a Windows-only WPF modular monolith targeting
.NET 9. It has one host and four reusable production libraries:

| Project | Responsibility | Permitted production references |
|---|---|---|
| Core | Provider-neutral query and result contracts | none |
| Results | Result storage, formatting, transformation, serialization | Core |
| Application | Use cases, workflow models, validation, SQL builders | Core, Results |
| Postgres | Npgsql adapters and PostgreSQL catalog/process integration | Core, Application |
| Desktop | WPF presentation and composition root | Application, Postgres, Results |

Tests may reference the layer under test and its dependencies. Production
projects must never reference test assemblies. New project-reference cycles are
forbidden and protected by `ArchitectureBoundaryTests`.

## Composition and lifetimes

`Desktop` is the only composition root. `ProductionServices.Build` is the
authoritative registration path shared by the application and production
composition tests. Registration validation is mandatory.

Target lifetimes:

- stateless validators, parsers, formatters, SQL builders: application lifetime;
- settings, logging, notifications, connection factory: application lifetime;
- tab manager and workspace coordinator: main-window/workspace lifetime;
- query document and cancellation owner: document lifetime;
- Npgsql connection, command, reader, transaction, stream, process: operation
  lifetime and disposed by the creator.

Services must receive collaborators through constructors. Views may request
dialogs and dispatch commands but must not construct database connections,
commands, or transactions.

## PostgreSQL access

`INpgsqlConnectionFactory` is the only approved production construction point
for `NpgsqlConnection`. It returns a closed connection with an explicit
`ApplicationName`; the caller owns opening and asynchronous disposal. Editor,
metadata, monitoring, administration, and plan operations use separate
operation-owned connections. Connection strings and passwords must not be
logged or persisted in diagnostics.

SQL values are parameters. PostgreSQL identifiers use
`PostgreSqlIdentifierQuoter`; identifiers are never passed as value parameters
or concatenated without quoting. Feature-specific SQL remains in Application
builders or Postgres adapters, never XAML.

Transactions are owned by the operation that opens them. Commit and rollback
must be explicit. Cancellation must reach `OpenAsync`, command/reader calls,
file operations, and external processes. Cancellation is an expected terminal
state, not a generic failure.

Sprint 36 adds an explicit exception for editor user-managed transactions:
the executor may retain one serialized connection keyed by the immutable tab
scope. That scope rejects connection-context changes, preserves PostgreSQL's
aborted state until explicit `ROLLBACK`, and is disposed/reset when the editor
closes. Implicit execution remains single-operation ownership.

## Compatibility

Version/capability decisions belong in provider-neutral capability records
populated by Postgres adapters. Unknown newer PostgreSQL versions must use the
latest known-safe behavior; unsupported older versions must fail clearly.
Scattered major-version literals are debt and may not be added.

PostgreSQL 18 is the primary hardening version and PostgreSQL 14 is the minimum
intended release baseline pending the Sprint 40 compatibility matrix.

## Errors, logging, and settings

- Preserve typed PostgreSQL errors and SQLSTATE where available.
- Distinguish validation, cancellation, permission, connection, timeout, and
  unexpected failures.
- Present a concise user message while retaining technical detail for logs.
- Do not log complete SQL, plans, connection strings, credentials, or imported
  row values by default.
- Settings need one authoritative, atomic, version-tolerant persistence service.
  The current absence of that composition is tracked as `TD-034-004`.

## UI and background work

Only WPF event handlers may be `async void`; all work they invoke returns a
`Task`. Each document owns its cancellation source and cancels on close.
Background refreshes must serialize or supersede older snapshots, observe all
exceptions, and stop when the workspace closes. UI collections are updated on
the dispatcher.

## Safety

Destructive actions display the exact target and consequence, require explicit
confirmation, never execute generated recommendation SQL automatically, and
report partial outcomes. Restore, truncate/delete-before-import, termination,
role changes, schema synchronization, maintenance, and statistics reset require
dedicated Sprint 38 review before release.

## Tests and feature freeze

- Unit tests cover deterministic business behavior.
- Integration tests use real PostgreSQL and skip explicitly when configuration
  is absent; a passing no-op test is forbidden.
- Architecture tests protect project direction, UI independence of lower
  layers, and centralized connection construction.
- A sprint is not complete merely because classes exist: production
  reachability and manual verification are separate acceptance evidence.
- From Sprint 34 onward, no user-facing feature is added except to complete an
  approved feature or fix correctness, safety, compatibility, or testability.

## Sprint 37 connection boundary

All production Npgsql construction is owned by `INpgsqlConnectionFactory` and
passes through an immutable `EffectiveConnectionConfiguration`. Connection
tests derive from the same profile/configuration model. Profile changes create
new identities and invalidate only the affected old pool. Provider reset cannot
be disabled, pool sizes are bounded, lifecycle attempts are correlated and
stale-safe, and diagnostics exclude connection strings and credentials. See
`docs/hardening/037-connection-contract.md`.

## Sprint 38 metadata boundary

Object navigation uses `IPostgresObjectMetadataProvider` through
`HardenedMetadataService`. Browser roots load schemas only; schema and relation
children load lazily through parameterised OID-scoped queries. Request owners
provide cancellation, generation and stale-result protection. UI-independent
OID identities drive refresh reconciliation and search compatibility, while
bounded context-aware caches and structured diagnostics prevent cross-profile
leakage and unobserved metadata failures. See
`docs/hardening/038-metadata-contract.md`.
