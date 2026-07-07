# Task Plan: Unit test coverage for InventorySummaryTileBase age-bucket logic

Single-task plan — this is a small, self-contained test-addition with no production code changes and no cross-cutting dependencies.

### task: add-inventory-summary-tile-tests

## Goal
Add unit tests for `InventorySummaryTileBase.LoadDataAsync` covering the age-bucket boundary logic (180-day and 365-day thresholds), the null-`LastStockTaking` "never" bucket, the `total` sum invariant, and the happy-path response shape. No production code changes.

## Context
- Target file under test: `backend/src/Anela.Heblo.Application/Features/Catalog/DashboardTiles/InventorySummaryTileBase.cs`
- `LoadDataAsync` buckets items by `(DateTime.UtcNow - item.LastStockTaking.Value).TotalDays` against `const double ThresholdCritical = 180` and `ThresholdWarning = 365`; items with `LastStockTaking == null` go to `never`.
- `CatalogAggregate.LastStockTaking` (`backend/src/Anela.Heblo.Domain/Features/Catalog/CatalogAggregate.cs`) is a computed, get-only property derived from `StockTakingHistory` (`List<StockTakingRecord>`, ordered by `Date` descending, first-or-default). It has no setter — fixtures must add a `StockTakingRecord` with the desired `Date`, or leave the list empty for `null`.
- Sibling reference test: `backend/test/Anela.Heblo.Tests/Features/Catalog/DashboardTiles/LowStockAlertTileTests.cs` — mirrors the `Mock<ICatalogRepository>` + `JsonSerializer`/`JsonDocument` assertion pattern to follow.
- Host subclass to exercise the base class logic: `ProductInventorySummaryTile` (`backend/src/Anela.Heblo.Application/Features/Catalog/DashboardTiles/ProductInventorySummaryTile.cs`), constructor `ProductInventorySummaryTile(ICatalogRepository catalogRepository)`. Its `ItemFilter` accepts `ProductType.Product`/`ProductType.Goods` and excludes other types (e.g. `ProductType.Material`), which is useful for the total/filter test.

## Files to create/modify
- `backend/test/Anela.Heblo.Tests/Features/Catalog/DashboardTiles/InventorySummaryTileBaseTests.cs` — new file, namespace `Anela.Heblo.Tests.Features.Catalog.DashboardTiles`.

## Implementation steps
1. Create the test class with a private fixture helper:
   ```csharp
   private CatalogAggregate CreateItem(string code, ProductType type, DateTime? lastStockTaking)
   {
       var item = new CatalogAggregate { ProductCode = code, Type = type };
       if (lastStockTaking.HasValue)
       {
           item.StockTakingHistory.Add(new StockTakingRecord { Date = lastStockTaking.Value });
       }
       return item;
   }
   ```
2. In each test, `Mock<ICatalogRepository>` with `Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(items)`, construct `new ProductInventorySummaryTile(repoMock.Object)`, call `await tile.LoadDataAsync()`.
3. Parse the result via `JsonSerializer.Serialize(result)` then `JsonDocument.Parse(...)`, and assert on `data.GetProperty("recent"|"medium"|"old"|"never"|"total").GetInt32()` and `status.GetString()`, following the exact pattern already used in `LowStockAlertTileTests`.
4. Capture `var now = DateTime.UtcNow;` once per test before building fixtures, and use `now.AddDays(-N)` offsets for boundary dates.

## Tests to write
1. `LoadDataAsync_ItemAt179Days_CountsAsRecent` — one item, `LastStockTaking = now.AddDays(-179)` → `recent == 1`, `medium == 0`, `old == 0`, `never == 0`.
2. `LoadDataAsync_ItemAt180Days_CountsAsMedium` — one item, `LastStockTaking = now.AddDays(-180)` → `medium == 1`, `recent == 0`.
3. `LoadDataAsync_ItemAt365Days_CountsAsMedium` — one item, `LastStockTaking = now.AddDays(-365)` → `medium == 1`, `old == 0`.
4. `LoadDataAsync_ItemAt366Days_CountsAsOld` — one item, `LastStockTaking = now.AddDays(-366)` → `old == 1`, `medium == 0`.
5. `LoadDataAsync_ItemWithNullLastStockTaking_CountsAsNever` — one item, no `StockTakingHistory` → `never == 1`, all other buckets `0`.
6. `LoadDataAsync_MixedItemsWithFilteredOutType_TotalExcludesFilteredItems` — one `recent`-bucket `Product`, one `medium`-bucket `Product`, one `Material`-type item (any age) → `total == 2` (excludes the Material item), and `total == recent + medium + old + never`.
7. `LoadDataAsync_HappyPath_ReturnsSuccessStatusAndExpectedShape` — small mixed set covering all four buckets (one item each) → `status == "success"`, `total == 4 == recent+medium+old+never`.

## Acceptance criteria
- All 7 tests above exist in `InventorySummaryTileBaseTests.cs`, pass, and fail if any bucket comparison operator or null-check in `InventorySummaryTileBase.LoadDataAsync` is reverted/mutated.
- No changes to any production (non-test) file.
- Full backend test suite passes (`dotnet test`), and `dotnet build` / `dotnet format` are clean.
