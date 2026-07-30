# Sprint 60 reconciliation

Candidate: `424133bff9684c962b93a71feab3ebdc49da46bd`

## Import

| Requirement | Implemented | Tested | Passed | Evidence | Defect |
| --- | ---: | ---: | ---: | --- | --- |
| CSV | Yes | Packaged + automated | Partial | Valid preview/mapping; execution failed on JSONB | `S61-C02` |
| TSV/custom delimiters | Yes | Unit | Yes/condition | Detection/parser tests | Manual packaged round trip incomplete |
| Encoding detect/override | Yes | Packaged + unit | Yes | UTF-8 detected; UTF variants tested | — |
| Delimiter/quote/header detection | Yes | Packaged + unit | Yes | Correct comma/quote/header review | — |
| Preview/malformed detection | Yes | Packaged + unit | Yes | Bounded logical preview; malformed tests | — |
| Existing-table import | Yes | Packaged + integration | **No** | Valid JSONB run failed | `S61-C02` |
| New-table import | Yes | Integration/prior UI | Partial | Service creates/imports complex types | Not rerun after blocker |
| Conservative type inference | Yes | Unit/integration | Yes/condition | Leading-zero/mixed/date promotion tests | Packaged new-table run pending |
| Mapping/conversion rules | Yes | Packaged + unit | Partial | 13 typed mappings correct; runtime binding failed | `S61-C02`, `S61-H01` |
| Date/timestamp/numeric | Yes | Unit/integration | Yes/condition | Invariant conversion tests | Complete packaged value verification pending |
| NULL vs empty | Yes | Parser/integration | Yes/condition | Explicit `\N`; preview distinction | Successful packaged commit pending |
| Identity/generated columns | Yes | Packaged + integration | Yes | Both omitted from writable mapping | — |
| Preflight | Yes | Unit/packaged review | Yes | Required/unsafe mapping tests | — |
| Atomic success | Yes | Integration | Yes/condition | Service integration | Packaged success blocked |
| Atomic failure | Yes | Packaged + integration | Yes | Two failures left zero rows | — |
| Batched transactions | Yes | Integration | Yes/condition | Partial-commit disclosure test | Manual packaged run pending |
| Stop on error | Yes | Packaged + integration | Yes | Reviewed and controlled failure | Diagnostics weak |
| Collect errors/rejected output | Yes | Unit/integration/prior UI | Yes/condition | Atomic rejected-file tests | Manual adversarial rerun pending |
| Progress/cancellation | Yes | Desktop/integration | Yes/condition | Cancellation and progress contracts | Full packaged measurement pending |
| Completion summary | Yes | Packaged export/prior import | Partial | Failures correctly avoided success | Successful packaged import unavailable |

## Export

| Requirement | Implemented | Tested | Passed | Evidence | Defect |
| --- | ---: | ---: | ---: | --- | --- |
| Object Explorer export | Yes | Desktop/integration | Yes/condition | Tasks reachability and relation streaming | Not manually repeated |
| Result-grid export | Yes | Packaged | Yes | Two retained rows exported | — |
| Selected rows/columns/range | Yes | Results/Desktop | Yes/condition | Selection and reordered-column tests | Not manually repeated |
| Truncation warnings | Yes | Packaged + tests | Yes | Wizard explicitly described retained scope | — |
| CSV | Yes | Unit/integration | Yes/condition | Parser/export tests | Independent round trip pending |
| TSV | Yes | Unit | Yes/condition | Serializer tests | Independent round trip pending |
| JSON array | Yes | Unit | Yes/condition | Streaming serializer tests | Manual parse pending |
| JSON Lines | Yes | Packaged + independent parser | Yes | 2 rows, 529 bytes, valid JSONL | — |
| SQL INSERT | Yes | Results/integration | Yes/condition | PostgreSQL literal and relation replay tests | Manual replay pending |
| Streaming | Yes | Integration/performance | Yes | Async relation export and bounded tests | Per-format peak-memory matrix pending |
| Complex PostgreSQL types | Yes | Integration | Yes/condition | Mixed values and relation export tests | Complete manual matrix pending |
| Overwrite protection | Yes | Unit/integration | Yes | Existing destination retained on failure/cancel | — |
| Temporary-file finalization | Yes | Packaged + tests | Yes | Atomic final path and successful JSONL | — |
| Cancellation | Yes | Integration | Yes/condition | Final path not replaced | Manual timing pending |
| Completion summary | Yes | Packaged | Yes | 2 rows/10 columns/529 bytes matched parser | — |

## Transaction and data-integrity evidence

- First invalid existing-table import: controlled OID mismatch; row count `0`.
- Corrected valid existing-table import: JSONB binary-version failure; row count
  `0`.
- Therefore atomic failure matched database reality in both cases.
- Integration tests cover atomic success, atomic rollback, batched partial
  commit, cancellation, new-table creation, and complex typed values.
- The packaged valid path remains blocked, so the integration evidence is not
  treated as a substitute for release qualification.

## Performance and scale evidence

The full Release run enabled the large-dataset gates. Automated tests cover
bounded previews, lazy large schemas, million-row result retention, relation
streaming, and cancellation. The packaged two-row JSONL export completed in
approximately 0.014 seconds. This audit did not capture peak memory, throughput,
and cancellation latency for every transfer format; those are release
conditions.
