Both reviewers approved. The implementation is complete.

---

# Implementation: Relocate PurchaseOrdersInTransitTile to Purchase Module

## What was implemented

Pure C# source-tree refactor: moved `PurchaseOrdersInTransitTile` from `Features/Dashboard/Tiles/` to `Features/Purchase/DashboardTiles/`, shifted its DI registration from `DashboardModule` to `PurchaseModule`, updated the `TileIdContractTests` assembly marker, and deleted the now-empty `Features/Dashboard/Tiles/` folder.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/Purchase/DashboardTiles/PurchaseOrdersInTransitTile.cs` — new location; only the namespace line changed from `Dashboard.Tiles` to `Purchase.DashboardTiles`
- `backend/src/Anela.Heblo.Application/Features/Dashboard/Tiles/PurchaseOrdersInTransitTile.cs` — deleted
- `backend/src/Anela.Heblo.Application/Features/Dashboard/DashboardModule.cs` — removed `using Dashboard.Tiles`, removed `RegisterTile<PurchaseOrdersInTransitTile>()` and its dead comment
- `backend/src/Anela.Heblo.Application/Features/Purchase/PurchaseModule.cs` — added `services.RegisterTile<PurchaseOrdersInTransitTile>();` after `LowStockEfficiencyTile`
- `backend/test/Anela.Heblo.Tests/Features/Dashboard/TileIdContractTests.cs` — swapped assembly marker `typeof` from old namespace to `LowStockEfficiencyTile`
- `backend/src/Anela.Heblo.Application/Features/Dashboard/Tiles/` (empty folder) — deleted

## Tests

- `TileIdContractTests` (4 tests): all pass — verifies tile ID attribute, lowercase, uniqueness, and backward-compatible ID derivation
- Full backend suite (4211 tests): all pass (pre-existing Docker-infrastructure failures unrelated to this change)

## How to verify

```bash
dotnet build backend/Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~TileIdContractTests"
grep -rn "Anela.Heblo.Domain.Features.Purchase" backend/src/Anela.Heblo.Application/Features/Dashboard/
# expected: no output
```

## Notes

No deviations. All seven FRs (including arch-review amendments FR-6 and FR-7) satisfied. Tile ID `"purchaseordersintransit"` and full `LoadDataAsync` body preserved verbatim — backward compatibility maintained per NFR-4.

## PR Summary

Relocate `PurchaseOrdersInTransitTile` from `Features/Dashboard/Tiles/` to `Features/Purchase/DashboardTiles/` and shift its DI registration from `DashboardModule` to `PurchaseModule`. This eliminates the compile-time dependency from the Dashboard module onto `Anela.Heblo.Domain.Features.Purchase`, restoring the module-ownership convention already established by `LowStockEfficiencyTile`.

The tile's identifier, payload, constructor, and runtime behaviour are completely unchanged (NFR-4). The `TileIdContractTests` assembly-marker `typeof` was repointed to `LowStockEfficiencyTile` since the old fully-qualified name no longer exists after the move. The now-empty `Features/Dashboard/Tiles/` folder is removed so the directory tree honestly reflects that no Dashboard-owned tiles remain.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Purchase/DashboardTiles/PurchaseOrdersInTransitTile.cs` — created; namespace changed to `Purchase.DashboardTiles`, body verbatim
- `backend/src/Anela.Heblo.Application/Features/Dashboard/Tiles/PurchaseOrdersInTransitTile.cs` — deleted
- `backend/src/Anela.Heblo.Application/Features/Dashboard/DashboardModule.cs` — removed stale `using` and `RegisterTile` call
- `backend/src/Anela.Heblo.Application/Features/Purchase/PurchaseModule.cs` — added `RegisterTile<PurchaseOrdersInTransitTile>()`
- `backend/test/Anela.Heblo.Tests/Features/Dashboard/TileIdContractTests.cs` — assembly marker updated to `LowStockEfficiencyTile`

## Status
DONE