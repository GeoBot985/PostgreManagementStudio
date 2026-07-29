# Desktop feature composition standard

This standard defines when a capability may be called a completed desktop
feature. Backend implementation, a passing unit test, or a button that invokes
a service method is not sufficient.

## Required composition

A completed feature must provide, as applicable:

- a discoverable entry point in the traditional menu-bar-first shell;
- consistent routed-command integration for menu, toolbar, keyboard and
  context-menu surfaces;
- correct active-document, connection, database and selected-object targeting;
- a durable dialog or workspace appropriate to the task;
- input validation and clear permission handling;
- loading, empty, success, failure and cancellation states;
- destructive confirmation and target identity before destructive operations;
- progress reporting and a bounded cancellation path for long operations;
- useful structured output, not only raw text or a message box;
- keyboard behaviour, focus order and sensible disabled-state explanations;
- proper disposal, shutdown and stale-update protection;
- state and layout persistence where it materially improves recovery or reuse;
- automated coverage of the core workflow logic and command enablement;
- redacted errors and logs with no credentials, connection strings, or other
  sensitive values.

## Classification gate

## Sprint 55 shell policy

The desktop shell is menu-bar-first. `ShellCommands` is the canonical command
owner; menus, toolbars, keyboard gestures and context menus bind to the same
routed command rather than duplicating feature handlers. A document is an SQL
editor tab. A workspace is a modeless tool window and is either singleton per
editor context or explicitly multi-instance; it must show its connection and
database target. Modal dialogs are reserved for short input or confirmation
steps, and wizards are reserved for staged transfer/review flows.

Every workspace that starts asynchronous work owns a cancellation/lifetime
token and stops refresh or detaches updates before disposal. Persisted state is
limited to validated, non-sensitive preferences; active PIDs, executable plans,
credentials, connection strings and destructive confirmations are never
restored. Destructive commands identify and revalidate their target immediately
before execution. Cross-workspace navigation preserves the originating server
and database or reports that the target is unavailable.

Use `NOT_IMPLEMENTED` when meaningful implementation is absent. Use
`SERVICE_ONLY` when implementation exists below the desktop boundary. Use
`DIAGNOSTIC_OR_TEMPORARY_UI` for one-shot/raw/developer/provisional surfaces.
Use `PARTIALLY_REACHABLE` when a real route exists but cannot safely complete
the advertised workflow. Use `END_TO_END_REACHABLE` when the primary workflow
can be completed but still falls short of release quality. Reserve
`RELEASE_QUALITY` for a discoverable, consistent, tested, recoverable workflow
that satisfies the applicable requirements above.

## Review questions

Before marking a feature complete, reviewers must be able to answer yes to:

1. Can a new user find it without reading source code?
2. Does it act on the intended connection/document/object rather than stale
   ambient state?
3. Can the user understand loading, empty, success, failure and cancellation?
4. Can destructive work be reviewed, confirmed, cancelled and reconciled?
5. Does the workflow remain usable after provider failure or window shutdown?
6. Are keyboard, menu, toolbar and context routes consistent?
7. Is the result useful enough to support the next user decision?
8. Do deterministic tests cover the boundary and command state?

Any “no” must be recorded as a blocking gap or a release-scope decision.
