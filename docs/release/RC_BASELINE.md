# Release-candidate baseline

## Repository and toolchain

| Item | Baseline |
|---|---|
| Branch / revision | `master` / `a84184e5eb11c8f923c29cb6b99d7649ac40042d` |
| Working tree | Clean except pre-existing untracked `STATE_OF_THE_NATION.md` |
| .NET SDK | 10.0.302; project targets .NET 9 Windows |
| UI framework | WPF, `net9.0-windows` |
| PostgreSQL client library | Npgsql 8.0.6 |
| Package model | Self-contained `win-x64` ZIP with controlled per-user PowerShell installer |
| Intended support | Windows 11 x64; PostgreSQL 18.4 qualified scope only |
| Release version | 0.9.0-rc.3 |

## Intended scope

The candidate is a compact PostgreSQL SQL editor and management tool covering
connections, Object Explorer, SQL execution/cancellation, result viewing and
export, file/recovery workflows, plans, search, restore review/execution,
maintenance, index inspection/reindex, schema comparison/preview, delimited
data transfer, and server activity/blocking/lock diagnostics with snapshots.

Settings editor, query history browser, query/database performance statistics,
role editor, data editing, PostgreSQL-to-PostgreSQL transfer, direct schema
synchronisation execution, and broad administration are deferred.

## Release gates

Block release for wrong-target operations, credential exposure, data loss,
repeatable primary-workflow crashes, installer failure, unsafe upgrade, hangs,
or silent partial completion. Public release also requires clean-machine,
multi-version PostgreSQL, signing/malware, and licence/attribution
qualification, which are not available in this workstation run.

User state is outside the install root. Settings/profile JSON is atomic and
corrupt-safe; saved passwords use Windows Credential Manager references.
