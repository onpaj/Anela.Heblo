### task: migrate-manufacture-tests-to-snapshot

**Files:** the ~20 test files under `backend/test/Anela.Heblo.Tests/Features/Manufacture/` that mock `IManufactureCatalogSource` or build `CatalogAggregate` (find with `grep -rln "CatalogAggregate" backend/test/Anela.Heblo.Tests/Features/Manufacture`), plus `ManufactureInventoryWriteDownServiceTests`, `ResidueDistributionCalculatorTests`, `BatchPlanningServiceTests`, `ConsumptionRateCalculator` tests.

- [ ] Replace `new CatalogAggregate { ... }` in Manufacture tests with `ProductCatalogSnapshotBuilder`; replace Catalog sale/consumed/stock-taking record fixtures with the snapshot record types.
- [ ] Keep assertions unchanged (behaviour must be identical); do not weaken any test.
- [ ] `dotnet test backend --filter "FullyQualifiedName~Features.Manufacture|FullyQualifiedName~Features.Catalog"` - all pass.
- [ ] Commit: `test(manufacture): use ProductCatalogSnapshot in Manufacture tests`.

