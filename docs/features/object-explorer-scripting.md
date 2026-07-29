# Object Explorer scripting and context actions

Sprint 58 adds traditional Object Explorer context menus that generate
PostgreSQL scripts in connected, unsaved query tabs. Generated SQL is always
presented for review and is never executed by a scripting command.

## Commands

Right-click, the context-menu key, and `Shift+F10` open the same node-specific
menu. `F5` refreshes the selected scope, `F2` starts a supported rename,
`Delete` opens a destructive confirmation, and `Enter` toggles the selected
node. The menu provides New Query, Script Object as, Select Rows, Properties,
Refresh, Rename, Delete, Copy Name, and Copy Qualified Name where applicable.

Read-only connections disable Rename and Delete. Disconnected sessions disable
metadata-dependent commands. Context-menu availability is computed from the
existing tree descriptor without synchronously retrieving object definitions.

## Script support

- Databases, schemas, and extensions: CREATE, DROP, and DROP/CREATE.
- Tables, partitioned tables, partitions, foreign tables, views, materialized
  views, and sequences: CREATE, DROP, DROP/CREATE and applicable DML templates.
- Columns: an explicit single-column SELECT from the owning relation.
- Indexes, constraints, and triggers: canonical PostgreSQL CREATE and DROP.
- Functions and procedures: `pg_get_functiondef` CREATE and signature-qualified
  DROP.
- Enum, domain, and composite types: CREATE and DROP.

SELECT uses physical column order and `LIMIT` with the configured display limit.
INSERT excludes generated, identity-always, and normally omittable defaulted
columns. UPDATE and DELETE always contain a WHERE clause and use primary-key
placeholders when available. DROP never adds `CASCADE`.

The metadata provider relies on PostgreSQL catalogue deparsers such as
`pg_get_functiondef`, `pg_get_viewdef`, `pg_get_constraintdef`,
`pg_get_indexdef`, and `pg_get_triggerdef` for complex definitions. All
identifiers pass through the application's single trusted PostgreSQL quoting
implementation.

## Destructive actions

Rename validates a non-empty identifier, requires confirmation, executes a
type-appropriate `ALTER ... RENAME TO`, and refreshes the explorer. Delete is
separate from DROP scripting: its confirmation displays the exact statement,
states that dependencies remain restricted, and executes only after explicit
approval. Database errors preserve the tree and flow through the existing
application error surface.

## Known boundaries

- Foreign-table CREATE preserves the foreign server and table/column options.
  User-mapping and foreign-server creation remain separate administrative work.
- Table reconstruction includes ordered columns, types/modifiers, defaults,
  identity/generated attributes, nullability, collation, constraints,
  partition keys and bounds, inheritance, tablespace/storage parameters,
  non-constraint indexes, triggers, and table/column comments. Per-identity
  sequence options, ownership statements, grants, and row-security policy
  reconstruction remain deferred.
- Materialized-view scripts use `WITH DATA`; original population state is not
  retained.
- Sequence ownership and domain constraints are included where PostgreSQL
  exposes them. Owner/comment statements are not yet appended to every CREATE
  script.
- Properties is read-only and shows definition, owner, privileges, size, row
  estimates, and structural counts where the selected object exposes them;
  some fields remain unavailable for object classes without composed metadata.
- Rename/delete permission capability is conservatively gated by connection and
  read-only state; PostgreSQL remains the final authority and reports
  insufficient privilege or dependency errors.

Unsupported or incomplete CREATE cases produce a clear error rather than
emitting a misleading partial script.
