# Sprint 59 Completion Report

## Outcome

Sprint 59 adds a daily-use Alt+F1 describe workflow to the query editor. It
resolves editor identifiers and aliases, presents ordered PostgreSQL metadata in
the persistent output area, formats selected columns, inserts lists, and safely
replaces `*`/`alias.*`.

## Architecture and files

- `ObjectDescription.cs`: editor resolver, structured description contracts,
  presets, deterministic formatter, and bounded editor edits.
- `NpgsqlObjectDescriptionMetadataProvider.cs`: read-only candidate resolution,
  identity validation, canonical PostgreSQL column metadata, and staged secondary
  detail retrieval.
- `QueryTabView.xaml(.cs)`: persistent description grid/text/definition surface,
  keyboard inclusion, copy/insert/replace actions, cancellation, and one-change
  editor updates.
- `ShellCommands.cs` and `MainWindow.xaml(.cs)`: shared `Alt+F1` routed command and
  traditional Query menu integration.
- `ProductionServices.cs`: production DI composition.
- Core, integration, desktop, and composition tests cover the new seams.

The resolver intentionally does not require full-document parsing. It tokenizes
quoted/Unicode identifiers around the caret and uses bounded statement patterns
for aliases, allowing unrelated syntax errors and partially written SQL.

## Catalogue queries

Resolution reads `pg_class`, `pg_namespace`, `pg_proc`, `pg_type`,
`pg_constraint`, and `pg_trigger`, using PostgreSQL visibility functions and
`current_schemas(true)`. Relation columns use `pg_attribute`, `pg_attrdef`,
`pg_type`, `pg_collation`, `pg_constraint`, `pg_index`, `col_description`,
`pg_get_expr`, and `format_type`. Secondary details use `pg_get_constraintdef`,
`pg_get_indexdef`, `pg_get_triggerdef`, and `pg_total_relation_size`. Existing
Sprint 58 canonical scripting supplies routine, sequence, type, index, constraint,
trigger, and schema definitions.

## Verification

- Release build: zero warnings and zero errors.
- Unit coverage: selection/caret priority, qualification, quoting, Unicode,
  aliases, CTEs, routine signatures, presets, all six formats, trusted quoting,
  bounded wildcard replacement, and insertion/caret placement.
- PostgreSQL 18.4 integration: identity/generated/default/type/ordinal truth,
  unique/PK/FK/index participation, view output and definition, enum, sequence,
  routine signature, and search-path visibility.
- WPF: command/menu/context reachability, persistent result surface, connected
  Alt+F1 table description, preset application, formatted copy, alias wildcard
  replacement, and keyboard overload chooser.
- Full release regression with large-dataset coverage: 433 passed, 0 failed,
  0 skipped. The harness also verified successful temporary database and role
  cleanup.

## Performance observations

The command updates status synchronously before any network await. Core identity
and columns are one asynchronous metadata phase; relation size and object
definitions are a second cancellable phase after the grid becomes copy-ready.
The feature does not read user rows or count table contents.

## Screenshots

- [Alt+F1 table and ordered columns](../screenshots/sprint-59/alt-f1-table-columns.png)
- [Alias-qualified resolution](../screenshots/sprint-59/alias-qualified-resolution.png)
- [Required-insert preset](../screenshots/sprint-59/required-insert-preset.png)
- [Copied SELECT list](../screenshots/sprint-59/copied-select-list.png)
- [Replaced alias wildcard](../screenshots/sprint-59/replace-alias-wildcard.png)
- [Overloaded function selector](../screenshots/sprint-59/overloaded-function-selector.png)

## Known limitations and deferred cases

CTE projection inference, guaranteed temporary-backend affinity, and perfect
nested-scope alias disambiguation remain limited as documented in the feature
guide. These limitations fail clearly or allow explicit selection; they do not
execute SQL or silently choose an ambiguous catalogue object.

## Regression risks

The main risks are alias extraction in unusual incomplete SQL and catalogue
version differences. These are bounded by explicit ambiguity handling, database
OID/server-fingerprint validation, cancellation on connection generation change,
catalogue-backed integration tests, and non-parallel WPF package tests.
