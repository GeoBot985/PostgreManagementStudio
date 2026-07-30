# Sprint 61 defect register

> Sprint 62 disposition: the product defects `S61-C01`, `S61-C02`,
> `S61-H01`, `S61-M01`, and `S61-M02` are closed in `0.9.0-rc.4`.
> `S61-RC01`, `S61-RC02`, and `S61-RC03` remain release-qualification
> blockers. Root causes, fixes, tests, packaged results, and limitations are
> recorded in
> [`../sprint-62/DEFECT_CLOSURE.md`](../sprint-62/DEFECT_CLOSURE.md).

## S61-C01 — Packaged simple wildcard replacement cannot find wildcard

- **Severity:** Critical
- **Affected feature:** Alt+F1 / column-list editor workflow
- **Environment:** Packaged `0.9.0-rc.3`, Windows 11 x64, PostgreSQL 18.4
- **Preconditions:** Connected editor; `s61_audit.orders` successfully described
- **Reproduction:**
  1. Enter `SELECT * FROM s61_audit.orders;`.
  2. Place the caret adjacent to `orders` and invoke Alt+F1.
  3. Confirm ordered columns load.
  4. Invoke `Replace *`.
  5. Repeat with the caret directly on `*`.
- **Expected:** Replace the statement wildcard with the explicit quoted list in
  one editor change.
- **Actual:** Status reports “No matching SELECT wildcard was found in current
  statement.” SQL remains unchanged.
- **Frequency:** 2/2 attempts
- **Evidence:** Sprint 61 packaged walkthrough; Sprint 59 service tests pass,
  proving the issue is at the composed UI/state seam
- **Likely component:** `QueryTabView` description/edit context binding or
  current-statement selection state
- **Workaround:** Manually copy and paste the formatted list
- **Release impact:** Breaks one of the four required daily workflows
- **Recommended fix:** Preserve resolved relation/alias and active statement in
  command state; add packaged/composed tests for simple, alias and multi-alias
  replacement with undo/redo.

## S61-C02 — Valid JSONB import fails in packaged binary COPY

- **Severity:** Critical
- **Affected feature:** Existing-table import wizard
- **Environment:** Packaged `0.9.0-rc.3`, Npgsql 8.0.6, PostgreSQL 18.4
- **Preconditions:** Destination has a writable `jsonb` column; valid JSON text
  is mapped; atomic “Fast bulk import (COPY FROM STDIN)” selected
- **Reproduction:**
  1. Open Database > Import Data.
  2. Select `docs/audits/sprint-61/import-existing.csv`.
  3. Keep detected UTF-8/comma/header/multiline/`\N` settings.
  4. Choose existing table `s61_audit.import_existing`.
  5. Accept 13 valid mappings; identity/generated are omitted.
  6. Execute atomic COPY.
- **Expected:** Three logical rows commit with JSON objects stored as JSONB.
- **Actual:** `Data transfer failed: XX000: unsupported jsonb version number 123`.
  The connection remains available and zero rows commit.
- **Frequency:** 1/1 valid JSONB attempt
- **Evidence:** Packaged results page and independent `SELECT count(*) = 0`
- **Likely component:** Binary COPY JSONB value binding in
  `NpgsqlDataTransferService` or wizard strategy composition
- **Workaround:** Avoid fast COPY and use typed parameterized insertion if the UI
  permits; otherwise import manually outside the application
- **Release impact:** Breaks ordinary valid import and prevents qualification
- **Recommended fix:** Bind JSONB using the Npgsql-supported JSONB representation
  or automatically choose typed inserts for mappings not safely supported by
  binary COPY. Add an exact composed-wizard PostgreSQL regression.

## S61-H01 — Import failure lacks source row and destination column

- **Severity:** High
- **Affected feature:** Import error diagnostics
- **Environment:** Same candidate/environment
- **Preconditions:** A mapped value cannot be represented by the selected COPY
  path
- **Reproduction:** Run the original fixture variant containing an empty numeric
  field, or reproduce `S61-C02`.
- **Expected:** Logical source row, source column, destination column/type, and a
  useful conversion category without secrets.
- **Actual:** Raw OID mismatch or JSONB binary-version text; no source row or
  mapped destination column is identified.
- **Frequency:** 2/2 observed failures
- **Evidence:** Packaged import results pages
- **Likely component:** COPY exception translation/transfer terminal outcome
- **Workaround:** Reduce the file and mappings manually until the value is found
- **Release impact:** Unacceptable troubleshooting experience; practical but
  costly workaround
- **Recommended fix:** Track logical row/mapping during conversion and translate
  safe Npgsql/PostgreSQL details into structured transfer diagnostics.

## S61-M01 — Multiline CSV review overestimates rows

- **Severity:** Medium
- **Affected feature:** Import review estimate
- **Environment:** Same candidate/environment
- **Preconditions:** CSV contains a quoted multiline field
- **Reproduction:** Review `import-existing.csv`.
- **Expected:** Estimate three logical data records.
- **Actual:** Review displays “Estimated rows: 5”; preview displays three valid
  logical records.
- **Frequency:** 2/2 reviews
- **Evidence:** Packaged Preview and Review pages
- **Likely component:** Physical-line counting estimate
- **Workaround:** Treat the value as an estimate and rely on final counts
- **Release impact:** Misleading but does not alter committed data
- **Recommended fix:** Use the bounded delimited-record parser or label physical
  lines explicitly.

## S61-M02 — Consecutive package builds produce different hashes

- **Severity:** Medium
- **Affected feature:** Release packaging/reproducibility
- **Environment:** Windows 11 x64, same source revision/configuration
- **Preconditions:** Build the release ZIP twice from revision
  `424133bff9684c962b93a71feab3ebdc49da46bd`
- **Reproduction:** Run `scripts/release/build-release.ps1` twice.
- **Expected:** A reproducible archive hash or documented controlled source of
  nondeterminism.
- **Actual:** Same 62,374,510-byte size but hashes
  `8179e145...f3c61` and `44ebdd10...790d6`.
- **Frequency:** 1/1 comparison
- **Evidence:** Sprint 61 build/hash record
- **Likely component:** ZIP entry timestamps or non-normalized generated metadata
- **Workaround:** Trust the signed/generated manifest and checksum for each build
- **Release impact:** Does not break source identity, but complicates independent
  reproducibility and provenance
- **Recommended fix:** Normalize archive timestamps/order and all generated
  metadata, or document a controlled non-reproducible policy.

## Release conditions without a demonstrated code defect

### S61-RC01 — SDK-free clean-machine validation not executed

The package is self-contained and starts on the development host, but no clean
Windows 11 x64 environment without an SDK was available. This prevents
unconditional approval.

### S61-RC02 — Candidate is unsigned

The manifest identifies an unsigned internal candidate. Public distribution
requires Authenticode signing or an explicit internal-only release decision.

### S61-RC03 — Complete manual qualification matrices remain incomplete

The critical import failure stopped the full new-table, round-trip, rejected
row, transaction, large-transfer, and all-format packaged walkthrough. Automated
coverage is green but cannot replace the missing post-fix end-to-end evidence.
