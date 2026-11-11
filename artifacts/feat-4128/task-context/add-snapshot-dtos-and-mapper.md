### task: add-snapshot-dtos-and-mapper

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Manufacture/Contracts/ProductCatalogSnapshot.cs`, `ProductStockSnapshot.cs`, `ProductPropertiesSnapshot.cs`, `ProductSalesRecordSnapshot.cs`, `ProductConsumptionRecordSnapshot.cs`, `ProductStockTakingSnapshot.cs`, `ProductKind.cs`, `StockTakingKind.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogAggregateSnapshotMapper.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogAggregateSnapshotMapperTests.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/Manufacture/TestHelpers/ProductCatalogSnapshotBuilder.cs`

- [ ] Write failing mapper tests first: scalar fields, `Type` enum mapping for every `ProductType` value (use `Enum.GetValues` to assert each maps and unknown throws), stock and properties (defaults such as `ExpirationMonths == 12` preserved), sales/consumed/stock-taking history incl. empty lists, derived `ProductFamily`/`SizeCode`/`LastStockTaking` equal to the `CatalogAggregate` values for short, 3-char, 6-char and 10-char codes.
- [ ] Create the DTO classes per `design.r1.md` (classes, `init` setters, no records, no `Anela.Heblo.Domain.Features.Catalog` using). Start from the member set in the design, then extend as later tasks need.
- [ ] Implement `CatalogAggregateSnapshotMapper.ToSnapshot(CatalogAggregate)` (single pass, materialise each history list once) and `ToDomain(ProductStockTakingSnapshot)` returning `StockTakingRecord`.
- [ ] Create `ProductCatalogSnapshotBuilder` (fluent `With...` methods; defaults equal to `new CatalogAggregate()` defaults).
- [ ] Run `dotnet test --filter CatalogAggregateSnapshotMapperTests` - expect pass; `dotnet build` green (purely additive).
- [ ] Commit: `feat(manufacture): add ProductCatalogSnapshot contract DTOs and Catalog mapper`.

