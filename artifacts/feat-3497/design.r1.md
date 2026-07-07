# Design: Unit test coverage for InventorySummaryTileBase age-bucket logic

No UI/UX component — this is a backend-only unit test addition (per Architecture Review: `Skip Design: true`).

## Component Design

No new components. Tests exercise the existing `InventorySummaryTileBase.LoadDataAsync` via the existing concrete subclass `ProductInventorySummaryTile` (`backend/src/Anela.Heblo.Application/Features/Catalog/DashboardTiles/ProductInventorySummaryTile.cs`), constructed with a mocked `ICatalogRepository`. New test file only:

```
backend/test/Anela.Heblo.Tests/Features/Catalog/DashboardTiles/InventorySummaryTileBaseTests.cs
```

## Data Schemas

No schema changes. Test fixtures construct `CatalogAggregate` instances with `StockTakingHistory` (`List<StockTakingRecord>`) populated to control the derived `LastStockTaking` property:
- Boundary cases: `StockTakingHistory` = one record with `Date = now.AddDays(-N)` for N in {179, 180, 365, 366}.
- "Never" bucket case: `StockTakingHistory` left empty, so `LastStockTaking == null`.

`LoadDataAsync`'s existing JSON response shape (`status`, `data.recent`, `data.medium`, `data.old`, `data.never`, `data.total`) is unchanged and asserted via `JsonSerializer`/`JsonDocument`, matching the pattern in `LowStockAlertTileTests`.
