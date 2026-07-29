# Sprint 43 Security Review

Date: 2026-07-29

## Implementation summary

- Added Windows Credential Manager persistence with explicit opt-in, stable references, deletion, replacement, and no-secret profile serialization.
- Added Local, Development, Test, Staging, Production, and Custom classifications. The active classification and read-only state are continuously visible with server, database, role, and backend PID.
- Production profiles enforce `default_transaction_read_only=on` by default. The provider, not only UI state, rejects writes. Maintenance also refuses a read-only profile.
- Centralized redaction for rendered text, structured values, nested exceptions, connection URIs/properties, tokens, backup output, maintenance errors, and query diagnostics.
- Strengthened destructive requests with exact server/database/object identity, uncertain-session refusal, duplicate in-flight prevention, and typed production confirmation.
- Hardened settings with versioned validation, bounded values, enum checks, corrupt-file backup, atomic saves, and safe defaults.
- Added untrusted text/control/bidi bounds and safe filename derivation.

## SQL-generation audit

| Area | Value handling | Identifier handling | Fragment handling | Result |
|---|---|---|---|---|
| Role create/alter | Password and expiry parameters | shared quoter | fixed role-attribute mappings | Pass |
| Grants/revokes/default privileges | enums/fixed privilege names | shared quoter | fixed target-kind mappings; unsafe routine signatures rejected | Pass |
| Data import | Npgsql parameters/binary COPY | shared quoter | fixed strategy enum | Pass |
| Maintenance | timeout values use controlled settings | shared quoter | operation/options from enums | Pass |
| Metadata/search/activity/session actions | parameters | OID identity or shared quoter | fixed SQL constants | Pass |
| Schema/index script generation | definitions remain explicitly reviewed script content | shared quoter | action/kind enums | Pass with residual risk for externally sourced definitions |
| Explain | user SQL is kept as explicit user-authored SQL | not applicable | EXPLAIN options from typed model | Pass |
| Backup/restore | values are distinct process arguments | not SQL | fixed tool option mappings | Pass |

No generated value found in the audited paths is intentionally concatenated as a SQL literal. User-authored SQL and extracted object definitions remain visibly separate executable content.

## Identifier matrix

| Input | Expected |
|---|---|
| `Reporting User` | `"Reporting User"` |
| `a"b` | `"a""b"` |
| components `a.b`, `c"d`, `select` | `"a.b"."c""d"."select"` |
| semicolon/comment/newline/Unicode in a name | remains one quoted identifier; live test affects one object |
| null character | rejected before SQL generation |
| schema + object | each component quoted independently |
| routine signature with `;`, `--`, `/*`, quote, or controls | rejected |

## Destructive-operation inventory

Restore, maintenance, actual-plan execution, session cancellation/termination, schema changes, data replacement, and security changes use the shared guard. Requests require exact server and database and may also identify an object. An uncertain session cannot prompt or execute. Production requests receive a typed database confirmation. The async guard suppresses duplicate in-flight execution for the same kind and exact target. Existing backup/restore tokens additionally bind the confirmation to the immutable plan.

## File, process, import/export, and clipboard review

- SQL/settings/profile/export/backup paths use `Path` APIs. Settings, profiles, result export, and backup output use same-directory temporary files followed by move/commit and clean up on cancellation/failure.
- Database-originated names pass through `SafeFileName` when used to derive filenames; traversal separators and invalid characters become inert.
- Backup password files are restricted, never passed as password command-line arguments, and deleted in `finally`. Processes use `UseShellExecute=false` and `ArgumentList`; output is bounded and redacted; cancellation kills the owned tree.
- Import inserts are parameterized and large inputs stream. Export uses atomic output.
- Spreadsheet-safe CSV prefixes string values beginning with `=`, `+`, `-`, or `@`; it defaults on. Raw CSV turns this off and preserves exact values, and should be used only when fidelity is required and the file will not be opened by formula-evaluating software.
- Clipboard serialization returns selected visible data only and retains no application clipboard cache. Binary values use the established result formatter representation.

## Logging and history privacy policy

`SensitiveDataRedactor` is the only underlying credential redaction policy; legacy query and backup facades delegate to it. Redaction occurs before diagnostics/process output is retained. Ordinary errors contain classified summaries, not stack traces or connection strings. Detailed diagnostics remain explicit and redacted.

Query-history policy is versioned in settings: persistence can be disabled, private-session default can suppress persistence, default text mode is fingerprint plus bounded preview, retention defaults to 30 days and 100 entries per query, and both are bounded. Results and parameter values are not part of query history. In-memory history services expose clear operations.

## Dependency and supply-chain report

Review command: `dotnet list PostgreManagementStudio.sln package --include-transitive` and `--vulnerable --include-transitive`.

- Runtime direct packages: Npgsql 8.0.6, Microsoft.Extensions.DependencyInjection 9.0.0, Microsoft.Extensions.Logging.Abstractions 9.0.0.
- Development/test direct packages: Microsoft.NET.Test.Sdk 17.12.0, xUnit 2.9.2, xunit.runner.visualstudio 2.8.2, coverlet.collector 6.0.2.
- Notable test transitives: Microsoft.CodeCoverage/TestPlatform 17.12.0, Newtonsoft.Json 13.0.1, System.Reflection.Metadata 1.6.0, xUnit components.
- Advisory result on 2026-07-29: no vulnerable direct or transitive package reported for any solution project.
- Package source: only `https://api.nuget.org/v3/index.json`.
- Versions are centrally pinned in `Directory.Packages.props`; no major upgrades were made. `packages.lock.json` is not currently enabled, so restore still depends on NuGet source integrity. Security updates require advisory review, a constrained central version change, Release restore/build, and the complete isolated PostgreSQL regression suite.
- No native binaries are distributed by this repository. PostgreSQL utilities are discovered local installations and their path/version are surfaced by the existing discovery service.

## Verification summary

Automated coverage includes credential round-trip/delete, no-secret serialization, credential duplication opt-in, protected advanced options, provider-enforced production read-only, hostile live identifiers, authentication redaction, structured/nested redaction, corrupt settings/profile recovery, identifier quoting/signature rejection, immutable editor execution, session generation/cancellation, shell-free process arguments, temporary password cleanup, CSV formula modes, atomic export, and destructive exact-target behavior.

Final isolated Release run `d928c49c1f`: 379 passed, 0 failed, 0 skipped (187 Core, 54 Postgres, 63 Results, 17 Desktop, 58 live PostgreSQL integration/performance). Build: 0 warnings, 0 errors. The large dataset, external PostgreSQL utilities, read-only/restricted roles, and cleanup paths were enabled.

`dotnet format --verify-no-changes` passes for all newly introduced Sprint 43 C# files, and `git diff --check` passes for the complete change. A solution-wide formatting check still reports inherited whitespace debt in pre-existing compact/one-line files outside this sprint; this sprint deliberately did not create a broad unrelated formatting rewrite.

Manual scenario evidence:

| Scenario | Result | Evidence |
|---|---|---|
| A credential persistence | Pass | Windows Credential Manager round-trip/delete test plus profile restart-equivalent reload and plaintext scan |
| B hostile identifiers | Pass | Isolated PostgreSQL live create/catalog lookup/drop test with quote, semicolon, comment, newline, Unicode |
| C wrong target | Pass | Existing rapid context-switch race tests; guard exact-target and production typed-confirm tests; visible status inspection |
| D logging redaction | Pass | Seeded password/token tests across connection failure, nested exceptions, process/query facades; repository/test output scan |
| E backup/restore invocation | Pass | Release backup/restore integration with PostgreSQL under `Program Files`, distinct arguments, pgpass cleanup |
| F malicious metadata | Pass | Live hostile metadata plus bounded control/bidi UI text tests and WPF plain-text rendering inspection |
| G export safety | Pass | Raw versus spreadsheet-safe formula-prefix tests; export option is explicit and defaults safe |

## Residual risks and completion confirmations

- Credential Manager cannot defend against a compromised current Windows account or process-memory inspection.
- Read-only mode is defense in depth, not a substitute for a read-only PostgreSQL role.
- Arbitrary user SQL, imported scripts, and schema definitions can be dangerous when the user explicitly executes them.
- Raw CSV can be unsafe in spreadsheet software; the user can intentionally select it for fidelity.
- Package restore is source-pinned and version-pinned but not lock-file locked.

Confirmed by implementation and tests:

1. Normal application configuration and profile files contain no plaintext credentials.
2. Audited generated SQL uses parameters for values and the shared component-wise quoter for identifiers.
3. Database-originated content is rendered as bounded plain text, never markup or a shell command.
4. Query commands retain immutable editor/session context and cannot redirect to a newly active global connection.
