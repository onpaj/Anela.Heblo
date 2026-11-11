# Implementation: preserve-stock-taking-cache-patch (r1)

- `IManufactureCatalogStockSync.SyncErpStockTakingAsync` now takes a `ProductStockTakingSnapshot record`.
- `SubmitManufactureStockTakingHandler` maps the ERP record to the snapshot and passes it; no `CatalogAggregate` member is called any more. TODO removed.
- `CatalogManufactureStockSyncAdapter` maps with `CatalogAggregateSnapshotMapper.ToDomain` and passes it to `CatalogCacheStore.ApplyErpStockTakingAsync(..., StockTakingRecord? record = null)`, which appends it to the patched clone of the current and stale merged aggregates (idempotent by Id, same semaphore/guards, clone-then-swap).
- Investigation: history is only held on the merged aggregate (rebuilt by merge), so the append is not redundant; behaviour matches the pre-change `product.SyncStockTaking(record)` (history + Stock.Erp).
- Tests: updated handler test verifications (snapshot argument) and adapter test calls; added adapter test for single history append (idempotent) + Stock.Erp.
- Verification: `dotnet build` Application 0 errors; `dotnet format --verify-no-changes` clean. The test project still does not compile (other Manufacture tests migrate in `migrate-manufacture-tests-to-snapshot`); the touched test files contribute no new compile errors beyond the pre-existing `CatalogAggregate`-vs-snapshot mocks in the handler test file. Tests therefore could not be executed this round; they must pass in the next task.
