# Sprint 57 — Final Release-Candidate Validation and State-of-the-Nation Review

## Outcome

The frozen `0.9.0-rc.3` package is approved as an **internal release candidate
with documented conditions**. It is not approved for public distribution.

## Candidate, repository, and environments

- Branch/revision: `master` / `21e3ab2a3ee11054222ca1c9bd72b223b2a4fd0b`.
- Package/hash: `PostgreManagementStudio-0.9.0-rc.3-win-x64.zip` /
  `e6244a56b6a654123cd3ae7a7318e2bc28e978b35981b709f0149a564d8829aa`.
- Build: Release, `net9.0-windows`, self-contained `win-x64`, 0 warnings/errors.
- Tested host: Windows 11 x64, local disposable PostgreSQL 18.4, PostgreSQL 18 utilities.
- The unrelated untracked `STATE_OF_THE_NATION.md` was preserved.

## Validation results

- Sprint 56 conditions were reconciled in `RC_EXIT_REVIEW.md`.
- Final PostgreSQL run: 393 passed, 0 failed, 0 skipped; disposable database,
  roles and large data were cleaned up.
- Packaging: manifest revision/version/hash matched; 407 archive files verified.
- Installer: install, repair, uninstall and external user-state preservation passed.
- Upgrade: prior Sprint 56 package installed, then this candidate installed into
  the same isolated root; pass. Stateful legacy-profile migration remains open.
- Packaged desktop UI: launched to the disconnected shell with menus, toolbars,
  Object Explorer and query workspace; exited normally.
- Full authenticated UI acceptance could not be automated because credential
  entry is intentionally not automated; its integration evidence is recorded,
  and clean-machine UI acceptance remains a condition.

## Findings

No blocker/critical defect was found or reopened. Wrong-target, destructive,
data-integrity, credential/privacy, recovery and package checks passed within
the disposable PostgreSQL 18.4 scope. RC-006 was fixed: `test-release.ps1`
now correctly counts xUnit `NotExecuted` results in its JSON evidence. The
full large-dataset run then had zero skips.

Open major qualification conditions are unsigned package/scanning/licensing,
clean standard-user/DPI acceptance, stateful old-profile upgrade/recovery, and
broader PostgreSQL/remote-security qualification. Deferred functionality is
not advertised. See the final review documents in `docs/release/`.

## Quality and decision

Scores and rationale are in `FINAL_STATE_OF_THE_NATION.md`. The final decision
is `APPROVE_WITH_DOCUMENTED_CONDITIONS`: internal RC only, subject to the
concrete public-release conditions in `FINAL_RELEASE_DECISION.md`. No Sprint 58
is proposed unless that external campaign exposes a bounded remediation need.
