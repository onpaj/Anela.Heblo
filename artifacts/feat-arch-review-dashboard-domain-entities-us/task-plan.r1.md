Plan saved to `docs/superpowers/plans/2026-06-01-relocate-dashboard-domain-entities.md`.

**Summary of the plan:**

- 7 tasks, atomic-commit at the end (per spec NFR-3).
- **Task 1** creates the new `Domain/Features/Dashboard/` folder with the three files and deletes the originals.
- **Task 2** updates 5 Persistence files (`using` flips only).
- **Task 3** updates 5 string occurrences in `ApplicationDbContextModelSnapshot.cs` — the arch-review's load-bearing amendment.
- **Tasks 4–5** update 4 Application handlers and 4 test files with per-file instructions (some files must keep `Xcc.Services.Dashboard` for `ITileRegistry`/`TileMetadata`/etc.; others drop it entirely).
- **Task 6** validates: `dotnet build`, `dotnet format --verify-no-changes`, `dotnet test --filter Dashboard`, solution-wide stale-reference grep, plus an optional `dotnet ef migrations add` no-op probe.
- **Task 7** stages the exact file list and commits.

The call-site list was verified by `grep` against the working tree — files the arch-review listed but that actually only reference DTOs (e.g. `Contracts/*Dto.cs`, `*Lock.cs`, `DashboardController.cs`, `DashboardControllerTests.cs`, `GetTileDataHandler.cs`, `GetAvailableTilesHandler.cs`) are explicitly listed as NOT-modified to prevent churn.

Per the pipeline note, no execution-handoff prompt — the plan file is the artifact.