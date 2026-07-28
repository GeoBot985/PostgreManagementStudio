# Sprint 38 metadata and Object Browser contract

## Request lifecycle

Every root, expansion, and refresh request is owned by a
`MetadataRequestController` with a unique request ID and monotonic generation.
States are `Idle`, `Queued`, `Loading`, `Refreshing`, `Cancelling`,
`Completed`, `Failed`, `Cancelled`, `Stale`, and `Disposed`.

A new refresh cancels and supersedes the prior request. Completion is applied
only when its request ID still owns the controller. Cancellation is idempotent,
late success or failure becomes stale, and disposal is terminal. Repeated
expansion of one node shares its active task rather than issuing duplicate
catalog queries. Browser shutdown recursively cancels and disposes node owners.

## Stable identity

`PostgresObjectIdentity` compares:

- connection profile and effective configuration identities;
- server fingerprint and database OID;
- catalog object OID and object class;
- parent and schema OIDs;
- sub-object number.

The name snapshot is descriptive and deliberately excluded from equality.
Consequently, a rename retains identity and browser state, while drop/recreate
with the same name gets a new identity. Columns use relation OID plus
`pg_attribute.attnum`. Synthetic category groups use a parent OID and category
sub-object number and never masquerade as catalog objects.

## Lazy provider boundary

`IPostgresObjectMetadataProvider` is the object-navigation boundary. The root
query returns database identity and visible schemas only. Schema expansion
loads that schema's supported relations and routines in two bounded,
parameterised catalog queries. Relation expansion loads only that relation's
columns. Ordinary expansion does not calculate sizes, statistics, properties,
or dependency graphs.

The existing completion snapshot API remains compatible, but its provider path
uses a fixed set of batched queries rather than one query per object.

Provider queries are read-only, cancellable, permission-aware, deterministic,
and scoped by schema or relation OID where applicable. No query silently
switches databases.

## Classification and visibility

The metadata model distinguishes ordinary and partitioned tables, partitions,
views, materialized views, sequences, foreign tables, functions, procedures,
aggregates, window functions, indexes where returned by search, and columns.
The current browser presents only its pre-existing user-facing categories.

Schemas are classified as user, catalog, information schema, toast, temporary,
temporary toast, or extension-owned. Filtering is applied after identity
construction and never mutates cached source metadata. Search returns the same
OID identity fields and system-visibility policy for navigable relation
results.

Routine identity uses `pg_proc.oid`; display and qualified names include
`pg_get_function_identity_arguments`, preserving overloads and correct drop
signatures.

## Refresh and reconciliation

Refresh invalidates the exact context/node cache key and reconciles incoming
children by stable identity. Existing node instances survive renames, retaining
safe expansion state. New objects are inserted, dropped objects are removed,
and dropped/recreated objects do not inherit old state. A failed refresh keeps
the last valid children and attaches a structured node error.

Ordering is deterministic by object class, schema, column ordinal, case-aware
name, routine signature, and OID. Columns remain in catalog ordinal order.

## Cache policy

Object metadata uses a thread-safe cache bounded to 256 entries by default with
a two-minute lifetime. Keys contain profile identity, effective configuration
identity, database, stable object identity, operation, and visibility mode.
Completion snapshots use a separate 32-entry, two-minute cache keyed by a
SHA-256 connection identity and database.

Credential changes therefore produce different keys without storing or
logging plaintext connection strings. Cancelled and failed requests are
removed and never become valid cache entries. Returned collections are
read-only copies. Refresh, node invalidation, profile invalidation, and complete
cache invalidation are explicit operations.

## Errors and diagnostics

Metadata errors are classified as cancellation, permission denial, connection
loss, missing object, unavailable database, timeout, unsupported version,
invalid metadata, provider failure, disposal, or unknown. User messages are
concise and secret-free.

Structured diagnostics record request/profile/database/node identities,
operation, start/completion timestamps, rows, cache status, cancellation,
final state, failure category, and SQLSTATE. They do not record connection
strings, passwords, SQL text, definitions, or property values.

