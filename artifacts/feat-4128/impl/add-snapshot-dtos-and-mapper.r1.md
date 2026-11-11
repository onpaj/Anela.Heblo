# Implementation: add-snapshot-dtos-and-mapper (r1)

Purely additive. Created in `Features/Manufacture/Contracts/`: ProductCatalogSnapshot, ProductStockSnapshot, ProductPropertiesSnapshot, ProductSalesRecordSnapshot, ProductConsumptionRecordSnapshot, ProductStockTakingSnapshot, ProductKind, StockTakingKind, plus StockSourceKind (mirror of Catalog StockSource so Available/Total are computed identically without referencing Catalog).
Created `Features/Catalog/Infrastructure/CatalogAggregateSnapshotMapper.cs` (internal static; `ToSnapshot(CatalogAggregate)`, `ToSnapshot(StockTakingRecord)`, `ToDomain(ProductStockTakingSnapshot)`; explicit enum switches throw on unknown).
Tests: `CatalogAggregateSnapshotMapperTests` (16 cases, all pass) and `ProductCatalogSnapshotBuilder` in tests/Features/Manufacture/TestHelpers.
Verification: `dotnet build test/Anela.Heblo.Tests` 0 errors; mapper tests 16/16 passed.
Note: member set is the design starting set plus Stock.Ordered and StockSourceKind; later tasks extend as compilation requires.
