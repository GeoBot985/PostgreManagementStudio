# Development environment

Verified 2026-07-28: Git 2.51.0.windows.1; .NET SDK 9.0.315; Windows 10. PostgreSQL/`psql` not installed or discoverable, and no PostgreSQL service was found.

Install Git, the current .NET LTS SDK, Visual Studio Community with **.NET desktop development**, and PostgreSQL for Windows. Verify with `git --version`, `dotnet --info`, `psql --version`, and `Get-Service *postgres*`.

Set `PMS_CONNECTION_STRING` locally; never store the password in this repository. Manual follow-up required: install PostgreSQL and verify the query.
