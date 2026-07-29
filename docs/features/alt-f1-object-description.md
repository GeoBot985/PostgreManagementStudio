# Alt+F1 Object Description

Press `Alt+F1` in a connected SQL editor, or choose **Query > Describe Object**, to
describe the selected identifier or the identifier at the caret. The command is
read-only: it queries PostgreSQL catalogues and never executes selected SQL.

## Resolution

Resolution prefers selected text, then the complete quoted or unquoted identifier
at/adjacent to the caret. Within the current semicolon-delimited statement it maps
`FROM`, `JOIN`, `UPDATE`, `INTO`, and `USING` aliases to their source relation.
Schema qualification and routine type signatures are preserved. PostgreSQL
visibility functions and `current_schemas(true)` provide search-path ordering;
multiple visible matches open a keyboard-operable chooser. CTEs are identified as
editor-local and are not misrepresented as persistent catalogue objects.

Supported catalogue targets are tables, partitioned and foreign tables, views,
materialized views, sequences, indexes, constraints, triggers, functions,
procedures, schemas, enums, domains, and standalone composite types. Relation
columns use `format_type`, exclude dropped/system columns, and retain `attnum`
ordinal order.

## Column formats and insertion

The toolbar provides horizontal, vertical, SELECT-list, qualified SELECT-list,
quoted SELECT-list, and qualified quoted formats. Quoting uses
`PostgreSqlIdentifierQuoter`; SQL Server brackets are never emitted.

**Insert** replaces the active selection or inserts at the caret. **Replace \***
only searches the current statement's SELECT list. It replaces exactly one `*` or
matching `alias.*`; zero or multiple matches are rejected. The editor applies the
replacement inside one WPF change block, preserving line endings and a single
undo unit.

## Inclusion and presets

All columns begin included. Use **All**, **Clear**, **Invert**, the name filter, or
Space on selected rows. Selection order never changes PostgreSQL ordinal order.

- **All visible**: every non-dropped user column.
- **Writable**: excludes generated and `GENERATED ALWAYS AS IDENTITY` columns.
- **Required insert**: writable, `NOT NULL`, and no default.
- **Key columns**: primary-key columns, otherwise unique columns.
- **Non-large**: excludes `bytea` and `text` columns.

## Loading and safety

Resolution, core identity, and columns load asynchronously. The panel displays the
copy-ready core result before a second cancellable request loads relation size,
constraints, indexes, and triggers. Reconnect generation tokens cancel stale work.
No table rows, `COUNT(*)`, sequence values, user DML, or generated SQL are executed.

## Known limitations

- CTEs are identified but their projected columns are not inferred from arbitrary
  expressions.
- Alias mapping is statement-local and best-effort for syntactically incomplete
  SQL; deeply nested scopes that reuse the same alias can require explicit
  selection or qualification.
- Temporary objects are resolvable only when visible to the metadata connection;
  the current stateless query-session model does not guarantee reuse of the
  physical backend that created a temporary object.
- Plain `text` is conservatively treated as large by the Non-large preset because
  PostgreSQL does not expose a declared maximum length for it.
