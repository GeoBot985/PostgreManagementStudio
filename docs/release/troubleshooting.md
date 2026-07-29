# Release-candidate troubleshooting

## Installation or launch

Verify the package SHA-256 in `FINAL_RC_CANDIDATE.md`, extract the ZIP fully,
and run `verify-package.ps1` before `install.ps1`. The self-contained package
does not require the .NET SDK or Visual Studio. Close a running application
before repair or upgrade. Installer logs are under
`%LOCALAPPDATA%\PostgreManagementStudio\logs`.

## Connection and PostgreSQL tools

Confirm server, port, database and role in the connection profile. Do not add a
password to a log or support report. Backup/restore additionally needs a
compatible local `psql`, `pg_dump` and `pg_restore`. The qualified scope is
Windows 11 x64 and PostgreSQL 18.4 only.

## Recovery and support data

Corrupt optional settings are backed up before safe defaults load; recovery SQL
does not execute automatically. Normal uninstall preserves user data. Provide
version, package hash, Windows/PostgreSQL version and redacted logs when
reporting a problem. Do not provide passwords, full connection strings, private
keys or unreviewed result data.

## Current release conditions

This is an internal RC. Clean-machine/DPI, stateful profile upgrade, signing,
malware scanning, licensing and broader PostgreSQL/remote-security scenarios
remain conditions described in `FINAL_RELEASE_DECISION.md`.
