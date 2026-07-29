# Desktop UI consistency audit — Sprint 55

## Method

Inspected `ShellCommands`, MainWindow menus/toolbars/context menus, editor and
result context menus, every Sprint 50–54 workspace constructor and close path,
settings/recovery stores, and deterministic WPF shell tests. The audit used
the command declaration as the canonical inventory and searched all XAML and
code-behind command bindings for duplicate or divergent routes.

## Findings and fixes

| Area | Finding | Sprint 55 result |
|---|---|---|
| Menu structure | Database and Tools routes were valid but context additions were accumulated | Kept the traditional structure; retained Database as a justified menu and grouped monitoring under View/context routes |
| Commands | Close commands used “Query” while the shell uses documents | Canonical labels are now Close Document, Close Other Documents and Close All Documents |
| Duplicate/dead commands | Object Explorer context menu exposed New Query despite being unrelated to the selected node | Removed the duplicate context launch; New Query remains File and toolbar reachable |
| Context targeting | Tree context menu did not select the item under the pointer before command invocation | Added context-opening selection and focus handling |
| Shortcuts | No deterministic collision guard existed | Added Sprint 55 shortcut inventory test; no collisions remain |
| Toolbars | Standard toolbar is limited to new/open/save/refresh/reconnect; query toolbar is execute/cancel/context | No feature-launch button wall found; retained high-frequency controls |
| Workspace lifecycle | Recent modeless workspaces close from QueryTabView cleanup; monitoring cancels its timer/token | Documented policy and verified existing close paths; remaining per-workspace prompt differences are recorded below |
| Accessibility | Icon-only toolbar controls already had automation names/tooltips; grids expose headers in composed workspaces | Added no speculative visual changes; remaining scaling/focus automation gaps are recorded |
| Terminology | Close wording was inconsistent | Standardised document-close command labels; existing PostgreSQL terms retained |
| Layout/settings | Settings store is atomic and corrupt-safe; shell layout persistence is limited | No unsafe persistence added; layout editor remains deferred |

## Remaining issues

- Object Explorer actions are now correctly targeted to the clicked tree item,
  but object-specific designers and script actions are not implemented.
- Settings/options remains disabled; only safe settings infrastructure exists.
- Query history and query/database performance workspaces remain deferred.
- Some older workspaces use local status text rather than a shared state
  component; their current redaction and cancellation behaviour remains safe.
- Full DPI/high-contrast visual qualification and persisted column preferences
  are not covered by the current deterministic test host.

## Evidence

- `ShellWorkflowTests.Sprint55_CanonicalShellShortcutsHaveNoCollisions`
- `docs/architecture/DESKTOP_COMMAND_AND_WORKSPACE_MODEL.md`
- `docs/sprints/SPRINT_55_REPORT.md`
