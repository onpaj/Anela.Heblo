# Implementation: flip-contract-and-migrate-production-consumers (r1)

- `IManufactureCatalogSource` now returns `ProductCatalogSnapshot`; `CatalogManufactureCatalogSourceAdapter` maps via `CatalogAggregateSnapshotMapper` (GetByIds keeps omitting missing ids).
- Manufacture services/handlers migrated to `ProductCatalogSnapshot`, `ProductSalesRecordSnapshot`, `ProductConsumptionRecordSnapshot`, `ProductKind`.
- DTO members added as the compiler demanded: `ProductCatalogSnapshot.ManufactureDifficulty`, `ManufactureHistory` (new `ProductManufactureRecordSnapshot`: Date, Amount), `GetTotalSold`. `IProductionActivityAnalyzer` now takes `ProductManufactureRecordSnapshot`.
- `GetManufactureStockTakingHistoryHandler` works on `ProductStockTakingSnapshot`, mapping `StockTakingKind` back to `StockTakingType` (wire format unchanged).
- `SubmitManufactureStockTakingHandler`: the `product.SyncStockTaking(...)` cache-patch line is removed with a TODO; it is restored behind `IManufactureCatalogStockSync` in task `preserve-stock-taking-cache-patch` (history append on cached aggregate is temporarily absent; Stock/lots sync via `SyncErpStockTakingAsync` unchanged).
- Remaining Catalog usings in Manufacture: `Domain.Features.Catalog` in CalculateBatchPlanHandler/ResidueDistributionCalculator (template ingredient `ProductType` from the manufacture client, out of scope), `.Stock` usings (ERP stock taking / StockTakingType DTO), `ManufactureCatalogSourceAdapter` (other contract).
- Verification: `dotnet build` Anela.Heblo.Application 0 errors; `grep CatalogAggregate` in Manufacture only matches a doc comment. Test project does not compile yet (tests migrate in task `migrate-manufacture-tests-to-snapshot`).
