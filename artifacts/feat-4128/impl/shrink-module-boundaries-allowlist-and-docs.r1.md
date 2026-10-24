# shrink-module-boundaries-allowlist-and-docs r1

- Removed all `CatalogAggregate`, `CatalogSaleRecord`, `ConsumedMaterialRecord` entries and the history-handler / `CalculatedBatchSizeHandler+<>c` / `CalculateBatchByIngredientHandler+<>c` StockTakingRecord/Type entries from `ManufactureCatalogAllowlist`.
- Residual (deliberate, commented): SubmitManufactureStockTakingHandler ERP stock-taking types, `ManufactureStockTakingHistoryItemDto -> StockTakingType`, `CatalogManufactureRecord` entries.
- Rewrote the allowlist header comment.
- `Catalog -> Manufacture` rule was failing (Catalog.Infrastructure adapters/mapper reference Manufacture.Contracts snapshot DTOs, introduced by earlier tasks); added commented adapter-direction entries to `CatalogManufactureAllowlist`.
- Updated `docs/processes/calc-batch-planning.md` wording; regenerated process index.
- `dotnet test --filter ModuleBoundariesTests`: 40/40 passed. `dotnet format` could not load a workspace in this environment (edit is comments/string entries only).
