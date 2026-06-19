### task: update-repository-interface

**Goal:** Change `IPackingMaterialRepository.AddDailyRunAsync` return type from `Task` to `Task<bool>`.

**Files to change:**
- `backend/src/Anela.Heblo.Domain/Features/PackingMaterials/IPackingMaterialRepository.cs` — change return type on line 43 and update the XML doc comment

**Implementation steps:**
1. On line 43, replace `Task AddDailyRunAsync(PackingMaterialDailyRun run, CancellationToken cancellationToken = default);` with `Task<bool> AddDailyRunAsync(PackingMaterialDailyRun run, CancellationToken cancellationToken = default);`.
2. Add or update the XML doc comment above the method to document: returns `true` when the row was inserted; returns `false` when a duplicate unique-violation was absorbed (daily run for that date already exists); never throws on duplicate.

**Acceptance criteria:**
- `IPackingMaterialRepository.AddDailyRunAsync` signature is `Task<bool>`.
- `dotnet build` fails on the Persistence, Application, and Tests projects (expected — downstream callers not yet updated), but the Domain project itself builds cleanly.

**Notes:**
- This task must be completed before all other tasks — it is the single source-of-truth change that all downstream tasks conform to.
