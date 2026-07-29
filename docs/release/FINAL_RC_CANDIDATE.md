# Final release-candidate identity

| Field | Value |
|---|---|
| Candidate | PostgreManagementStudio 0.9.0-rc.3 |
| Frozen source | `21e3ab2a3ee11054222ca1c9bd72b223b2a4fd0b` on `master` |
| Build | Release, `net9.0-windows`, self-contained `win-x64` |
| Package | `artifacts/release-sprint57/PostgreManagementStudio-0.9.0-rc.3-win-x64.zip` |
| SHA-256 | `e6244a56b6a654123cd3ae7a7318e2bc28e978b35981b709f0149a564d8829aa` |
| Build date | 2026-07-29 (Africa/Johannesburg) |
| Package contents | 407 archive files; 401 application files in manifest |
| Windows claim | Windows 11 x64 |
| PostgreSQL claim | PostgreSQL 18.4 only; Npgsql 8.0.6 |
| Installer | Offline ZIP with controlled per-user PowerShell installer |
| Prerequisites | No .NET runtime/SDK; `pg_dump`, `pg_restore`, and `psql` for backup/restore |
| Signing | Unsigned internal candidate |

Included scope is the compact PostgreSQL workflow documented in
`RC_RELEASE_NOTES.md`: connection management, browsing, SQL, bounded results
and export, plans, search, restore, maintenance, index reindex, schema
comparison/preview, delimited-file transfer, and activity/blocking/lock views.
Settings/history/statistics editors, data editing, roles, PostgreSQL-to-
PostgreSQL transfer, and direct synchronisation execution are deferred.

This identity is immutable for Sprint 57 evidence. Any product or package change
invalidates package-dependent evidence and requires a new candidate identity.
