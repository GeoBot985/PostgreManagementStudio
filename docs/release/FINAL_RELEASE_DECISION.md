# Final release decision

## APPROVE_WITH_DOCUMENTED_CONDITIONS

Approve PostgreManagementStudio 0.9.0-rc.3 **as an internal release candidate**
identified by `FINAL_RC_CANDIDATE.md`. This is not approval for public or broad
external distribution.

Evidence supports the narrow Windows 11 x64 / PostgreSQL 18.4 scope: frozen
package verification, UI launch, installer lifecycle, prior-package installer
upgrade, and a disposable PostgreSQL run of 393 passing tests. No blocker or
critical defect, credential exposure, wrong-target finding, or installer data
loss was reproduced.

Public release remains conditional on all of the following:

1. sign and malware/SmartScreen-scan the exact frozen package and approve licence notices;
2. run clean Windows standard-user, DPI and multi-monitor acceptance;
3. run a real old-profile/settings/credential upgrade and interrupted-state recovery campaign;
4. keep PostgreSQL support limited to 18.4 unless each broader claimed version is qualified;
5. do not advertise deferred settings/history/statistics, roles, editing, PG-to-PG transfer, or direct synchronisation.

If any condition reveals a safety, installation, credential, or wrong-target
defect, invalidate this decision and open a targeted remediation sprint.
