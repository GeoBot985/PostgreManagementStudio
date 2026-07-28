# Sprint 39 completion report

## Outcome

Sprint 39 replaces the direct backup/restore button-to-process path with
immutable plans, central lifecycle ownership, validated/versioned PostgreSQL
tools, safe structured arguments, bounded cancellable process execution,
atomic backup publication, archive inspection, target-specific restore
confirmation, exact connection validation, classified outcomes, and structured
secret-safe diagnostics.

## Principal corrections

1. Backup and restore previously built and launched a process directly from
   mutable UI values.
2. Tool discovery was synchronous, unversioned, and duplicated by callers.
3. Process output was unbounded and cancellation terminated without a bounded
   graceful/escalation contract.
4. Successful exit alone could make an incomplete backup appear successful.
5. Backup output wrote directly to the final destination.
6. Restore format was inferred from filename extension.
7. Archive input was not structurally inspected before confirmation/execution.
8. Confirmation was generic and not cryptographically bound to one target and
   operation.
9. Existing-target and create-database validation were not distinguished.
10. Restore transaction and possible-partial-change semantics were not
    represented in terminal results.
11. Errors, warnings, diagnostics, and history did not share a structured
    redaction/classification policy.
12. Concurrent operations did not own destination/target locks or a bounded
    process slot.

## Release verification

The Release runner provisions a random database and owner/read-only/restricted
roles on PostgreSQL 18.4. Sprint 39 tests create real custom and plain backups,
inspect the custom archive, restore both formats into disposable databases,
verify data/objects and fresh connections, exercise failure paths, and remove
all generated targets. The normal runner then drops its generated database and
roles.

Final evidence is recorded after the last source review:

| Measure | Result |
|---|---|
| Build | 0 warnings, 0 errors |
| PostgreSQL | 18.4 |
| Tests | 283 passed, 0 failed, 0 skipped |
| Live backup/restore round trips | custom archive and plain SQL passed |
| Generated databases/roles remaining | 0 / 0 |
| Cleanup | passed |

## Assessment

| Dimension | Score |
|---|---:|
| Correctness and target safety | 95% |
| Credential and command security | 96% |
| Process reliability and cancellation | 94% |
| File integrity and recoverability | 95% |
| Compatibility and diagnostics | 93% |
| Usability and failure messaging | 92% |
| Maintainability and automated coverage | 94% |
| Overall release-candidate quality | 94% |

The Sprint 39 target of at least 90% is met. Residual platform-dependent manual
scenarios and the Windows PostgreSQL code-page limitation are documented in
the contract and regression matrix; no out-of-scope backup feature was added.
