## Module / File
`backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/SubmitStockTaking/SubmitStockTakingHandler.cs`

## Coverage
Zero tests. Not referenced in any test file.

## What's not tested
Four paths in `Handle`:
1. **Domain service returns an error** (`stockTakingRecord.Error` non-empty) — returns `StockTakingFailed` with product code and error details in the error dictionary; the stock-taking record is not persisted to the catalog
2. **Domain service succeeds, product not found in catalog** — stock-taking record is returned successfully but `product.SyncStockTaking()` is never called; no error is surfaced to the caller
3. **Domain service succeeds, product found** — calls `product.SyncStockTaking(stockTakingRecord)` to update local stock and add to history
4. **Exception from domain service** — returns `InternalServerError`

## Why it matters
Path 2 is a silent correctness hole: if the catalog lookup misses the product (e.g. stale code, casing mismatch), the eshop stock record is updated but the local catalog snapshot is not. The UI will show the operation as successful while the local stock data diverges. Without a test, this stays invisible.

## Suggested approach
Unit-test with mocked `ICatalogRepository` and `IEshopStockDomainService`. Four tests covering each path; for path 2 assert that `SyncStockTaking` is not called; for path 3 assert that it is. ~2 hours.

---
_Filed by weekly coverage-gap routine on 2026-06-08. Based on CI run #27104028537 (6568feba33640ae063b2cb6af3c81da31b3720e1)._