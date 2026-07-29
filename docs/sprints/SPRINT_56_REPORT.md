# Sprint 56 — Release Candidate Hardening, Regression, Packaging, and Upgrade Validation

## Outcome

Sprint 56 produced a reproducible internal release-candidate qualification
baseline for PostgreManagementStudio 0.9.0-rc.3. The candidate is conditionally
accepted for continued internal testing; it is not approved for public release.

## Baseline and scope

- Revision: `a84184e5eb11c8f923c29cb6b99d7649ac40042d`.
- Toolchain: .NET SDK 10.0.302, WPF `net9.0-windows`, Npgsql 8.0.6.
- Package: self-contained Windows 11 x64 ZIP, PostgreSQL 18.4 qualified scope.
- Covered workflows: connections, Object Explorer, SQL execution/cancellation,
  results/export, files/recovery, plans/search, restore, maintenance, index
  inspection/reindex, schema comparison/preview, delimited transfer, and
  activity/blocking/lock diagnostics.
- Deferred: settings editor, query history, query/database statistics, role
  editor, data editing, PostgreSQL-to-PostgreSQL transfer, direct schema sync,
  and broad administration.

## Build, regression, and packaging

`scripts/release/build-release.ps1` completed a Release build with 0 warnings
and 0 errors and ran the solution baseline: 333 passed, 60 skipped, 0 failed.
The skips are PostgreSQL integration tests without configured credentials in
this qualification run. `verify-package.ps1` passed for 407 archive files
(401 application files in the manifest). The package SHA-256 is
`22fa5b41a1952d90d5514d95efcc95b1b657169d378d6bf083f7ae4dc58b19ad`.

`test-installer.ps1` passed install, repair, preservation of a user marker,
and uninstall while preserving user data. Existing controlled PostgreSQL and
desktop evidence remains referenced; a fresh clean-machine and multi-version
campaign was not available in this run.

## Upgrade, safety, and performance review

Application state remains outside the install root under `%LOCALAPPDATA%`,
with saved passwords referenced through Windows Credential Manager. Repair and
uninstall preserve that state; no destructive migration or automatic recovery
execution was introduced. The package has no debug/test/coverage artifacts or
seeded credentials. Build and test execution remained bounded, and the
installer cleanup test passed. Cold-start, long-duration monitoring, large
schemas, million-row transfers, and multi-DPI measurements remain open.

## Defects and exit decision

No blocker or critical product-code defect was found. Open qualification gates
are clean-machine/standard-user/display testing, real prior-version upgrade,
PostgreSQL 14–17 compatibility, package signing/malware scanning, and final
licence approval. The exit decision is **conditional internal RC**. These gates
are recorded in `docs/release/RC_DEFECT_REGISTER.md` and
`docs/release/RC_EXIT_REVIEW.md`.

## Evidence and follow-up

See the Sprint 56 release documents in `docs/release/`, especially
`RC_TEST_MATRIX.md`, `RC_SMOKE_TEST.md`, `UPGRADE_AND_MIGRATION_TESTS.md`, and
`POSTGRESQL_COMPATIBILITY_MATRIX.md`. Sprint 57 should run the external
qualification campaign and close release gates before adding features.
