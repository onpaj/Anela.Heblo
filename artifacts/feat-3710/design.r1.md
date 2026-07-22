# Design: Deterministic cutoff date in InventoryCountTileBase

## Component Design
`InventoryCountTileBase.LoadDataAsync` (`backend/src/Anela.Heblo.Application/Features/Catalog/DashboardTiles/InventoryCountTileBase.cs`) is the only component touched. Its cutoff computation on line 38 changes from `DateTime.UtcNow.AddDays(-DaysOffset)` to `_timeProvider.GetUtcNow().UtcDateTime.AddDays(-DaysOffset)`, using the `TimeProvider` already injected via the constructor and already used for the `date`/`lastUpdated` fields. No new components, interfaces, or dependencies are introduced; the method's signature, return shape, and error handling are unchanged. New unit tests in `InventoryCountTileBaseTests.cs` exercise the existing concrete subclasses (`ProductInventoryCountTile`/`MaterialInventoryCountTile`), plus a minimal private test subclass for the custom-`DaysOffset` case, using `Moq` for `ICatalogRepository` and `FakeTimeProvider` to pin "now."

## Data Schemas
N/A — no schema or API change, internal method fix only.
