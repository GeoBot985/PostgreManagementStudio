# Sprint 59 reconciliation

Candidate: `424133bff9684c962b93a71feab3ebdc49da46bd`

| Requirement | Implemented | Tested | Passed | Evidence | Defect |
| --- | ---: | ---: | ---: | --- | --- |
| Alt+F1 binding | Yes | Packaged + Desktop | Yes | Packaged command resolved `s61_audit.orders` | — |
| Query menu command | Yes | Packaged + Desktop | Yes | Traditional Query menu integration | — |
| Selected-text resolution | Yes | Unit | Yes | `SelectedTextHasPriorityOverCaret` | — |
| Caret identifier | Yes | Packaged + unit | Yes | Directly adjacent caret resolved; complete-token tests | Semicolon-adjacent position did not resolve until caret moved left |
| Schema-qualified identifier | Yes | Packaged + unit | Yes | `s61_audit.orders` and qualified-token cases | — |
| Quoted identifier | Yes | Unit/integration | Yes | Quoting and catalogue identity tests | — |
| Alias resolution | Yes | Unit/prior packaged | Yes | Alias member and no-`AS` tests; Sprint 59 screenshot | — |
| Search-path resolution | Yes | Integration | Yes | Visibility returned without arbitrary choice | — |
| Ambiguous-name selection | Yes | Unit/integration | Yes | Ambiguity is explicit; no arbitrary resolution | — |
| Temporary-table resolution | Limited | Unit/design | Partial | Temporary context modeled | Backend-affinity limitation documented |
| Routine overload resolution | Yes | Integration/prior packaged | Yes | Signature metadata and overload chooser screenshot | — |
| Ordered column metadata | Yes | Packaged + integration | Yes | Physical order matched catalogue | — |
| Copy formats | Yes | Unit/prior packaged | Yes | Six formatter variants | — |
| Column filtering | Yes | Unit/Desktop | Yes | Visible selection and inclusion state | — |
| Column presets | Yes | Unit/prior packaged | Yes | Deterministic all/writable/required/key/non-large presets | — |
| Editor insertion | Yes | Unit/prior packaged | Yes | Bounded selection replacement and caret placement | — |
| `SELECT *` replacement | Yes | Packaged + unit | **No** | Unit service passes; packaged canonical statement fails | `S61-C01` |
| `alias.*` replacement | Yes | Unit/prior packaged | Yes/condition | Prior packaged screenshot and bounded unit test | Must retest after `S61-C01` fix |
| Undo behavior | Yes | Desktop/editor contract | Partial | One editor change is produced | Packaged undo/redo not repeated after failed command |
| Disconnected state | Yes | Desktop | Yes | Command state and message tests | — |
| Stale metadata invalidation | Yes | Integration | Yes | Database OID/fingerprint and dropped-object tests | — |
| Keyboard accessibility | Yes | Packaged | Yes/condition | Alt+F1 and keyboard actions reachable | Core replace action fails |
| Staged metadata loading | Yes | Code/integration | Yes | Core columns precede cancellable secondary detail | — |
| Performance | Yes | Packaged/integration | Yes | Approximately 902 ms; no user-row read | — |

## Resolution matrix

| SQL context | Expected object | Result | Passed |
| --- | --- | --- | ---: |
| Unqualified table | Visible relation | Search-path integration resolves/identifies ambiguity | Yes |
| Schema-qualified table | Exact relation | Packaged `s61_audit.orders` | Yes |
| Quoted table | Exact quoted relation | Quoted-token and integration coverage | Yes |
| Mixed-case table | Case-sensitive quoted relation | Identifier-token/quoting coverage | Yes |
| Unicode table | Unicode relation | Tokenizer and metadata coverage | Yes |
| Alias | Aliased relation | Unit alias extraction | Yes |
| Alias-qualified column | Aliased relation/member | `AliasQualifiedColumnResolvesRelationAndMember` | Yes |
| Multiple aliases/join | Selected alias only | Bounded alias replacement tests | Yes |
| Nested subquery | Nearest bounded statement or explicit ambiguity | Limited conservative behavior | Partial |
| CTE | Editor-local object | `CteIsMarkedAsEditorLocal` | Yes |
| Temporary table | Session temporary relation | Modeled; backend affinity not guaranteed | Partial |
| Duplicate schemas | No arbitrary result | Search-path visibility/ambiguity integration | Yes |
| Search-path object | Visible relation | Visibility integration | Yes |
| Selected identifier | Selection wins | Unit | Yes |
| Caret inside identifier | Complete token | Unit | Yes |
| Caret adjacent | Complete token | Packaged/unit | Yes |
| Overloaded function | Exact signature/chooser | Integration and prior UI evidence | Yes |
| Procedure signature | Exact routine identity | Structured scripting/description metadata | Yes |
| Partially invalid SQL | Bounded identifier still resolves | Unit | Yes |
| Disconnected editor | Controlled unavailable state | Desktop tests | Yes |
| Dropped after cache | Stale identity rejected | Integration | Yes |

## Column-list and wildcard conclusion

Catalogue truth included identity, generated expression, ordinal order,
defaults, nullability, PK/FK/unique membership, comments, enum, domain, array,
and JSON types. Presets and all formatters are deterministic in unit tests.
Nevertheless, the packaged command returned “No matching SELECT wildcard was
found in current statement” for:

```sql
SELECT * FROM s61_audit.orders;
```

It failed both with the caret on the described table and with the caret on `*`.
SQL remained unchanged, so the failure is safe but workflow-critical.
