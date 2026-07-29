# Release-candidate smoke test

Run on a disposable Windows 11 x64 profile with a disposable PostgreSQL 18.4
database. Do not use production data or credentials. Record each step as
Pass/Fail/Blocked and attach the package hash.

1. Extract the approved ZIP and run `verify-package.ps1`.
2. Run `install.ps1` and launch PostgreManagementStudio.
3. Create and test a PostgreSQL connection; verify the target identity.
4. Connect and expand Object Explorer.
5. Open a query, execute `SELECT 1`, inspect the result, and export it.
6. Start a disposable long-running query and cancel it.
7. Open an estimated plan and verify the operation remains review-only.
8. Search for a disposable object.
9. Open Performance Dashboard; refresh Activity, Blocking and Locks.
10. On disposable sessions only, test Cancel query and Terminate session
    confirmations and verify the target is revalidated.
11. Run a safe maintenance operation and inspect the generated SQL/status.
12. Open Index Management and inspect/reindex a disposable index if permitted.
13. Compare two disposable schemas and preview (do not execute) synchronisation.
14. Import a small CSV into a disposable table; export the result/file.
15. Save a privacy-default diagnostic snapshot and verify no connection string
    or password appears.
16. Close the workspace, disconnect, restart the application, and verify safe
    state only; no destructive operation resumes.
17. Run `install.ps1 -Repair`, then `uninstall.ps1`; verify application files
    are removed and user-state policy is honoured.
