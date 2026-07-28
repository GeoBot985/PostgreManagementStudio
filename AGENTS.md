# Agent guidance

Core is dependency-free; Application orchestrates; Postgres owns Npgsql;
Results owns the in-memory result-store and is the only implementer of the
internal `IResultSetWriter` (see `src/PostgreManagementStudio.Core/AssemblyInfo.cs`);
Desktop is WPF. Dependencies point inward. Keep SQL out of code-behind, use
async APIs and cancellation tokens, and do not add plugins, cloud auth,
Monaco, docking, or commercial grids.

Run `dotnet build --configuration Release` and
`dotnet test --configuration Release` before handoff. To run the gated perf
suite, set `PMS_RUN_PERF=1`.

Result-store rules that persist across sprints:

- The result store must not reference WPF, Npgsql, Monaco, a specific grid
  vendor, clipboard APIs, or file-system export formats.
- Values are stored as typed CLR objects (string/int/long/double/bool/Guid/
  DateTime/byte[]/etc.). Do not apply locale-specific display formatting in
  storage.
- `IResultSetWriter` is `internal`; never expose arbitrary row mutation to
  the visual layer.
- Truncation is never silent: `WasTruncated`, `TruncationReason`,
  `ReceivedRowCount`, `RetainedRowCount`, and `FinalRowCount` are all part of
  the public contract.
- Partial results survive cancellation and failure.
- Disposal is idempotent and never requires a UI thread.

Current milestone: Sprint 002 result-store foundation.