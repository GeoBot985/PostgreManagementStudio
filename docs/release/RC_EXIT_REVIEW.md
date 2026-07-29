# Release-candidate exit review

## Evidence

- Source revision: `a84184e5eb11c8f923c29cb6b99d7649ac40042d`.
- Release build: 0 warnings, 0 errors.
- Automated tests: 333 passed in the current solution baseline; 60 PostgreSQL
  integration tests skipped without configured integration credentials.
- Package: self-contained `win-x64`, 407 archive files, hash recorded in release
  notes and manifest.
- Package scan: pass; no debug/test/coverage artefacts or seeded credentials.
- Installer lifecycle: pass for install, repair, uninstall and user-state
  preservation in an isolated temporary profile.

## Dimension review

| Dimension | Assessment | Reason |
|---|---|---|
| Functional completeness | Conditional pass | Current documented scope is reachable; deferred features are not claimed |
| Reliability | Conditional pass | Automated baseline passes; clean-machine and live DB rerun remain |
| Data safety | Pass for tested scope | Target-aware destructive workflows and package state policy have evidence |
| Credential safety | Pass | Settings/credential policy and package/security scans pass |
| Responsiveness | Conditional pass | Bounded/cancellable architecture; full timing campaign not run |
| Resource stability | Conditional pass | Existing bounded tests; extended clean environment campaign pending |
| Installation | Conditional pass | Package and isolated lifecycle pass; clean supported machine pending |
| Upgrade | Not tested | No prior-version install was available for this run |
| Compatibility | Conditional pass | PostgreSQL 18.4 prior evidence; other versions unverified |
| Recovery | Pass | Corrupt optional settings fallback and SQL recovery tests pass |
| Documentation | Conditional pass | RC documents now state exact scope and gates |
| Test evidence | Conditional pass | Automated/package evidence complete; manual/clean-machine evidence incomplete |

## Decision

**Conditional internal release candidate; no public release approval.** No
blocker or critical product-code defect was found, but the untested upgrade,
clean-machine Windows, broader PostgreSQL compatibility, signing/malware and
licence gates prevent an overall release-candidate pass. These are explicit
qualification gates, not hidden as passing tests.

## Recommended Sprint 57

Run the external qualification campaign: clean Windows installation and
upgrade from the prior package, Windows display/permissions matrix, PostgreSQL
14–18.4 compatibility where supported, signed-package scanning, and final
licence/third-party notice approval. Do not add new product features until
those gates are closed.
