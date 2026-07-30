# Sprint 63 clean-machine evidence

## Outcome

**Qualification gate failed.** A clean Windows 11 Enterprise x64 guest was
created and booted, but the guest evidence collector could not run because
VirtualBox Guest Additions did not provide guest execution or file transfer.
No absent evidence is represented as a Pass.

## Host and image provenance

| Item | Evidence |
| --- | --- |
| Hypervisor | Oracle VirtualBox 7.2.12 |
| VM | `PMS-Sprint63-Clean`, disposable |
| Guest source | Windows 11 Enterprise 25H2 Evaluation, English x64 |
| ISO bytes | 7,092,807,680 |
| ISO SHA-256 | `A61ADEAB895EF5A4DB436E0A7011C92A2FF17BB0357F58B13BBC4062E535E7B9` |
| Microsoft hash check | Matched the official evaluation hash PDF |
| Observed guest build | `26100.ge_release.240331-1435` |
| VM resources | 8 GB RAM, 80 GB disk, EFI, TPM 2.0 |
| Networking | NAT and isolated VirtualBox host-only adapter |
| Integration | Clipboard and drag-and-drop disabled |
| Source isolation | Repository source not copied to guest |

The installer initially stalled under the host Hyper-V execution engine. The
same populated disposable disk completed setup using one virtual CPU. This
affected environment provisioning only; the release package was never changed
or launched.

## Prepared evidence collection

`guest-qualification.ps1` was prepared to record:

- Windows edition, version, build, architecture, locale, timezone, display,
  user, and administrator state;
- `dotnet --list-sdks` and `dotnet --list-runtimes`;
- application-data directories before first launch;
- package path, size, SHA-256, extraction path, and file count;
- manifest identity and package verifier output;
- archive/executable Authenticode state;
- Defender status and scan;
- unexpected source/test files and relevant environment-variable names.

The script did not execute in the guest. Therefore it provides collection
design, not result evidence.

## Evidence actually observed

- Windows installed from the hash-verified ISO.
- The guest reached the new `PMS Qualification` desktop.
- No PostgreManagementStudio launch occurred.
- VirtualBox reported Guest Additions run level `0`.
- `VBoxManage guestcontrol` returned “guest execution service is not ready”.

## Mandatory evidence not obtained

- no-.NET-SDK proof;
- installed runtime and PostgreSQL utility inventory;
- standard-user privilege proof;
- locale/timezone/scaling/resolution record;
- pre-launch application-data directory record;
- independently calculated guest package hash;
- clean extraction inventory and package-verifier output;
- Defender/SmartScreen result;
- post-launch application-data record.

## Decision impact

Sprint 63 states that clean-machine validation which cannot be completed is a
mandatory `Not approved for release` outcome. Host-side facts and the successful
fresh desktop cannot substitute for the missing independent guest evidence.
