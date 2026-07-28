# Sprint 36 regression matrix

| Risk | Automated evidence | Result |
|---|---|---|
| Valid and invalid lifecycle transitions | lifecycle unit tests | Pass |
| Stale completion and duplicate execution | execution-ID and concurrent-run tests | Pass |
| Cancellation idempotency and timeout | document unit tests | Pass |
| Long command cancellation/recovery | live `pg_sleep` test | Pass |
| Cancellation during row streaming | live million-row stream cancellation | Pass |
| Immutable profile/database snapshot | unit mutation test and live database override | Pass |
| Rapid tab switching / independence | two-document unit test and ten live concurrent executions | Pass |
| Stale/missing profile and missing database | unit validation and live missing-database test | Pass |
| PostgreSQL diagnostics | missing table/column, divide-by-zero, duplicate relation | Pass |
| Secret redaction | quoted/unquoted credential tests and provider-construction failure | Pass |
| Multi-statement scripts | strings, nested comments, dollar quote, notice, ordered sets, middle failure | Pass |
| Row limit and count separation | 25,000-row live bounded-store test | Pass |
| Rows affected | live DDL/DML session test | Pass |
| Problematic values | live XML, infinity, NaN, network, geometry, enum/composite plus formatter tests | Pass |
| Transaction abort/rollback | live persistent editor-scope `22012`/`25P02`/rollback/recovery | Pass |
| Active transaction disposal | live `pg_stat_activity.xact_start` assertion | Pass |
| Backend termination and recovery | live self-backend termination | Pass |
| Command timeout classification | live timeout and recovery | Pass |
| Broken/invalid connection construction | provider boundary test | Pass |
| Bounded provider backpressure | bounded-channel implementation plus large-stream tests | Pass |
| UI command state and shutdown | document state tests and STA shell lifecycle | Pass |
| Diagnostic privacy | telemetry shape and redaction tests | Pass |

Physical Windows-service stop, cable-level network interruption, and invalid
certificate infrastructure are represented by backend termination, missing
database, connection failure, timeout, and broken-provider paths. They are not
performed by the repeatable suite because they mutate the shared PostgreSQL
host rather than the isolated test database.
