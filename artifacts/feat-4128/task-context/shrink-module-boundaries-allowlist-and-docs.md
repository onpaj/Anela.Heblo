### task: shrink-module-boundaries-allowlist-and-docs

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`
- Modify (if they mention the contract): `docs/processes/module-manufacture*.md`, `docs/architecture/development_guidelines.md` example list

- [ ] Delete from `ManufactureCatalogAllowlist` all entries targeting `Catalog.CatalogAggregate`, `Catalog.Sales.CatalogSaleRecord`, `Catalog.ConsumedMaterials.ConsumedMaterialRecord`, and the `Catalog.Stock.StockTakingRecord` / `StockTakingType` entries that were only reachable via aggregate history (history handler `+<>c`, `CalculatedBatchSizeHandler+<>c`, `CalculateBatchByIngredientHandler+<>c`).
- [ ] Run `dotnet test --filter ModuleBoundariesTests`; for each remaining failure decide: is it a real remaining reference to fix, or a legitimate out-of-scope entry (ERP stock-taking types, `CatalogManufactureRecord`, `ManufactureStockTakingHistoryItemDto -> StockTakingType`)? Re-add only the latter, with a comment saying why and that it is a separate follow-up. Never re-add a `CatalogAggregate` entry.
- [ ] Rewrite the allowlist header comment (it currently describes the ProductCatalogSnapshot follow-up) to describe the remaining residual entries.
- [ ] If a process doc was touched run `python3 scripts/process-docs/check.py index`.
- [ ] Commit: `test(architecture): drop Manufacture->Catalog CatalogAggregate allowlist entries`.

