# Specification: Relocate PurchaseOrdersInTransitTile to Purchase Module

## Summary
Move `PurchaseOrdersInTransitTile` from the Dashboard module to the Purchase module to eliminate a cross-module dependency that violates the project's module independence rule. This is a pure refactoring with no behavior, API, or UI changes — only file location and module registration shift.

## Background
The Anela.Heblo backend follows Clean Architecture with Vertical Slice organization, where each feature module owns its domain types and the tiles that depend on them. Dashboard tiles that consume another module's domain data must live in that module, not in Dashboard.

`PurchaseOrdersInTransitTile` currently sits at `backend/src/Anela.Heblo.Application/Features/Dashboard/Tiles/PurchaseOrdersInTransitTile.cs` but injects `IPurchaseOrderRepository` from `Anela.Heblo.Domain.Features.Purchase`. This creates a compile-time dependency from `DashboardModule` onto the Purchase domain, breaking the module independence rule documented in `docs/architecture/development_guidelines.md`.

The established convention is demonstrated by `LowStockEfficiencyTile`, a Purchase-domain tile located at `backend/src/Anela.Heblo.Application/Features/Purchase/DashboardTiles/LowStockEfficiencyTile.cs` and registered via `PurchaseModule.cs`. Aligning `PurchaseOrdersInTransitTile` to this pattern restores consistency and removes the hidden coupling.

## Functional Requirements

### FR-1: Move tile source file to Purchase module
Move the file `PurchaseOrdersInTransitTile.cs` from its current Dashboard location to the Purchase module's `DashboardTiles` folder, mirroring the layout used by `LowStockEfficiencyTile`.

**Source:** `backend/src/Anela.Heblo.Application/Features/Dashboard/Tiles/PurchaseOrdersInTransitTile.cs`
**Destination:** `backend/src/Anela.Heblo.Application/Features/Purchase/DashboardTiles/PurchaseOrdersInTransitTile.cs`

**Acceptance criteria:**
- The file exists at the new path.
- The file no longer exists at the old path.
- The class namespace is updated to match its new location (e.g. `Anela.Heblo.Application.Features.Purchase.DashboardTiles`).
- The class body, constructor, dependencies, and `LoadDataAsync` (or equivalent) logic are unchanged.
- The tile's `TileId` / identifier value remains identical so the frontend continues to resolve it (per the stable tile identifiers introduced in commit `33ed409a`).

### FR-2: Register tile in PurchaseModule
Register the relocated tile from `PurchaseModule.cs`, adjacent to the existing `LowStockEfficiencyTile` registration.

**Acceptance criteria:**
- `PurchaseModule.cs` contains a `services.RegisterTile<PurchaseOrdersInTransitTile>();` call.
- The new `using` directive for the relocated namespace is added to `PurchaseModule.cs` if required.
- Application starts successfully and DI resolves the tile from the Purchase module registration.

### FR-3: Remove tile registration from DashboardModule
Remove the Dashboard module's registration of and reference to the tile.

**Acceptance criteria:**
- `DashboardModule.cs` no longer contains `services.RegisterTile<PurchaseOrdersInTransitTile>();` (the call currently on line 22).
- The `using Anela.Heblo.Application.Features.Dashboard.Tiles;` (or equivalent) reference is removed from `DashboardModule.cs` if it becomes unused after the change.
- `DashboardModule.cs` has no remaining `using` or symbol reference to `Anela.Heblo.Domain.Features.Purchase` introduced by this tile.

### FR-4: Preserve runtime behavior
The tile must continue to function identically from the user's perspective after the move.

**Acceptance criteria:**
- The tile renders on the Dashboard with the same identifier, title, data, and refresh cadence as before.
- The tile queries `IPurchaseOrderRepository.GetByStatusAsync(PurchaseOrderStatus.InTransit, ...)` and returns the same payload shape.
- No frontend changes are required; the existing tile rendering code resolves the unchanged tile identifier.

### FR-5: Eliminate Dashboard → Purchase compile-time coupling
After the change, the Dashboard module assembly must not depend on Purchase domain types via any tile it owns.

**Acceptance criteria:**
- Searching `backend/src/Anela.Heblo.Application/Features/Dashboard/` returns no references to `Anela.Heblo.Domain.Features.Purchase`.
- `dotnet build` succeeds for the entire solution.
- `dotnet format` reports no violations introduced by the change.

## Non-Functional Requirements

### NFR-1: Performance
No performance impact. The tile's execution path, repository call, and data shape are unchanged.

### NFR-2: Security
No security impact. Authentication, authorization, and data access semantics are identical — only the registering module changes.

### NFR-3: Maintainability
The change improves maintainability by restoring the convention that each module owns its tiles. After this refactor, a developer changing `IPurchaseOrderRepository` only needs to touch Purchase-module code.

### NFR-4: Backwards compatibility
The tile identifier must remain stable so existing dashboard layouts, user preferences, and any persisted tile references continue to resolve correctly.

## Data Model
No data model changes. The tile continues to consume `PurchaseOrder` entities filtered by `PurchaseOrderStatus.InTransit` via `IPurchaseOrderRepository.GetByStatusAsync`.

## API / Interface Design
No external API changes. No frontend changes. No new contracts. The only interface-level change is internal DI registration moving from `DashboardModule` to `PurchaseModule`.

## Dependencies
- Existing `IPurchaseOrderRepository` in `Anela.Heblo.Domain.Features.Purchase`.
- Existing `RegisterTile<T>()` extension method (already used by `LowStockEfficiencyTile`).
- Existing tile framework / base class used by `PurchaseOrdersInTransitTile` today.
- Pattern reference: `LowStockEfficiencyTile` at `backend/src/Anela.Heblo.Application/Features/Purchase/DashboardTiles/LowStockEfficiencyTile.cs`.

## Out of Scope
- Any change to the tile's UI, layout, data payload, or refresh behavior.
- Renaming the tile class or changing its public identifier.
- Refactoring other Dashboard tiles, even if they exhibit similar coupling (out of scope for this task; file separately if found).
- Adding new tests beyond what is necessary to keep existing tests green.
- Frontend changes of any kind.
- Changes to `IPurchaseOrderRepository` or `PurchaseOrderStatus`.

## Open Questions
None.

## Status: COMPLETE