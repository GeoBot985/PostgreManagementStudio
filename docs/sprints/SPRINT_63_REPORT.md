# Sprint 63 completion report

## Decision

**Not approved for release**

This is a completed qualification decision, not a claim that the release
criteria passed. Sprint 63 requires this decision when clean-machine validation
cannot be completed or any packaged step remains unexecuted.

## Environment

A disposable Oracle VirtualBox 7.2.12 Windows 11 Enterprise 25H2 x64 VM was
created from a Microsoft evaluation ISO whose SHA-256 matched Microsoft's
published value. The guest reached the fresh desktop. Repository source was not
copied; shared clipboard and drag-and-drop were disabled.

## Artifact identity

| Field | Value |
| --- | --- |
| Version | `0.9.0-rc.4` |
| Product source | `516e655a2c6a94c1e7556b2f279ac457353626aa` |
| Package | `PostgreManagementStudio-0.9.0-rc.4-win-x64.zip` |
| Size | 62,383,059 bytes |
| SHA-256 | `A928BDB8600CD2E4787B0ADCF9AE0FEAE84FB5E01B07DE987073178568F01E7E` |
| File count | 407 |
| Manifest SHA-256 | `F049D3EAB743CCB9101B629018E8907D833761235776D270E57CDE8FC4F2C450` |
| Signing | Unsigned |

The existing artifact was not rebuilt.

## Tests performed and results

- Host package SHA-256: Pass.
- Host package verifier, 407 files: Pass.
- Manifest/source/dirty/runtime identity: Pass.
- Microsoft ISO provenance: Pass.
- Fresh Windows installation and boot: Pass.
- Clean guest SDK proof: Fail, not captured.
- Clean guest independent package verification: Fail, not captured.
- Complete packaged workflow: Fail, 0 Pass / 25 unexecuted.
- Focused transfer/cancellation/recovery/accessibility/performance matrices:
  Fail, not executed.
- Automated release suite twice: 918 passed, 0 failed, 0 skipped.
- NuGet resolved-package vulnerability review: no vulnerable packages found.
- Signing/signature verification: Fail, artifact unsigned.

## Workflow results

The 25-step workflow did not start because VirtualBox Guest Additions failed to
provide guest execution/file transfer, leaving the preconditions unprovable.
The complete step-by-step Fail record is in
`docs/audits/sprint-63/PACKAGED_WORKFLOW.md`.

## PostgreSQL fixtures

A disposable PostgreSQL 18.4 database and dedicated role were created. The
fixture contains ordinary, identity, generated, key, JSONB, array, enum,
domain, UUID, numeric, timestamp, multiline, Unicode, view, materialized view,
sequence, function, procedure, index, trigger, 305 large-schema tables, import
targets, a deletion target, and 100,000 transfer rows. CSV/TSV/invalid fixtures
are committed without credentials.

## Signing status and distribution scope

No signing certificate or signing tool was available. Public signing was not
performed. Internal-only approval is also denied because clean-machine and
workflow requirements did not pass.

Distribution scope: **none approved**. Engineering may retain the exact hash
only as an unapproved candidate for continued qualification.

## Known limitations and defects

The sole newly recorded issue is `S63-RC01`, a qualification-infrastructure
Blocker. No product defect was found and no fix/rebuild was made. Mandatory
clean-machine, transfer, cancellation, failure, accessibility, performance,
antivirus, and signing evidence remains absent.

## Final recommendation

Repair the disposable guest integration or provision another clean Windows 11
machine. Resume with independent no-SDK and hash evidence, then execute every
packaged step and focused matrix against this unchanged artifact. Do not
publish or internally distribute the candidate until those gates pass.
