# PostgreSQL compatibility matrix

The release claim is intentionally narrowed to the version with prior isolated
qualification evidence. Npgsql client version is 8.0.6.

| PostgreSQL | Connect/metadata/query | Plans/backup/restore | Activity/actions | Import/export/schema | Status |
|---|---|---|---|---|---|
| 14 | Not tested | Not tested | Not tested | Not tested | Unverified; not claimed |
| 15 | Not tested | Not tested | Not tested | Not tested | Unverified; not claimed |
| 16 | Not tested | Not tested | Not tested | Not tested | Unverified; not claimed |
| 17 | Not tested | Not tested | Not tested | Not tested | Unverified; not claimed |
| 18.4 | Prior isolated qualification passed | Prior isolated qualification passed | Prior isolated qualification passed | Prior isolated qualification passed | Supported scope |

Extension-dependent and permission-limited capabilities remain explicit:
`pg_stat_statements` query statistics are not composed; activity visibility and
session actions depend on PostgreSQL permissions; backup/restore depends on
compatible local PostgreSQL client utilities. Version-gated maintenance and
index options use detected server version where the workflow supports them.

Sprint 57 reran the full disposable, large-dataset regression on PostgreSQL
18.4: 393 passed, 0 failed, 0 skipped. This strengthens only the 18.4 row; it
does not infer compatibility for 14â€“17.
