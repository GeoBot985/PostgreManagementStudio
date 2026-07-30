# Sprint 62 completion report

## Outcome

Sprint 62 repaired and qualified the product defects found in Sprint 61.
PostgreManagementStudio `0.9.0-rc.4` closes `S61-C01`, `S61-C02`, `S61-H01`,
`S61-M01`, and `S61-M02`. A new result-session handoff crash discovered during
packaged qualification was also repaired.

Final release decision: **Not approved for release**. Product-critical repairs
pass, but clean-machine execution, the uninterrupted 25-step packaged run, and
public-artifact signing remain release conditions.

## Root causes and fixes

### Wildcard replacement

`SqlRelationReferenceParser.ReadAliases` assigned an unaliased relation's
basename as an explicit alias. The composed Alt+F1 command therefore searched
for `table.*` in a statement containing bare `*`.

The parser now distinguishes explicit aliases from relation names. The editor
action retains the active document/statement identity, rejects stale or
wrong-tab state, preserves line endings and indentation, and applies the
replacement as one undoable operation.

### Complex PostgreSQL import

Automatic import selected binary COPY for JSONB and sent JSON text as though it
were PostgreSQL's versioned binary JSONB payload. The strategy selector now
allows binary COPY only for certified mappings. Complex mappings use validated
parameterised batches with explicit PostgreSQL casts and pre-execution
validation.

JSON, JSONB, enum, domain, arrays, UUID, bytea, temporal, interval, network,
range, and multirange values are covered by the integration matrix. Unsupported
types fail validation before execution.

### Transfer diagnostics and row estimates

Transfer failures previously exposed driver text without the logical record or
mapped destination. The transfer result now carries structured diagnostics:
logical row, physical line start/end, source column, redacted value policy,
destination column/type, category, safe SQLSTATE, and safe detail.

Review estimates now count parsed logical records. Multiline quoted fields no
longer inflate the displayed row count.

### Deterministic packaging

ZIP creation previously inherited variable entry timestamps. The release
builder now writes entries in deterministic order with fixed timestamps. Two
clean builds from the same revision produced an identical archive hash.

### Result-session handoff

Packaged re-execution exposed a crash not present in the Sprint 61 defect list.
`QueryDocument` disposed the previous result session before the shell status
bar switched references; status rendering then read the disposed result store.
The shell now prefers the active document session and treats only a disposed
handoff as unavailable status data. Other errors are not hidden.

## Architecture decisions

- SQL relation resolution remains independent of WPF composition.
- Import strategy selection is explicit and visible in review/results.
- Binary COPY is an allow-list optimisation, not a default for unknown types.
- Complex values are validated and cast by destination PostgreSQL type.
- Diagnostics are structured data first and formatted for UI second.
- Source values are redacted by default; diagnostics identify location without
  leaking credentials or sensitive payloads.
- Result ownership remains with the document; the shell reads that source of
  truth during session transitions.
- Reproducibility is achieved in the packager rather than by weakening package
  provenance checks.

## Product files changed

- `Directory.Build.props`
- `scripts/release/build-release.ps1`
- `src/PostgreManagementStudio.Application/DataTransfer.cs`
- `src/PostgreManagementStudio.Application/DataTransferWizard.cs`
- `src/PostgreManagementStudio.Application/ObjectDescription.cs`
- `src/PostgreManagementStudio.Application/ProductionDataTransfer.cs`
- `src/PostgreManagementStudio.Desktop/AssemblyInfo.cs`
- `src/PostgreManagementStudio.Desktop/DataTransferWorkspaceWindow.cs`
- `src/PostgreManagementStudio.Desktop/QueryTabView.xaml.cs`
- `src/PostgreManagementStudio.Postgres/NpgsqlDataTransferService.cs`

## Tests changed

- `tests/PostgreManagementStudio.Core.Tests/ObjectDescriptionTests.cs`
- `tests/PostgreManagementStudio.Core.Tests/ProductionDataTransferTests.cs`
- `tests/PostgreManagementStudio.Desktop.Tests/ShellWorkflowTests.cs`
- `tests/PostgreManagementStudio.IntegrationTests/ProductionDataTransferIntegrationTests.cs`

New regressions cover the exact composed wildcard action, one-step undo/redo,
disposed result-session handoff, certified COPY selection, typed complex
fallback, structured invalid-JSON diagnostics, multiline logical records, and
the complete PostgreSQL type matrix.

## Tests run

- Targeted Core, Desktop, and PostgreSQL integration tests during repair.
- Release package build and 407-file package verification.
- Two complete Release-suite repetitions with large-dataset gates enabled.
- Package hash comparison across two clean builds.
- Exact-package WPF walkthrough against PostgreSQL.
- NuGet vulnerability scan and credential-pattern review.

## Results

- Full run ID: `9ec79f1f71`
- Pass 1: 459 passed, 0 failed, 0 skipped
- Pass 2: 459 passed, 0 failed, 0 skipped
- Combined: **918 passed, 0 failed, 0 skipped**
- Cleanup: succeeded
- Package verification: 407 files passed
- Package SHA-256:
  `A928BDB8600CD2E4787B0ADCF9AE0FEAE84FB5E01B07DE987073178568F01E7E`
- Deterministic rebuild: identical hash
- Vulnerable resolved NuGet packages: none reported

## Packaged results

The exact package successfully:

1. launched and connected to PostgreSQL;
2. described `s62_audit.example_table`;
3. replaced bare `*` with the six ordered columns;
4. restored the exact source with undo and restored the expansion with redo;
5. executed a complex-type query twice without a handoff crash;
6. previewed two logical CSV rows despite a multiline JSON field;
7. reported the typed fallback before execution;
8. committed two JSONB/array/enum/timestamptz rows with zero rejects;
9. read the imported values back correctly.

The complete prescribed 25-step scenario was not captured as one all-pass run.
See `docs/audits/sprint-62/PACKAGED_WORKFLOW.md`.

## Performance

The full two-pass harness elapsed in approximately 73.4 seconds. Large-data
gates ran. The final packaged two-row complex import completed in approximately
174 ms. Transfer services remain streamed or batched with bounded preview and
result retention.

## Security and privacy

The supplied local test password was used only through the local environment
and was not committed. Package verification rejects seeded credentials.
Diagnostics redact source values by default. No vulnerable resolved packages
were reported by the configured NuGet source.

## Known limitations and release conditions

- No SDK-free Windows 11 test target was available.
- The complete 25-step packaged workflow remains incomplete as a single rc.4
  run.
- The candidate is unsigned.
- The comprehensive packaged screen-reader, all-format round-trip, and
  cancellation matrix was not repeated.

These limitations do not reopen the repaired product defects, but they prevent
release approval under the Sprint 62 rules.

## Commits

- `b617589` — Sprint 062: repair release-blocking core workflows
- `516e655` — Sprint 062: prevent result handoff crash
- Final documentation commit records the qualification decision and evidence.
