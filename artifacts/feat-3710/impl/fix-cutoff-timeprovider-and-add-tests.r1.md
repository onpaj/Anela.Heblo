## Implementation summary — fix-cutoff-timeprovider-and-add-tests (r1)

### Changes

1. `backend/src/Anela.Heblo.Application/Features/Catalog/DashboardTiles/InventoryCountTileBase.cs`
   - Line 38: replaced `DateTime.UtcNow.AddDays(-DaysOffset)` with `_timeProvider.GetUtcNow().UtcDateTime.AddDays(-DaysOffset)` so the cutoff computation uses the injected `TimeProvider`, matching the date field on line 52 and making the filter deterministic under test.

2. `backend/test/Anela.Heblo.Tests/Features/Catalog/DashboardTiles/InventoryCountTileBaseTests.cs` (new)
   - `LoadDataAsync_ItemAtExactCutoff_IsIncluded` — item with `LastStockTaking` exactly at the cutoff is included.
   - `LoadDataAsync_ItemOneSecondBeforeCutoff_IsExcluded` — item one second before the cutoff is excluded.
   - `LoadDataAsync_ItemWithNullLastStockTaking_IsExcluded` — item with no stock-taking history is excluded.
   - `LoadDataAsync_CustomDaysOffset_ShiftsCutoff` — a subclass with a custom `DaysOffset` shifts the cutoff accordingly.
   - Uses `Microsoft.Extensions.Time.Testing.FakeTimeProvider` frozen at a fixed instant and a mocked `ICatalogRepository`, exercising the concrete `ProductInventoryCountTile` (and a local test-only subclass for the custom-offset case).

### Verification

- `dotnet build Anela.Heblo.sln` — succeeds (0 errors; pre-existing unrelated access-matrix-generation warning).
- `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~InventoryCountTileBaseTests"` — 4/4 passed.
- `dotnet format` — 0 files required formatting changes.
