# Architecture Review: Relocate PurchaseOrdersInTransitTile to Purchase Module

## Skip Design: true

## Architectural Fit Assessment

The proposal aligns perfectly with the established Vertical Slice + module-independence convention enforced elsewhere in the codebase. Verification confirms:

- **Destination convention exists** — `backend/src/Anela.Heblo.Application/Features/Purchase/DashboardTiles/` already contains `LowStockEfficiencyTile.cs`. `PurchaseModule.cs:32` already calls `services.RegisterTile<LowStockEfficiencyTile>();` and already has `using Anela.Heblo.Application.Features.Purchase.DashboardTiles;` (line 2).
- **Tile framework supports the move transparently** — `[TileId("purchaseordersintransit")]` on the class (verified at `PurchaseOrdersInTransitTile.cs:6`) decouples persisted dashboard layouts from the C# namespace. Moving the class does not change its public tile id.
- **Cross-module coupling is real** — `DashboardModule.cs:2` carries `using Anela.Heblo.Application.Features.Dashboard.Tiles;` and `DashboardModule.cs:22` registers the tile. After the relocation, both lines become unused and can be removed cleanly.
- **One hidden coupling the spec misses** — `backend/test/Anela.Heblo.Tests/Features/Dashboard/TileIdContractTests.cs:15` uses `typeof(Anela.Heblo.Application.Features.Dashboard.Tiles.PurchaseOrdersInTransitTile).Assembly` as a marker type to pin the application assembly for reflection scanning. The fully qualified namespace will no longer exist after the move; the test will not compile. **This must be addressed as part of the change (see Specification Amendments).**

The fit is otherwise mechanical. No new contracts, no new lifecycles, no new persistence.

## Proposed Architecture

### Component Overview

```
Before:
  Anela.Heblo.Application
    Features/
      Dashboard/
        DashboardModule.cs ──RegisterTile──► PurchaseOrdersInTransitTile ──► IPurchaseOrderRepository
        Tiles/
          PurchaseOrdersInTransitTile.cs                              (Domain.Features.Purchase)
      Purchase/
        PurchaseModule.cs ──RegisterTile──► LowStockEfficiencyTile

  ⇒ DashboardModule has compile-time edge into Domain.Features.Purchase (via tile)

After:
  Anela.Heblo.Application
    Features/
      Dashboard/
        DashboardModule.cs   (no Purchase-domain references)
      Purchase/
        PurchaseModule.cs ──RegisterTile──► LowStockEfficiencyTile
                          ──RegisterTile──► PurchaseOrdersInTransitTile ──► IPurchaseOrderRepository
        DashboardTiles/
          LowStockEfficiencyTile.cs
          PurchaseOrdersInTransitTile.cs

  ⇒ Cross-module edge eliminated; tile co-located with the domain it consumes
```

### Key Design Decisions

#### Decision 1: Preserve direct `IPurchaseOrderRepository` injection (do not switch to MediatR)
**Options considered:**
- (A) Keep direct repository injection as-is.
- (B) Refactor to send a MediatR request, mirroring `LowStockEfficiencyTile`'s pattern.

**Chosen approach:** (A) — keep direct repository injection.

**Rationale:** The spec is explicit that this is a pure relocation with no behavior change (FR-1, FR-4, NFR-1). The MediatR migration is a separate refactor that would expand scope, alter test surface, and risk subtle behavior shifts (caching, pipeline behaviors, error mapping). Convergence on the MediatR pattern can be filed as a follow-up.

#### Decision 2: Preserve the existing `TileId` value (`"purchaseordersintransit"`)
**Options considered:**
- (A) Keep `[TileId("purchaseordersintransit")]` unchanged.
- (B) Rename to something Purchase-prefixed.

**Chosen approach:** (A).

**Rationale:** NFR-4 mandates backward compatibility. `UserDashboardTiles` rows and any persisted user preferences reference this exact string. The `TileIdContractTests.AllConcreteTiles_WhoseNameEndsInTile_HaveBackwardCompatibleTileId` test enforces it: the attribute value must equal `ClassName.ToLower().Replace("tile","")`. The current value already satisfies this; do not change it.

#### Decision 3: Update the contract-test marker type rather than introducing a new sentinel
**Options considered:**
- (A) Update `TileIdContractTests.cs:15` to reference the new fully qualified type `Anela.Heblo.Application.Features.Purchase.DashboardTiles.PurchaseOrdersInTransitTile`.
- (B) Replace the marker with any stable application-layer type (e.g., `DashboardModule` itself or `LowStockEfficiencyTile`).
- (C) Introduce a dedicated `AssemblyMarker` empty class.

**Chosen approach:** (B) — switch the marker to `LowStockEfficiencyTile` (already a stable, conventional tile in the application assembly).

**Rationale:** (A) re-creates the same fragility under a new name. (C) adds a new abstraction the codebase does not currently use. (B) is the minimal, intent-preserving change: the test only needs *some* type from the application assembly, and `LowStockEfficiencyTile` is the canonical example tile.

## Implementation Guidance

### Directory / Module Structure

Create one file, delete one file, edit two:

| Action | Path |
|---|---|
| **Create** | `backend/src/Anela.Heblo.Application/Features/Purchase/DashboardTiles/PurchaseOrdersInTransitTile.cs` |
| **Delete** | `backend/src/Anela.Heblo.Application/Features/Dashboard/Tiles/PurchaseOrdersInTransitTile.cs` |
| **Delete (empty folder)** | `backend/src/Anela.Heblo.Application/Features/Dashboard/Tiles/` (only if empty after the move — verified: it is) |
| **Edit** | `backend/src/Anela.Heblo.Application/Features/Dashboard/DashboardModule.cs` |
| **Edit** | `backend/src/Anela.Heblo.Application/Features/Purchase/PurchaseModule.cs` |
| **Edit** | `backend/test/Anela.Heblo.Tests/Features/Dashboard/TileIdContractTests.cs` |

### Interfaces and Contracts

No changes. Preserve verbatim:
- `[TileId("purchaseordersintransit")]`
- `ITile` implementation (all properties and `LoadDataAsync` signature)
- Constructor signature `(IPurchaseOrderRepository purchaseOrderRepository)`
- Returned anonymous payload shape (`status`, `data{ count, totalAmount, formattedAmount }`, `metadata`, `drillDown`)

The new class declaration must be:
```csharp
namespace Anela.Heblo.Application.Features.Purchase.DashboardTiles;
```

### Data Flow

Unchanged from the user perspective:

```
Frontend tile lookup ──► GetTileDataHandler ──► tile registry resolves "purchaseordersintransit"
                                              ──► PurchaseOrdersInTransitTile.LoadDataAsync
                                              ──► IPurchaseOrderRepository.GetByStatusAsync(InTransit)
                                              ──► aggregated payload returned
```

The only flow change is at composition time: DI registration now occurs inside `AddPurchaseModule()` instead of `AddDashboardModule()`. Provided both modules are registered in `Program.cs` (they are, since `LowStockEfficiencyTile` works today), runtime resolution is identical.

### Edits in Detail

**`DashboardModule.cs`** — remove line 2 `using Anela.Heblo.Application.Features.Dashboard.Tiles;` and line 22 `services.RegisterTile<PurchaseOrdersInTransitTile>();`. The comment on line 21 (`// Register dashboard tiles`) becomes dead — remove it as well, since no Dashboard-owned tiles remain.

**`PurchaseModule.cs`** — add `services.RegisterTile<PurchaseOrdersInTransitTile>();` immediately after line 32. No new `using` directive is needed — line 2 already imports `Anela.Heblo.Application.Features.Purchase.DashboardTiles`.

**`TileIdContractTests.cs:15`** — replace
```csharp
typeof(Anela.Heblo.Application.Features.Dashboard.Tiles.PurchaseOrdersInTransitTile).Assembly
```
with
```csharp
typeof(Anela.Heblo.Application.Features.Purchase.DashboardTiles.LowStockEfficiencyTile).Assembly
```
The comment `// Anela.Heblo.Application` remains accurate.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `TileIdContractTests` fails to compile because its marker type vanished | **HIGH** | Update marker to `LowStockEfficiencyTile` in the same commit (Decision 3). Spec currently misses this — see Amendments. |
| Stale persisted `UserDashboardTiles` rows fail to resolve due to a hidden namespace check | LOW | `[TileId("purchaseordersintransit")]` is the only resolution key; namespace is not persisted. Contract tests in `TileIdContractTests` enforce this. |
| Empty `Features/Dashboard/Tiles/` folder remains in source control | LOW | Delete folder as part of the change so the directory layout reflects ownership. |
| Frontend hardcodes the tile id and breaks if anything changes | LOW | Spec preserves the id verbatim; commit `33ed409a` introduced the stable-id contract precisely for this case. |
| Future Dashboard tiles regress and reintroduce cross-module coupling | LOW | Out of scope, but worth filing a separate task for an analyzer/test that asserts `Features/Dashboard/` does not reference `Domain.Features.<other>`. |

## Specification Amendments

The spec is otherwise complete and correctly scoped. Two amendments are required:

1. **Add FR-6: Update `TileIdContractTests` assembly marker.** The spec does not mention `backend/test/Anela.Heblo.Tests/Features/Dashboard/TileIdContractTests.cs:15`, which hard-codes the fully qualified type `Anela.Heblo.Application.Features.Dashboard.Tiles.PurchaseOrdersInTransitTile` as an assembly-scanning marker. After the move this no longer compiles. The amendment: replace the marker with `typeof(Anela.Heblo.Application.Features.Purchase.DashboardTiles.LowStockEfficiencyTile).Assembly` in the same change.
   *Acceptance:* `dotnet test` runs `TileIdContractTests` green; no other test references the old namespace.

2. **Add FR-7: Remove the empty `Features/Dashboard/Tiles/` folder.** Verified that this tile is the only file in that folder. Leaving an empty `Tiles/` directory in the Dashboard module suggests Dashboard-owned tiles exist when they do not. Delete the folder as part of the commit.
   *Acceptance:* `Features/Dashboard/Tiles/` no longer exists in the working tree.

No other amendments are required. FR-1 through FR-5 are accurate and verified against the actual source.

## Prerequisites

None. The change is self-contained:

- `Anela.Heblo.Application.Features.Purchase.DashboardTiles` namespace already exists (verified — `LowStockEfficiencyTile.cs` lives there).
- `PurchaseModule.cs` is already wired into the composition root (`LowStockEfficiencyTile` works today via the same path).
- `RegisterTile<T>()` extension is already in use by both modules.
- No database migration, no config change, no infrastructure change.

Validation gate before merge: `dotnet build`, `dotnet format`, `dotnet test` (specifically `TileIdContractTests` and any existing `PurchaseOrdersInTransitTile` unit tests — verified there are none under `backend/test`, so no test-file moves are needed beyond the marker fix).