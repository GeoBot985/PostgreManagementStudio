# Release-candidate defect register

No blocker or critical product-code defect was discovered in the Sprint 56
workstation qualification. The following open items are release qualification
gates, not silently downgraded product defects.

| ID | Title | Severity | Status | Release decision |
|---|---|---|---|---|
| RC-001 | Clean-machine and standard-user/display campaign unavailable | MAJOR qualification gap | Open | No public release until Windows 11 clean-machine, DPI and permissions evidence exists |
| RC-002 | Prior-version upgrade migration not independently exercised in Sprint 56 | MAJOR qualification gap | Open | Conditional internal RC only; perform upgrade campaign before public release |
| RC-003 | PostgreSQL versions other than 18.4 not qualified | MAJOR qualification gap | Open | Keep support claim narrowed to PostgreSQL 18.4 |
| RC-004 | Package is unsigned and malware/SmartScreen scan is not available here | MAJOR release gate | Open | Sign and scan frozen package before distribution |
| RC-005 | Settings editor, query history and query/database statistics remain deferred | Accepted scope limitation | Accepted | Do not advertise these capabilities; no functional release claim is false |
| RC-006 | Release summary undercounted xUnit skipped tests | MINOR evidence defect | Closed Sprint 57 | `test-release.ps1` now counts `NotExecuted` result records; full large-dataset run had 393 pass / 0 skip |

Sprint 57 disposition: RC-001 remains open; RC-002 is reduced by a passing
prior-package installer upgrade but stateful profile migration remains open;
RC-003 is closed only for PostgreSQL 18.4 and remains an explicit limitation
for other versions; RC-004 remains open; RC-005 remains accepted.

No defect was found involving wrong-target destructive operations, credential
exposure, unsafe install/uninstall state handling, or silent partial completion.
