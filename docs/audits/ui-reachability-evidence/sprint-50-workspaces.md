# Sprint 50 workspace evidence

## Restore workspace

Reproducible route: connect to PostgreSQL, then choose `Database > Restore`.
The shell opens `RestoreWorkspaceWindow` as a non-modal owned workspace. The
workspace displays the source archive, exact server/database/user target,
supported restore options, inspection output, operation progress and cancel
control. Restore requires inspection and an explicit destructive confirmation
that includes the exact target identity. The connection string is held only in
memory; credentials are not displayed or persisted by this workspace.

## Object search workspace

Reproducible routes: `Tools > Search Objects`, `Ctrl+Shift+F`, or the shared
shell search command. The workspace displays server/database scope, supported
object-type filters, definition/system-object options, a sortable result grid,
cancel/clear actions and qualified-name copy. A new search cancels the prior
request, generation checks suppress stale results, and closing the workspace
cancels and disposes its request.

## Activity scope decision

The prior one-shot activity snapshot was removed from the release command
surface. Sprint 50 does not claim a live monitor: refresh, filtering, selection
identity, cancellation and termination require a dedicated follow-up workspace.

## Automated evidence

- `Sprint50_CommandSurfaceUsesSharedRestoreAndSearchRoutes`
- `Sprint50_WorkspacesExposeDurableControlsAndSafeTargeting`
- Desktop composition tests and the Release build pass; exact commands and
  results are recorded in `docs/sprints/SPRINT_50_REPORT.md`.
