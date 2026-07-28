# Sprint 32 — Execution Plan Explorer

Extended the execution-plan tooling with a structured explorer projection and resilient import/capability layer.

## Delivered

- Flattened plan-node grid rows with stable node identifiers, parent IDs, depth, numbering, inclusive/exclusive runtime, and estimated cost contribution.
- Search across node types, relations, indexes, and preserved unknown string properties.
- Severity-labelled evidence warnings for large sequential scans, severe row-estimation mismatches, and external sort methods.
- Safe malformed JSON import results that preserve actionable parser errors without replacing the current plan.
- Centralized PostgreSQL-major capability selection for WAL, memory, serialization, settings, and summary options.
- Existing estimated/actual EXPLAIN safety, unknown-property parsing, raw-plan persistence, plan diagnostics, and comparison services remain separate from UI code.
- Unit coverage for flattening, searching, warning generation, import failure handling, capability mapping, and unavailable runtime metrics.

## Boundary

The desktop retains the existing plan command/result-tab surface; full structured node-grid/details synchronization, import/export dialogs, option controls, and dedicated workspace wiring remain follow-up UI work. No automatic query execution beyond explicitly confirmed Actual Plan, rewriting, tuning, or schema changes was added.
