All tasks complete. Writing the output artifact.

---

# Implementation: Remove dead `ComponentType` property from `ITile` contract

## What was implemented

Removed the unused `Type ComponentType` property from the `ITile` interface and all related carrier types, factory methods, handlers, and tests. The property was set to `typeof(object)` in every implementation, was never mapped to `DashboardTileDto`, and never crossed the API boundary. This is a pure dead-code removal with byte-identical wire output.

## Files created/modified

**Contract layer (3 files):**
- `backend/src/Anela.Heblo.Xcc/Services/Dashboard/ITile.cs` — removed `Type ComponentType { get; }` declaration
- `backend/src/Anela.Heblo.Xcc/Services/Dashboard/TileMetadata.cs` — removed `Type ComponentType` positional record parameter (8 → 7 params, renumbered)
- `backend/src/Anela.Heblo.Xcc/Services/Dashboard/TileData.cs` — removed `public Type ComponentType { get; set; } = typeof(object);` property

**Registry and handler (2 files):**
- `backend/src/Anela.Heblo.Xcc/Services/Dashboard/TileRegistry.cs` — removed `tile.ComponentType` argument from `ToMetadata` factory
- `backend/src/Anela.Heblo.Application/Features/Dashboard/UseCases/GetTileData/GetTileDataHandler.cs` — removed `ComponentType = tile.ComponentType,` from `TileData` initializer

**Production tile implementations (16 files):**
- `BackgroundTaskStatusTile.cs`, `InvoiceImportStatisticsTile.cs`, `FailedJobsTile.cs`, `InventorySummaryTileBase.cs`, `InventoryCountTileBase.cs`, `LowStockAlertTile.cs`, `DataQualityStatusTile.cs`, `DqtYesterdayStatusTile.cs`, `CriticalGiftPackagesTile.cs`, `TransportBoxBaseTile.cs`, `ManualActionRequiredTile.cs`, `ManufactureConditionsTile.cs`, `UpcomingProductionTile.cs`, `LowStockEfficiencyTile.cs`, `PurchaseOrdersInTransitTile.cs`, `WeatherForecastTile.cs`

**Tests (10 files):**
- `TestTiles.cs` — removed from 5 private tile classes (`replace_all`)
- `TileExtensionsTests.cs` — removed from 2 private test tiles
- `TileRegistryTests.cs` — removed from `TrackedTile`
- `TileRegistryValidationTests.cs` — removed from 3 private validation tiles
- `GetAvailableTilesHandlerTests.cs` — removed `typeof(object)` from 3 `TileMetadata` constructor calls
- `GetTileDataHandlerTests.cs` — removed `typeof(object)` from 3 `TileMetadata` constructor calls
- `GetUserSettingsHandlerTests.cs` — removed `typeof(object)` from 1 `MakeTile()` helper call
- `ManualActionRequiredTileTests.cs` — removed `Assert.Equal(typeof(object), _tile.ComponentType)` assertion line
- `ManufactureConditionsTileTests.cs` — removed `_tile.ComponentType.Should().Be(typeof(object))` assertion line
- `WeatherForecastTileTests.cs` — removed `_tile.ComponentType.Should().Be(typeof(object))` assertion line

## Tests

All 31 changed files are backend-only. Validation results:
- `dotnet build`: 0 errors (pre-existing 233 warnings unchanged)
- `dotnet test` (dashboard subset): 115/115 passed; 3 per-tile metadata test classes 23/23 passed
- Full suite: 40 failures are pre-existing Docker/testcontainer integration tests, unrelated to this change
- `grep -rn "ComponentType" --include="*.cs"`: 0 hits

## How to verify

```bash
cd /path/to/repo
grep -rn "ComponentType" --include="*.cs" backend/src/ backend/test/
# Expected: zero output

dotnet build
# Expected: 0 errors

dotnet test --filter "FullyQualifiedName~Dashboard" backend/test/Anela.Heblo.Tests/
# Expected: 115/115 passed
```

## Notes

The plan's baseline grep listed 35 hits but missed 7 additional `TileMetadata` constructor call sites in 3 test files (`GetAvailableTilesHandlerTests.cs`, `GetTileDataHandlerTests.cs`, `GetUserSettingsHandlerTests.cs`). These were caught by the code quality reviewer and fixed in a second implementer pass. The final commit touches 31 files (vs. 28 in the plan) due to these additional sites.

`DashboardTileDto` was not modified. The JSON shape returned by dashboard endpoints is byte-identical.

## PR Summary

Removes the dead `Type ComponentType` property from the `ITile` interface, `TileMetadata` record, `TileData` class, all 16 production tile implementations, and all consuming sites in the registry, handler, and test suite. The property was set to `typeof(object)` everywhere, never propagated to `DashboardTileDto`, and never reached the frontend — pure boilerplate that imposed a compile-time obligation on every new tile author.

`DashboardTileDto` is unchanged: the dashboard API JSON shape is byte-identical and the auto-generated TypeScript client regenerates as a no-op.

### Changes
- `ITile.cs` — removed `Type ComponentType { get; }` interface member
- `TileMetadata.cs` — removed positional `Type ComponentType` parameter (record signature simplified to 8 params)
- `TileData.cs` — removed `Type ComponentType` mutable property
- `TileRegistry.cs` — dropped `tile.ComponentType` from `ToMetadata` factory call
- `GetTileDataHandler.cs` — dropped `ComponentType = tile.ComponentType` from `TileData` initializer
- 16 production tile classes — removed `ComponentType => typeof(object)` expression-body property
- 7 test files — removed property declarations from private/fixture tile classes and `typeof(object)` arguments from `TileMetadata` constructor calls
- 3 test files — removed single `ComponentType` assertion line from multi-assert metadata test methods (surrounding assertions preserved)

## Status
DONE_WITH_CONCERNS

**Concern:** The plan's baseline grep enumerated exactly 35 expected hits but missed 7 additional `TileMetadata(...)` constructor call sites (not property declarations, so grep for `ComponentType` wouldn't catch them). The code quality review caught these. Future plans for positional-record parameter removals should also grep for constructor call argument patterns, not only property/field declarations.