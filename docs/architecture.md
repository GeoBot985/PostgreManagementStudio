# Architecture

PostgreManagementStudio is a Windows WPF modular monolith with five production
assemblies. The enforced project dependency graph is:

```text
Core <- Results <- Application <- Postgres
  ^         ^          ^            ^
  +---------+----------+------------+-- Desktop composition root
```

`Core` contains provider-neutral query/result contracts. `Results` implements
result storage, transformation, formatting, and export. `Application` contains
use-case models and services. `Postgres` contains Npgsql and PostgreSQL catalog
adapters. `Desktop` is the host and presentation layer.

The authoritative hardening rules, ownership model, and known deviations are
in `docs/architecture/architecture-baseline.md`. The Sprint 34 audit is in
`docs/hardening/034-architecture-audit.md`.
