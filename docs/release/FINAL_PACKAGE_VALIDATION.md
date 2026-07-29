# Final package validation

The final package is the candidate in `FINAL_RC_CANDIDATE.md`.

| Check | Result |
|---|---|
| Release build | Pass, 0 warnings / 0 errors |
| Manifest identity | Pass: version `0.9.0-rc.3`, revision `21e3ab2...`, clean source |
| Package verification | Pass: 407 archive files, 401 manifest files |
| Desktop launch | Pass: packaged EXE opened the disconnected WPF shell with menus, toolbars, Object Explorer and query tab |
| Install/repair/uninstall preservation | Pass in isolated profile |
| Prior-package installer upgrade | Pass from the Sprint 56 ZIP into the same isolated install root |
| Reinstall | Covered by installer lifecycle; pass |
| Path/portable campaign | Not tested from a non-system drive/path-with-spaces |
| Clean standard-user/DPI campaign | Not tested; release condition |

The validation host was Windows 11 x64 with PostgreSQL 18.4 installed locally,
not a clean virtual machine. The self-contained package needs no developer
tools; backup/restore needs compatible PostgreSQL utilities. No signing or
malware scan was available in this environment.
