# State of the Nation

## Final release decision

**Not approved for release**

Sprint 63 verified the identity and host-side integrity of
PostgreManagementStudio `0.9.0-rc.4`, provisioned a disposable Windows 11
Enterprise x64 virtual machine, and reran the complete automated release suite
twice. Release approval is still prohibited because clean-machine evidence
collection could not be completed and none of the mandatory 25 packaged steps
was classified Pass in one uninterrupted clean-machine run.

## Distribution scope

No public or internal release approval is granted. The package remains a
repaired engineering release candidate and may be retained only for continued
qualification. Its unsigned state independently prevents public distribution.

## Candidate identification

| Field | Value |
| --- | --- |
| Version | `0.9.0-rc.4` |
| Branch | `master` |
| Product source revision | `516e655a2c6a94c1e7556b2f279ac457353626aa` |
| Sprint starting/reporting parent | `36744b3d0688909f788fd0272bac9d92f6364f64` |
| Final reporting revision | The Sprint 63 commit containing this report |
| Package | `PostgreManagementStudio-0.9.0-rc.4-win-x64.zip` |
| Package size | 62,383,059 bytes |
| Package SHA-256 | `A928BDB8600CD2E4787B0ADCF9AE0FEAE84FB5E01B07DE987073178568F01E7E` |
| Package files | 407 |
| Release manifest SHA-256 | `F049D3EAB743CCB9101B629018E8907D833761235776D270E57CDE8FC4F2C450` |
| Runtime | self-contained `win-x64`, `net9.0-windows` |
| Signature | unsigned; no signer or signing timestamp |
| Qualification date | 2026-07-30 |

## Source and package provenance

The pre-existing package was qualified without rebuilding. Its SHA-256 matched
the required Sprint 63 value. The package verifier passed all 407 files. The
manifest identifies version `0.9.0-rc.4`, source revision `516e655...`, clean
source state, self-contained `win-x64`, and unsigned status. No signing
certificate or signing tool was available on the qualification host.

## Qualification environment

A fresh Oracle VirtualBox 7.2.12 VM was created from the official Microsoft
Windows 11 Enterprise 25H2 evaluation x64 ISO. The ISO SHA-256
`A61ADEAB895EF5A4DB436E0A7011C92A2FF17BB0357F58B13BBC4062E535E7B9`
matched Microsoft's published English x64 evaluation hash. The guest booted to
Windows build `26100.ge_release.240331-1435`.

The VM used a new 80 GB disk, 8 GB RAM, EFI, TPM 2.0, NAT plus an isolated
host-only adapter, disabled clipboard/drag-and-drop, and a package-only
exchange folder. Repository source was not copied into the guest.

## Clean-machine validation

**Failed as a qualification gate.** Windows installed and reached the fresh
desktop, but VirtualBox Guest Additions did not expose guest execution or
file-transfer services. Consequently the prepared evidence collector could not
independently prove the guest's SDK/runtime inventory, standard-user privilege,
package hash, extraction inventory, antivirus result, or pre-launch
application-data state. These facts must not be inferred from the host.

No product defect was observed; this is an incomplete qualification
environment. Sprint 63 requires `Not approved for release` whenever
clean-machine validation cannot be completed.

## Artifact verification

Host-side verification passed:

- exact filename, size, and required package SHA-256;
- archive extraction and all 407 manifest entries;
- source revision and clean manifest state;
- deterministic self-contained runtime metadata;
- no source/test project files in the package;
- unsigned archive and executable recorded explicitly.

Clean-machine re-verification was not completed and is therefore not a Pass.

## Automated test results

Release run `66baaedaba` executed two complete passes with PostgreSQL and
large-dataset gates:

| Pass | Core | Results | PostgreSQL | Desktop | Integration | Total |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | 235 | 65 | 54 | 33 | 72 | 459 |
| 2 | 235 | 65 | 54 | 33 | 72 | 459 |
| **Combined** | **470** | **130** | **108** | **66** | **144** | **918** |

Result: **918 passed, 0 failed, 0 skipped**. Cleanup succeeded. Elapsed time
was approximately 84.2 seconds.

## Complete 25-step packaged workflow

**Fail: 0 Pass, 25 Fail (unexecuted).** The sequence did not start because its
mandatory clean-machine preconditions were not provable. No automated,
development-host, prior-sprint, partial, or service-level result was promoted
to a packaged-workflow Pass.

## Core experience scorecard

| Experience | Product evidence | Sprint 63 clean package | Release status |
| --- | --- | --- | --- |
| Object Explorer | Prior and automated tests green | Not executed | Blocked |
| Tree scripting | Prior and automated tests green | Not executed | Blocked |
| Alt+F1 describe | Prior and automated tests green | Not executed | Blocked |
| Import/export | Prior and automated tests green | Not executed | Blocked |

## Transfer round-trip results

The Sprint 63 CSV, TSV, invalid-input, new-table, complex-type, and
100,000-row fixtures were prepared against a disposable PostgreSQL 18.4
database. The clean packaged transfer matrix was not executed, so every
mandatory round-trip criterion remains failed/unproven.

## Transaction and cancellation results

Automated PostgreSQL coverage passed. Packaged atomic/batched cancellation,
connection recovery, and completion-summary/database-truth checks were not
executed on the clean machine and are not release evidence.

## Failure-recovery results

The invalid fixture and disposable failure targets were prepared. Source
removal, destination removal, permission denial, disk/path failure, retry, and
application-close-during-transfer scenarios were not executed in the candidate.

## Accessibility results

No focused packaged keyboard, focus order, accessible-name/role,
screen-reader, scaling, or non-colour warning review was completed. Prior
automated accessibility coverage is retained but cannot satisfy this gate.

## Performance and stability results

The two-pass host suite completed in about 84.2 seconds with large-dataset
gates enabled. Clean-machine startup, Object Explorer, Alt+F1, transfer
responsiveness, cancellation latency, and long-session resource measurements
were not captured.

## Security and dependency results

`dotnet list package --vulnerable --include-transitive` reported no vulnerable
resolved packages. Package inspection found no source files or supplied test
credentials. Evidence and fixtures do not commit plaintext passwords. The
clean-machine Microsoft Defender/SmartScreen result was not captured.

## Signing and signature verification

The package and executable are unsigned. No eligible code-signing certificate
or `signtool` was available. No signature verification or post-signing hash
exists. This blocks public release, but the incomplete clean workflow also
blocks internal-distribution approval.

## Defects discovered during qualification

`S63-RC01` (Blocker, qualification infrastructure): the fresh VirtualBox guest
did not expose reliable guest execution/file transfer after installation.
This prevented collection of mandatory SDK-free evidence and execution of the
packaged workflow. No product code was changed and no product Blocker or
Critical defect was discovered.

## Remaining known limitations

- clean-machine SDK-free state is not independently recorded;
- clean-machine package hash and manifest verification are absent;
- the complete 25-step packaged workflow has 25 unexecuted steps;
- transfer, cancellation, recovery, accessibility, and performance matrices
  remain incomplete;
- antivirus/SmartScreen behaviour is not recorded;
- artifacts are unsigned.

## Release conditions

Before any approval, repair or replace the clean-machine harness and repeat
Sprint 63 against the same exact package. Independently capture the no-SDK
environment and artifact hash, pass all 25 steps in one recorded run, complete
all focused matrices, and either sign the public artifacts or approve a formal
internal-only policy.

## Final recommendation

Retain `0.9.0-rc.4` as an unapproved engineering candidate. Do not publish or
internally distribute it as a qualified release. Resume at clean-machine
evidence collection; do not rebuild unless a product defect is found.

## Evidence index

- `docs/sprints/SPRINT_63_REPORT.md`
- `docs/audits/sprint-63/CLEAN_MACHINE_EVIDENCE.md`
- `docs/audits/sprint-63/PACKAGED_WORKFLOW.md`
- `docs/audits/sprint-63/DEFECT_REGISTER.md`
- `docs/audits/sprint-63/qualification-fixture.sql`
- `docs/audits/sprint-63/guest-qualification.ps1`
- `docs/audits/sprint-63/import-existing.csv`
- `docs/audits/sprint-63/import-new-table.csv`
- `docs/audits/sprint-63/import-invalid.csv`
- `docs/audits/sprint-63/roundtrip.tsv`
- `TestResults/66baaedaba/release-summary.json` (generated, ignored)
- `artifacts/release/release-manifest.json` (generated, ignored)
- `artifacts/release/PostgreManagementStudio-0.9.0-rc.4-win-x64.zip`
