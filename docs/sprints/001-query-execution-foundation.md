# Sprint 001 — Query Execution Foundation

Status: Complete with documented low-severity issues.

Added provider-neutral query request, options, result metadata, typed cells/rows, notices, errors, and execution event contracts in Core. Added an asynchronous Npgsql reader that opens connections asynchronously, streams row batches, preserves typed values, emits metadata and notices, and disposes the reader/command/connection.

Event ordering is `ExecutionStarted`, result-set metadata and row batches, result-set completion, command completion, then `ExecutionCompleted`. Failures emit `ExecutionFailed`; cancellation emits `ExecutionCancelled`; neither emits `ExecutionCompleted`.

Verification: local PostgreSQL integration tests passed for scalar queries, batching/order, multiple result sets, commands, notices, errors, cancellation, and recovery. The temporary WPF screen supports arbitrary SQL and cancellation. Formal UI timing/performance measurement and independent review remain low-severity follow-ups.
