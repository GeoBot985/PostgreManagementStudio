# UI reachability evidence

Sprint 49 evidence was collected from the current Release desktop build and
the source tree. The desktop process was launched from:

`src/PostgreManagementStudio.Desktop/bin/Release/net9.0-windows/PostgreManagementStudio.Desktop.exe`

The process was verified to use that executable path before inspection. No
connection credentials, connection strings, or database content were entered.

## Reproducible live inspection

1. Launch the executable above.
2. Confirm the initial shell contains the File, Edit, View, Query, Database,
   Tools, Window and Help menus; Standard and Query toolbars; status bar;
   Object Explorer tree; SQLQuery1.sql tab; and Results/Messages/Execution Plan
   output tabs.
3. Confirm the disconnected state disables Execute, Cancel, estimated plan,
   actual plan, result actions, Object Explorer refresh and Reconnect.
4. Open File > Connect. Confirm the PostgreSQL dialog exposes server, port,
   database, username, password, SSL mode, environment, read-only, profile,
   Test Connection, Connect and Cancel controls, plus safe password-storage
   wording.
5. Close the dialog without entering credentials.
6. Inspect the View, Query, Database, Tools and Window menus. Record disabled
   or one-shot controls according to the matrix; do not infer a complete
   workflow from their labels.

## Evidence map

| Evidence | Location |
|---|---|
| Live shell and menu structure | `src/PostgreManagementStudio.Desktop/MainWindow.xaml`; current executable inspection |
| Connection dialog composition | `src/PostgreManagementStudio.Desktop/ConnectionDialog.xaml(.cs)`; live File > Connect inspection |
| Routed command registration and enablement | `src/PostgreManagementStudio.Desktop/ShellCommands.cs`, `MainWindow.xaml.cs` |
| Query/result/editor surface | `src/PostgreManagementStudio.Desktop/QueryTabView.xaml(.cs)` |
| Production dependency graph | `src/PostgreManagementStudio.Desktop/ProductionServices.cs` |
| Automated shell evidence | `tests/PostgreManagementStudio.Desktop.Tests/ShellWorkflowTests.cs`, `ProductionCompositionTests.cs` |
| Live PostgreSQL qualification | `docs/sprints/SPRINT_48_REPORT.md`, `TestResults/df038c8466/release-summary.json` |

Screenshots were used during the live inspection to verify visual placement,
but are not stored here because the audit can be reproduced from the steps and
the source evidence without retaining machine-specific window imagery.

## Sprint 50 workspace evidence

The Sprint 50 shell routes now open the durable restore and object-search
workspaces described in [sprint-50-workspaces.md](sprint-50-workspaces.md).
The activity snapshot route was removed from the release command surface
because it did not meet the workspace standard; its backend service remains
available for the deferred live monitor sprint.
