# Agent guidance

Core is dependency-free; Application orchestrates; Postgres owns Npgsql; Results owns result contracts; Desktop is WPF. Dependencies point inward. Keep SQL out of code-behind, use async APIs and cancellation tokens, and do not add plugins, cloud auth, Monaco, docking, or commercial grids.

Run `dotnet build --configuration Release` and `dotnet test --configuration Release` before handoff. Current milestone is the version-query vertical slice.
