# Sprint 62 defect closure

## S61-C01 — packaged wildcard replacement

- Root cause: unaliased relation basenames were treated as explicit aliases.
- Fix: distinguish explicit aliases; bind edits to active document/statement;
  preserve formatting; reject stale state.
- Files: `ObjectDescription.cs`, `QueryTabView.xaml.cs`.
- Tests: core wildcard variants and composed WPF replacement/undo/redo.
- Automated result: pass in both full-suite repetitions.
- Packaged result: simple bare wildcard replaced; exact undo and redo passed.
- Remaining limitation: none known.
- Final status: **Closed**.

## S61-C02 — valid JSONB import

- Root cause: binary COPY received ordinary JSON text for JSONB.
- Fix: certify binary-safe mappings and select validated typed parameter batches
  for complex types.
- Files: `ProductionDataTransfer.cs`, `DataTransferWizard.cs`,
  `NpgsqlDataTransferService.cs`, `DataTransferWorkspaceWindow.cs`.
- Tests: complex PostgreSQL type matrix and composed strategy coverage.
- Automated result: pass in both full-suite repetitions.
- Packaged result: 2/2 JSONB/array/enum/timestamptz rows committed and verified.
- Remaining limitation: none known for supported types.
- Final status: **Closed**.

## S61-H01 — transfer diagnostics

- Root cause: driver exceptions were flattened before source/mapping context was
  attached.
- Fix: structured `TransferError` records and safe UI/rejected-row formatting.
- Files: `DataTransfer.cs`, `ProductionDataTransfer.cs`,
  `NpgsqlDataTransferService.cs`, `DataTransferWorkspaceWindow.cs`.
- Tests: invalid JSON asserts logical row, source column, destination column,
  destination type, safe message, and unchanged atomic row count.
- Automated result: pass in both full-suite repetitions.
- Packaged result: valid path reports structured strategy/counts; failure model
  is exercised by integration because deliberately inserting invalid test data
  in the final packaged pass was not necessary to close the attributable seam.
- Remaining limitation: PostgreSQL errors that cannot be attributed to one
  mapped value retain operation-level context.
- Final status: **Closed**.

## S61-M01 — multiline row estimate

- Root cause: review counted physical newline-delimited lines.
- Fix: count records through the delimited logical-record parser and preserve
  physical line spans separately.
- Files: `ProductionDataTransfer.cs`, `DataTransferWizard.cs`.
- Tests: multiline parser/estimate regressions.
- Automated result: pass.
- Packaged result: two logical records shown in preview and review despite a
  multiline quoted JSON field.
- Remaining limitation: review remains a bounded estimate for unsampled
  malformed content.
- Final status: **Closed**.

## S61-M02 — reproducible ZIP

- Root cause: ZIP entry timestamps varied between builds.
- Fix: deterministic ordering and fixed entry timestamps.
- Files: `scripts/release/build-release.ps1`.
- Tests: package construction and verification coverage.
- Automated result: release pipeline pass.
- Packaged result: two clean builds had identical SHA-256.
- Remaining limitation: binaries remain unsigned.
- Final status: **Closed**.

## S61-RC01 — SDK-free clean machine

- Root cause: environmental qualification gap, not a product code defect.
- Fix: none possible on the current host.
- Automated result: self-contained package and startup composition pass.
- Packaged result: development-host startup passes.
- Remaining limitation: no Windows 11 x64 machine without a development SDK
  was available.
- Final status: **Remains blocking**.

## S61-RC02 — signing

- Root cause: no release signing identity/certificate is configured.
- Fix: deterministic manifest and checksums preserve internal provenance but do
  not replace Authenticode.
- Automated result: package identity and hashes pass.
- Packaged result: unsigned.
- Remaining limitation: public trust chain absent.
- Final status: **Remains blocking** for public release.

## S61-RC03 — full manual qualification

- Root cause: the Sprint 61 Critical defects stopped the original matrix; the
  post-fix run focused on the repaired seams and discovered one new crash.
- Fix: repaired seams and broad automated coverage are complete.
- Automated result: 918/918 pass with no skips.
- Packaged result: repaired core flows pass, but the entire 25-step sequence was
  not repeated without inherited/partial steps.
- Remaining limitation: complete packaged new-table, all-format, cancellation,
  accessibility, confirmed deletion, disconnect, and close evidence.
- Final status: **Remains blocking**.

## S62-C01 — disposed result-session handoff crash

- Severity: Critical when discovered.
- Root cause: shell status read a disposed prior result store after the document
  had switched sessions but before shell state had caught up.
- Fix: read the active document session and tolerate disposed handoff only for
  shell status metrics.
- Files: `QueryTabView.xaml.cs`.
- Tests: `Sprint62_ShellStateIgnoresDisposedResultDuringSessionHandoff`.
- Automated result: pass in both complete repetitions.
- Packaged result: the same query executed twice; app remained alive and showed
  the second result.
- Remaining limitation: none known.
- Final status: **Closed**.
