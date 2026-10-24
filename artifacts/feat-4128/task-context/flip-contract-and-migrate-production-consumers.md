### task: flip-contract-and-migrate-production-consumers

**Files:**
- Modify: `Features/Manufacture/Contracts/IManufactureCatalogSource.cs` (return types; drop Catalog using and "deliberate leak" comment)
- Modify: `Features/Catalog/Infrastructure/CatalogManufactureCatalogSourceAdapter.cs` (map via `CatalogAggregateSnapshotMapper`; `GetByIdsAsync` keeps omitting missing ids; update class comment)
- Modify Manufacture services: `Services/BatchPlanningService.cs`, `ResidueDistributionCalculator.cs`, `ManufactureInventoryWriteDownService.cs`, `ManufactureSeverityCalculator.cs`, `IManufactureSeverityCalculator.cs`, `ManufactureAnalysisMapper.cs`, `IManufactureAnalysisMapper.cs`, `ConsumptionRateCalculator.cs`, `IConsumptionRateCalculator.cs`
- Modify Manufacture handlers under `UseCases/`: CreateManufactureOrder, UpdateManufactureOrder, UpdateManufactureOrderStatus, CalculatedBatchSize, CalculateBatchPlan, CalculateBatchByIngredient, GetManufactureProtocol, GetStockAnalysis, GetManufactureOutput, GetManufactureStockTakingHistory, GetSemiproductRecipePdf (all under `backend/src/Anela.Heblo.Application/Features/Manufacture/`)
- Leave `SubmitManufactureStockTakingHandler` for the stock-sync task (it only needs a temporary compile fix here: keep behaviour, see below)

- [ ] Swap the interface return types, then fix compile errors file by file replacing `CatalogAggregate` with `ProductCatalogSnapshot`, `CatalogSaleRecord` with `ProductSalesRecordSnapshot`, `ConsumedMaterialRecord` with `ProductConsumptionRecordSnapshot`. Add only the DTO members the compiler demands.
- [ ] `GetManufactureStockTakingHistoryHandler`: operate on `ProductStockTakingSnapshot`; map `StockTakingKind` to the existing Catalog `StockTakingType` for `ManufactureStockTakingHistoryItemDto` so the wire format is unchanged.
- [ ] `SubmitManufactureStockTakingHandler`: temporarily read `Type`, `HasLots` from the snapshot and comment out nothing else; the `product.SyncStockTaking(...)` call is replaced in the next-but-one task. To keep behaviour meanwhile, perform the history append through the Catalog side only in task `preserve-stock-taking-cache-patch`; do not leave the build broken: if needed, do this task and that task in one commit.
- [ ] Remove every `using Anela.Heblo.Domain.Features.Catalog*` that becomes unused in Manufacture (retain `.Stock` usings still needed by the ERP stock-taking submit code).
- [ ] `dotnet build` of the Application project must be green. Verify: `grep -rn CatalogAggregate backend/src/Anela.Heblo.Application/Features/Manufacture` returns nothing.
- [ ] Commit: `refactor(manufacture): consume ProductCatalogSnapshot instead of CatalogAggregate`.

