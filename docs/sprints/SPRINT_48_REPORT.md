# Sprint 48 - Release Candidate Reliability and Defect Burn-Down

## Outcome

Sprint 48 is complete. The sprint remained a hardening sprint: no new product
workspace or broad UI redesign was introduced. The work focused on exception
boundaries, cancellation presentation, cleanup reliability, release identity,
and correcting an unsupported generated compatibility claim.

The final source tree builds successfully in Release with zero warnings and
zero errors. The isolated PostgreSQL qualification ran three complete
iterations against the local PostgreSQL 18.4 test database, including the large
dataset fixture:

| Result | Value |
|---|---:|
| Tests per iteration | 384 |
| Iterations | 3 |
| Passed | 1,152 |
| Failed | 0 |
| Skipped | 0 |
| Database/resource cleanup | Passed |
| Qualification run | `TestResults/df038c8466` |

This improves the release candidate but does not establish public-release
readiness by itself. Remote secure-authentication, clean-VM, signing/malware,
legal and broader PostgreSQL-version qualification remain outside this sprint.

## Repository state inspected

- Branch: `master`
- Starting revision: `b90f57bf3304b70f1ca5b9f2e7966c1007e04cfc`
- Working tree initially contained only the prior untracked
  `STATE_OF_THE_NATION.md` report.
- Five production projects and five test projects were inspected.
- Framework remains .NET 9 WPF with Npgsql 8.0.6 and Windows `win-x64`
  self-contained packaging.
- Existing release, hardening, test, security, and packaging documentation was
  compared with the implementation.

## Audit areas covered

The audit reviewed and exercised the primary workflows and boundaries requested
for the sprint:

- startup, shell construction and shutdown;
- connection dialog profile loading, connect/test paths, saved-password
  deletion and pending-session cleanup;
- Object Explorer refresh/expansion and connection-generation changes;
- query tab creation, switching, execution, cancellation and disposal;
- result search, page navigation, copy, export and error presentation;
- file open/save error paths;
- completion cancellation and stale-result handling;
- routed menu, shortcut and toolbar command state;
- multiple query documents and connection ownership;
- settings/recovery composition;
- build, analyzer, unit, desktop and live PostgreSQL integration coverage;
- release version and generated PostgreSQL compatibility metadata.

The exact packaged executable was previously inspected for the traditional
shell and clean shutdown. Sprint 48 additionally ran the complete live
qualification runner using the local disposable PostgreSQL environment.

## Defects discovered

| ID | Severity | Defect | Root cause | Disposition |
|---|---|---|---|---|
| S48-001 | High | Several WPF async event boundaries could allow exceptions from startup recovery, tab-selection metadata refresh, or connection-state UI refresh to escape the event handler. | `async void` event handlers called asynchronous workflows directly without one shared observation boundary. | Fixed and covered by the existing shell lifecycle/composition tests plus final live qualification. |
| S48-002 | High | Connection-profile loading, saved-password deletion, and pending-session disposal had unhandled failure paths. | `Loaded`, password-delete and `Closed` handlers assumed profile/credential operations could not fail. | Fixed with redacted user messages, safe button-state restoration and trace-only cleanup diagnostics. |
| S48-003 | Medium | Result page/search events could surface exceptions through WPF event dispatch and leave the user without a useful state message. | Page navigation and search handlers awaited operations without a local cancellation/failure boundary. | Fixed; cancellation is now silent/normal and failures select Messages with a redacted operation-specific message. |
| S48-004 | Medium | File open/save, export and clipboard failures exposed raw exception text and did not consistently restore the Messages/status surface. | UI handlers used raw `ex.Message` or had no clipboard boundary. | Fixed using centralized `DesktopErrorPresentation` and explicit cancellation handling. |
| S48-005 | Medium | About displayed `0.9.0` while the release candidate identity included the RC suffix. | `AssemblyVersionText()` used only numeric assembly version. | Fixed to use informational version without build metadata; version bumped to `0.9.0-rc.3`. |
| S48-006 | High release gate | Release packaging generated an unverified `PostgreSQL 14+` claim. | `build-release.ps1` hard-coded the broad claim alongside the PG18.4 result. | Fixed by generating the truthful qualified claim `PostgreSQL 18.4`. Older versions remain future qualification work. |

No wrong-target query, wrong-target destructive operation, credential exposure,
transaction-success misreport, installer data-loss defect, ordinary-use crash,
or unrecoverable tested connection-loss defect was discovered in this sprint.

## Defects fixed

### Error and cancellation boundaries

`MainWindow` now observes startup restoration, tab-selection refresh and
connection-state UI refresh failures. The UI is updated after startup failure so
command state is recalculated rather than left in a partially busy state.

`ConnectionDialog` now handles profile-load failure, saved-password deletion
failure and pending-session cleanup failure. Password deletion disables the
button while in flight and restores it if the operation fails.

`QueryTabView` now handles result-search, page-navigation, completion, file
open/save, export, clipboard and unload cleanup failures. Cancellation is
handled separately from ordinary failure. Errors are shown in the Messages or
status surface with an operation label and redacted, bounded text.

### Central safe presentation

`DesktopErrorPresentation.Failure` centralizes the rule that:

- cancellation is presented as a normal cancelled operation;
- secrets are redacted before display;
- empty exception messages receive a useful fallback;
- untrusted messages are bounded before entering the WPF surface.

The new desktop test verifies both secret redaction and cancellation wording.

### Release identity and support claim

`Directory.Build.props` now advances the candidate from `rc.2` to `rc.3`.
The About dialog reads the informational version and strips only build metadata,
so the visible version is `0.9.0-rc.3`. `build-release.ps1` now emits only the
qualified PostgreSQL 18.4 support claim rather than claiming PostgreSQL 14+.

## Files changed

- `Directory.Build.props`
- `scripts/release/build-release.ps1`
- `src/PostgreManagementStudio.Desktop/DesktopErrorPresentation.cs`
- `src/PostgreManagementStudio.Desktop/MainWindow.xaml.cs`
- `src/PostgreManagementStudio.Desktop/ConnectionDialog.xaml.cs`
- `src/PostgreManagementStudio.Desktop/QueryTabView.xaml.cs`
- `tests/PostgreManagementStudio.Desktop.Tests/ShellWorkflowTests.cs`
- `docs/sprints/SPRINT_48_REPORT.md`

The existing untracked `STATE_OF_THE_NATION.md` was preserved as a prior audit
artefact and is not part of the Sprint 48 source change.

## Tests added or updated

Added one deterministic desktop unit test:

`DesktopErrorPresentation_RedactsSecretsAndHandlesCancellation`

The test uses a controlled exception and does not depend on timing, UI delays,
or a live database. Existing desktop shell, composition, recovery and lifecycle
tests were rerun, and the complete live integration suite was rerun three times.

## Build and test results

### Release build

```powershell
dotnet build PostgreManagementStudio.sln --configuration Release --no-restore
```

Result: passed, 0 warnings, 0 errors.

### Normal solution tests

```powershell
dotnet test PostgreManagementStudio.sln --configuration Release --no-build --no-restore
```

Result after the code changes: 325 passed, 0 failed, 60 explicitly skipped.
The skips are environment-gated PostgreSQL tests when the connection variables
are not provided; they are not false passes.

### Live release qualification

```powershell
$env:PMS_ADMIN_CONNECTION_STRING = 'Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=<local-test-password>'
.\scripts\test-release.ps1 -AdminConnectionString $env:PMS_ADMIN_CONNECTION_STRING -Repeat 3 -SkipCoverage -IncludeLargeDataset
```

The actual local test password was supplied through the process environment and
is intentionally not recorded here. The runner created and removed disposable
databases and roles successfully. PostgreSQL reported version 18.4.

### Static quality

- `dotnet format analyzers --verify-no-changes --no-restore --verbosity quiet`:
  passed.
- `git diff --check`: passed; Git reported only the repository's normal
  LF-to-CRLF normalization notices.
- Full whitespace `dotnet format --verify-no-changes` remains an existing
  repository hygiene issue and was not expanded into this sprint.

## Known unresolved defects

The following are deliberately not disguised as complete:

- Remote TLS/password and client-certificate qualification is not available in
  the current environment.
- PostgreSQL versions other than 18.4 remain unverified and are no longer
  claimed by the generated package manifest.
- Clean-VM, Unicode-profile, non-English locale, mixed-DPI and multi-monitor
  packaging campaigns remain open.
- The candidate is unsigned and has not received a signed-package malware or
  SmartScreen campaign.
- The public licence remains a project-owner/legal decision outside code.
- Application diagnostics remain primarily Trace-based; installer logs are
  persistent but there is no complete application support-bundle workflow.
- Recent Files, Options, syntax highlighting, current-statement execution,
  formatting, bookmarks, editable data, rich administration workspaces,
  docking, multi-window and advanced monitoring remain outside the supported
  release surface.
- Full whitespace formatting verification still reports pre-existing changes.

## Release-candidate risks remaining

The Sprint 48 fixes reduce runtime error-boundary and release-identity risk but
do not change the previous public-release decision. The committed `rc.3`
candidate was rebuilt and verified after the final code commit. It is still
subject to the clean-environment/signing/legal gates before public distribution.

The verified package is
`PostgreManagementStudio-0.9.0-rc.3-win-x64.zip`; the immutable archive
contains the generated manifest and checksum for the final commit.

The primary remaining risk is qualification scope, not a known local database
correctness failure. The current evidence supports a qualified personal tool
for Windows 11 x64 and PostgreSQL 18.4, with the documented reduced UI surface.
It does not support a broad PostgreSQL-version or remote-secure-connectivity
claim.

## Recommended focus for Sprint 49

Sprint 49 should be a small external qualification and release-governance
sprint, not a feature sprint:

1. Re-run the committed `0.9.0-rc.3` package verification after any signing
   or packaging change, including its manifest, checksum, version display and
   support claims.
2. Run the clean Windows 11 standard-user install/repair/upgrade/uninstall
   matrix, including Unicode path, locale and display-scaling cases intended
   for support.
3. Qualify the exact remote TLS/password/client-certificate modes that will be
   advertised, or remove them from the release documentation.
4. Obtain final licence/third-party approval, sign the frozen artefact and run
   malware/SmartScreen validation.
5. Rerun the three-pass release suite and exact-package smoke test after
   signing.

Explicit exclusions: new database workspaces, editable grids, docking/theme
redesign, architecture rewrite and broad feature parity work.

## Sprint 48 conclusion

Sprint 48 meets its definition of done for the code hardening scope. The
solution is warning-free, all live qualification iterations pass, changed
failure paths are covered by deterministic tests, commands remain on the
existing routed shell, and no credentials are added to source, logs or report
evidence.

The application is improved and the support claim is now truthful for the
generated package. It is still an internal release candidate until the
remaining external release gates are closed.
