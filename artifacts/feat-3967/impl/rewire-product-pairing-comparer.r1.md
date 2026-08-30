# Implementation: rewire-product-pairing-comparer

## What was implemented
Rewired `ProductPairingDqtComparer` off the Catalog-owned `IEshopStockClient`/`IErpStockClient` clients and `EshopStock`/`ErpStock`/`ProductType` domain types onto the DataQuality-owned contracts `IDqtEshopStockSource`/`IDqtErpStockSource` and their `DqtEshopStockItem`/`DqtErpStockItem` snapshot types added in earlier tasks of this pipeline. This removes the last direct cross-module dependency on Catalog domain types from the DataQuality module for this comparer. The `IsSellable` filter (previously a private static helper checking `ProductTypeId` against `ProductType.Goods`/`ProductType.Product`) is now read directly off `DqtErpStockItem.IsSellable`, which is computed in the Catalog-side adapter (`DataQualityErpStockSourceAdapter`, added in an earlier task).

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/ProductPairingDqtComparer.cs` — constructor and fields now depend on `IDqtEshopStockSource`/`IDqtErpStockSource`; removed the `using Anela.Heblo.Domain.Features.Catalog;` / `using Anela.Heblo.Domain.Features.Catalog.Stock;` imports and the private `IsSellable(ErpStock)` helper; comparison logic unchanged.
- `backend/test/Anela.Heblo.Tests/Features/DataQuality/ProductPairingDqtComparerTests.cs` — mocks `IDqtEshopStockSource`/`IDqtErpStockSource` instead of the Catalog clients; test data built from `DqtEshopStockItem`/`DqtErpStockItem` instead of `EshopStock`/`ErpStock`; `IsSellable` now set directly as a bool instead of via `ProductTypeId`.

## Tests
`backend/test/Anela.Heblo.Tests/Features/DataQuality/ProductPairingDqtComparerTests.cs` — 5 tests covering: all-paired (no mismatches), missing-in-ERP, missing-in-ERP-with-unresolved-pair-code, missing-in-Shoptet (sellable-only filter), and resilience-wrapping of both list calls.

## How to verify
- `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ProductPairingDqtComparerTests"` → `Passed! - Failed: 0, Passed: 5, Skipped: 0`
- `dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj` (transitively builds `Anela.Heblo.Application` and `Anela.Heblo.Domain`) → 0 errors.

## Notes
No deviations from the task-context spec. DI registration for `ProductPairingDqtComparer` in `DataQualityModule.cs` uses `services.AddScoped<IDriftDqtComparer, ProductPairingDqtComparer>()`, which resolves the new constructor dependencies automatically — no other call site needed updating.

## Status
DONE
