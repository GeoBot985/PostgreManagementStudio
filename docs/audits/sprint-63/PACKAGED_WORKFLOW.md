# Sprint 63 packaged workflow record

## Sequence status

Candidate:
`PostgreManagementStudio-0.9.0-rc.4-win-x64.zip`

SHA-256:
`A928BDB8600CD2E4787B0ADCF9AE0FEAE84FB5E01B07DE987073178568F01E7E`

The workflow was stopped before Step 1 because the mandatory clean-machine
identity, no-SDK state, standard-user context, package transfer, and independent
hash verification could not be established. Per Sprint 63, an unexecuted step
is a Fail and may not be labelled Partial, inferred, automated, or previously
passed.

| Step | Required action | Result | Actual result |
| ---: | --- | --- | --- |
| 1 | Launch packaged application | Fail | Not executed |
| 2 | Create/open connection | Fail | Not executed |
| 3 | Connect | Fail | Not executed |
| 4 | Expand database | Fail | Not executed |
| 5 | Navigate schemas/tables | Fail | Not executed |
| 6 | Refresh hierarchy | Fail | Not executed |
| 7 | Open properties | Fail | Not executed |
| 8 | Generate CREATE script | Fail | Not executed |
| 9 | Generate explicit SELECT | Fail | Not executed |
| 10 | Open query editor | Fail | Not executed |
| 11 | Execute SELECT twice | Fail | Not executed |
| 12 | Use Alt+F1 | Fail | Not executed |
| 13 | Copy ordered column list | Fail | Not executed |
| 14 | Replace wildcard; undo/redo | Fail | Not executed |
| 15 | Execute modified query | Fail | Not executed |
| 16 | Export results to CSV | Fail | Not executed |
| 17 | Export complete source to JSONL | Fail | Not executed |
| 18 | Import CSV into existing table | Fail | Not executed |
| 19 | Import into new table | Fail | Not executed |
| 20 | Verify imported values | Fail | Not executed |
| 21 | Generate DROP script | Fail | Not executed |
| 22 | Confirmed object deletion | Fail | Not executed |
| 23 | Refresh Object Explorer | Fail | Not executed |
| 24 | Disconnect cleanly | Fail | Not executed |
| 25 | Close application | Fail | Not executed |

Summary: **0 Pass, 25 Fail**.

## Focused matrices

| Matrix | Result | Reason |
| --- | --- | --- |
| CSV/TSV/JSONL/SQL round trips | Fail | Not executed in clean package |
| Existing/new-table import | Fail | Not executed in clean package |
| Atomic/batched transactions | Fail | Not executed in clean package |
| Query and transfer cancellation | Fail | Not executed in clean package |
| Failure and connection recovery | Fail | Not executed in clean package |
| Accessibility and keyboard | Fail | Not executed in clean package |
| Performance and long-session stability | Fail | Not executed in clean package |
| Antivirus and SmartScreen | Fail | Not captured |

The corresponding automated suite remains green, but it is intentionally not
used to alter these packaged results.
