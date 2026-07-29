# Known limitations

| Feature | Exact limitation | Impact / workaround | Disposition | Blocks public release? |
|---|---|---|---|---|
| Windows qualification | Only Windows 11 x64 is claimed; clean VM, standard-user, DPI and multi-monitor campaign is pending | Use the qualified environment; run smoke test on target fleet | Qualification gate | Yes |
| PostgreSQL versions | Only PostgreSQL 18.4 has prior isolated evidence | Do not advertise 14–17 support | Qualification gate | Yes for broader claim |
| Signing | Candidate is unsigned | Sign and scan the frozen ZIP before distribution | Release gate | Yes |
| Query statistics | No composed `pg_stat_statements` adapter | Use SQL editor or explicit unavailable state | Deferred | No, if unadvertised |
| Database statistics | No composed database/table statistics workspace | Use supported activity/index views | Deferred | No, if unadvertised |
| Query history/settings | No user-facing history browser or settings editor | Use documented defaults and SQL files | Deferred | No, if unadvertised |
| Data editing | No editable result grid | Use explicit SQL with review | Deferred | No |
| PostgreSQL transfer | No PostgreSQL-to-PostgreSQL migration workflow | Use file transfer or external PostgreSQL tools | Deferred | No |
| Backup tools | Backup/restore needs compatible `pg_dump`, `pg_restore`, and `psql` | Install/configure tools on the target machine | Supported prerequisite | No |
| Remote security | TLS/client-certificate/SSPI matrix is not qualified | Limit internal testing to documented connection modes | Qualification gate | Yes for public broad claim |
| Stateful upgrade | A prior-package installer upgrade passed, but real old settings/profile/credential and interrupted-migration recovery is not fully exercised | Preserve user state and run the documented upgrade campaign before public release | Qualification gate | Yes |
