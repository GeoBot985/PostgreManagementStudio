# PostgreManagementStudio 0.9.0-rc.3 release-candidate notes

## Package

- Windows 11 x64 self-contained offline ZIP.
- Package: `PostgreManagementStudio-0.9.0-rc.3-win-x64.zip`.
- Sprint 56 qualification hash: `22fa5b41a1952d90d5514d95efcc95b1b657169d378d6bf083f7ae4dc58b19ad`.
- Npgsql: 8.0.6. PostgreSQL support claim: 18.4 only.
- Package is unsigned internal-candidate material; signing and malware scan are
  required before distribution.

## Included workflows

SQL editing/recovery, PostgreSQL connection and Object Explorer, query execution
and cancellation, results/export, plans, search, restore, maintenance, index
inspection/reindex, schema comparison and synchronisation preview, CSV transfer,
and activity/blocking/lock monitoring with privacy-aware snapshots.

## Important limitations

No editable data grid, query history browser, settings editor, query/database
performance statistics workspace, role editor, PostgreSQL-to-PostgreSQL
transfer, direct schema synchronisation execution, or broad PostgreSQL-version
claim is included. Backup/restore requires compatible PostgreSQL client tools.

## Upgrade and privacy

Repair/upgrade preserves external user state. Passwords use Windows Credential
Manager references, not ordinary settings JSON. Logs and diagnostic snapshots
are redacted/bounded; query text is omitted from activity snapshots by default.

## Qualification status

Package verification and isolated install/repair/uninstall preservation passed.
Public release remains blocked pending clean-machine/display, upgrade,
multi-version PostgreSQL, signing/malware and licence/attribution qualification.
