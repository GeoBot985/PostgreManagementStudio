# Architecture

The modular monolith has Core, Application, Postgres, Results, and Desktop layers. Desktop depends on Application/Postgres/Results; Postgres and Results depend on Core. The initial flow is button -> Application service -> Core interface -> Npgsql adapter -> PostgreSQL `SELECT version()` -> temporary UI.

Deferred: SQL editor, result grid, object explorer, cloud support, plugins, and visual redesign.
