# Release-candidate performance results

## Current reproducible baseline

The Sprint 56 release build completed in approximately 4 seconds after restore
was warm on this workstation. The full solution test run completed in under 5
seconds after build artifacts were present. The release package is self-contained
and contains 407 archive files (401 application files in the manifest).

Automated tests passed: Core 188, Results 63, PostgreSQL 54, Desktop 28;
integration tests 60 were skipped without a configured PostgreSQL environment.
No performance claim is derived from skipped integration tests.

## Resource review

Existing bounded result storage, metadata concurrency, latest-request search,
bounded transfer history, bounded activity refresh, cancellation tokens and
atomic temporary-file output were reviewed. No new unbounded collection or
timer was introduced by Sprint 56. The installer lifecycle test cleaned its
temporary root successfully.

## Not measured here

Cold/warm startup timing, private bytes/handles, extended monitoring duration,
large-schema expansion, million-row transfer, multi-DPI rendering and
connection-pool pressure require the isolated PostgreSQL and clean Windows
campaigns. They remain qualification work, not implied passes.
