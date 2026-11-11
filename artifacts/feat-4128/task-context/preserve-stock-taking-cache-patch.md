### task: preserve-stock-taking-cache-patch

**Files:**
- Modify: `Features/Manufacture/Contracts/IManufactureCatalogStockSync.cs`
- Modify: `Features/Catalog/Infrastructure/CatalogManufactureStockSyncAdapter.cs`, and `CatalogCacheStore.cs` if an append-history helper is needed
- Modify: `Features/Manufacture/UseCases/SubmitManufactureStockTaking/SubmitManufactureStockTakingHandler.cs`
- Tests: existing `SubmitManufactureStockTakingHandlerTests.cs`, existing stock-sync adapter tests (find with `grep -rl CatalogManufactureStockSyncAdapter backend/test`)

Background: before this change the handler called `product.SyncStockTaking(record)` on the shared cached aggregate (appends to `StockTakingHistory`, sets `Stock.Erp`); `ApplyErpStockTakingAsync` patches stock and lots only. See Decision 4 in the architecture review.

- [ ] Write failing tests: (1) handler passes a `ProductStockTakingSnapshot` mapped from the ERP record to `SyncErpStockTakingAsync`; (2) Catalog sync adapter, after a sync, makes the record visible in the cached aggregate's `StockTakingHistory` once (idempotent on repeated call with same Id) and `Stock.Erp` equals the new amount.
- [ ] Extend `SyncErpStockTakingAsync` with `ProductStockTakingSnapshot record`; in the Catalog adapter map with `CatalogAggregateSnapshotMapper.ToDomain` and append to the cached aggregates (current and stale merged caches, under the same guards `ApplyErpStockTakingAsync` uses).
- [ ] Remove `product.SyncStockTaking(...)` from the handler; handler no longer calls any `CatalogAggregate` member. Keep `ErpStockTakingRequest/Lot` usage (out of scope).
- [ ] If investigation shows history is rebuilt elsewhere making the append redundant, drop the extra argument instead and prove unchanged behaviour with a test; note the finding in the PR.
- [ ] Run `dotnet test --filter "SubmitManufactureStockTaking|StockSync"` - pass.
- [ ] Commit: `refactor(manufacture): move stock-taking history cache patch behind sync contract`.

