# Sprint 63 defect register

## S63-RC01 — Clean VM guest services unavailable

- **Severity:** Blocker (release qualification infrastructure)
- **Affected gate:** SDK-free environment proof and complete packaged workflow
- **Environment:** Oracle VirtualBox 7.2.12 on Windows 11; Windows 11 Enterprise
  25H2 evaluation guest
- **Expected:** Guest Additions provide execution and controlled file transfer,
  allowing evidence collection under a fresh standard user
- **Actual:** Guest reached the desktop, but Guest Additions run level remained
  `0`; `VBoxManage guestcontrol` reported that the guest execution service was
  not ready
- **Frequency:** Reproduced after setup completion and wait/recheck
- **Product impact:** None demonstrated; application was not launched
- **Release impact:** Clean-machine state, independent package verification,
  and the 25-step workflow cannot be proven
- **Workaround rejected:** Host-side inference, development-host execution, and
  prior automated evidence are prohibited substitutes
- **Disposition:** Open; release blocking
- **Recommended next action:** Repair Guest Additions in a disposable snapshot
  or provision a replacement Hyper-V/VMware/physical Windows 11 machine, then
  restart Sprint 63 evidence collection against the unchanged package

## Product defects

No new PostgreManagementStudio product defect was discovered. No product source
or release artifact was changed.

## Historical release conditions

- `S61-RC01` is superseded by `S63-RC01`: a clean machine now exists, but its
  evidence and workflow could not be completed.
- `S61-RC02` remains open: the candidate is unsigned.
- `S61-RC03` remains open: all 25 packaged steps and focused matrices remain
  incomplete.
