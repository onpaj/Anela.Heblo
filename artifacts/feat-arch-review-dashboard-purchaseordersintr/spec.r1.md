# Specification: Relocate `PurchaseOrdersInTransitTile` to Purchase Module

## Summary
Move the `PurchaseOrdersInTransitTile` from the Dashboard module to the Purchase module to eliminate cross-module coupling and restore the established convention that each feature tile lives with the domain it queries. This is a structural refactor with no behavioral changes — file move, registration move, namespace update.

## Background
The Dashboard module currently hosts `PurchaseOrdersInTransitTile` at `backend/src/Anela.Heblo.Application/Features/Dashboard/Tiles/PurchaseOrdersInTransitTile.cs`, but the tile depends on `IPurchaseOrderRepository` from `Anela.Heblo.Domain.Features.Purchase`. This violates the module-independence rule documented in `docs/architecture/development_guidelines.md` ("No direct access to another module's entities").

Every other Purchase-domain tile already follows the correct pattern. `LowStockEfficiencyTile` lives at `backend/src/Anela.Heblo.Application/Features/Purchase/DashboardTiles/LowStockEfficiencyTile.cs` and is registered in `PurchaseModule.cs`. The misplaced tile is the only inconsistency, and `DashboardModule.cs` is currently a hidden breakage site if `IPurchaseOrderRepository` changes signature.

Identified by the daily architecture-review routine on 2026-05-28.

## Functional Requirements

### FR-1: Move tile source file to Purchase module
Relocate the file from its current Dashboard location to the Purchase module's `DashboardTiles` folder, mirroring the placement of `LowStockEfficiencyTile`.

- **Source:** `backend/src/Anela.Heblo.Application/Features/Dashboard/Tiles/PurchaseOrdersInTransitTile.cs`
- **Destination:** `backend/src/Anela.Heblo.Application/Features/Purchase/DashboardTiles/PurchaseOrdersInTransitTile.cs`

**Acceptance criteria:**
- File exists at the new path with identical logic.
- File no longer exists at the old path.
- Namespace updated to match the new folder (e.g. `Anela.Heblo.Application.Features.Purchase.DashboardTiles`).
- No changes to class name, public API, constructor, method bodies, or business logic.

### FR-2: Update DI registration ownership
Transfer tile registration from `DashboardModule` to `PurchaseModule`.

**Acceptance criteria:**
- `PurchaseModule.cs` contains `services.RegisterTile<PurchaseOrdersInTransitTile>();` alongside the existing `LowStockEfficiencyTile` registration.
- `DashboardModule.cs` no longer references `PurchaseOrdersInTransitTile` (registration call removed; `using` for the old tile namespace removed if it becomes unused).
- DI container still resolves the tile at runtime; the dashboard renders it with no visible change.

### FR-3: Remove Dashboard → Purchase compile-time dependency introduced by this tile
The Dashboard module's project/folder should no longer import `Anela.Heblo.Domain.Features.Purchase` solely because of this tile.

**Acceptance criteria:**
- `DashboardModule.cs` no longer carries the `using Anela.Heblo.Domain.Features.Purchase;` statement that existed solely to support the moved tile registration (preserve it only if another remaining symbol in that file requires it).
- No file under `Features/Dashboard/Tiles/` references `IPurchaseOrderRepository`, `PurchaseOrderStatus`, or other types from `Anela.Heblo.Domain.Features.Purchase`.

### FR-4: Behavior preservation
The dashboard surface that displays in-transit purchase orders must continue to function identically.

**Acceptance criteria:**
- The tile is discovered by whatever mechanism the dashboard uses to enumerate registered tiles (the existing `RegisterTile<T>` registration pattern is preserved).
- Tile renders the same data, in the same format, with the same caching/refresh semantics as before.
- Any existing unit/integration tests for the tile continue to pass; if they reference the old namespace, update them to the new namespace.

## Non-Functional Requirements

### NFR-1: Performance
No performance impact expected — this is a structural refactor. No new allocations, no new DI scopes, no new database calls.

### NFR-2: Security
No security impact. Authentication, authorization, and data exposure are unchanged.

### NFR-3: Backwards compatibility
- No public HTTP API surface is affected.
- No database schema changes.
- No frontend changes required — the tile's contract (whatever DTO/response it produces) is unchanged.

### NFR-4: Build & lint
Per `CLAUDE.md` validation rules:
- `dotnet build` succeeds with no new warnings.
- `dotnet format` produces no diff.
- All tests touched (or transitively affected by the namespace change) pass.

## Data Model
No data model changes. The tile continues to call `IPurchaseOrderRepository.GetByStatusAsync(PurchaseOrderStatus.InTransit, cancellationToken)`; the repository, entity, and status enum remain in their current locations within the Purchase domain.

## API / Interface Design
No public API changes. Internal changes only:

- **Type relocation:** `Anela.Heblo.Application.Features.Dashboard.Tiles.PurchaseOrdersInTransitTile` → `Anela.Heblo.Application.Features.Purchase.DashboardTiles.PurchaseOrdersInTransitTile`.
- **Registration ownership:** moved from `DashboardModule.AddDashboardModule` (or equivalent extension) to `PurchaseModule.AddPurchaseModule` (or equivalent).
- **Tile registration pattern:** unchanged — continues to use `services.RegisterTile<T>()`.

## Dependencies
- Existing `RegisterTile<T>` extension method (used by `LowStockEfficiencyTile` and other tiles).
- `IPurchaseOrderRepository` and `PurchaseOrderStatus` from `Anela.Heblo.Domain.Features.Purchase` (unchanged; consumed from the Purchase module where the tile now lives).
- `PurchaseModule.cs` registration site.
- `DashboardModule.cs` registration site (for the removal).

## Out of Scope
- Refactoring or renaming `IPurchaseOrderRepository`, `PurchaseOrderStatus`, or the tile's logic.
- Changing the tile's DTO, caching behavior, or rendering contract.
- Auditing other tiles for similar cross-module placement issues (this spec covers only `PurchaseOrdersInTransitTile`; any further findings belong in their own briefs).
- Changes to the frontend dashboard rendering, tile registry transport, or tile-discovery mechanism.
- Adding or modifying tests beyond what is required to keep the existing suite green.
- Database migrations or configuration changes.

## Open Questions
None.

## Status: COMPLETE