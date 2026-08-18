## Module / File
`backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/DeleteManufactureDifficulty/DeleteManufactureDifficultyHandler.cs`

## Coverage
Line coverage: 23.7% (filter threshold: 60%)

## What's not tested
1. **Not-found path** — when the difficulty entry does not exist, the handler returns success=false with a descriptive message. No test covers this.
2. **Delete + cache invalidation cascade** — after `DeleteAsync`, the handler calls `RefreshManufactureDifficultySettingsData` on the catalog repository to keep the `CatalogAggregate` consistent. No test verifies both calls happen in sequence, or that the cache refresh receives the correct `productCode` from the deleted entry.
3. **Exception path** — if either `DeleteAsync` or `RefreshManufactureDifficultySettingsData` throws, the handler returns a failure response. This path is untested.

## Why it matters
If the cache-refresh call is accidentally dropped or called with the wrong product code, the catalog aggregate retains stale difficulty data and downstream pricing calculations use incorrect coefficients. This would be a silent data-quality regression. The not-found guard ensures callers get a clear failure instead of a repository exception.

## Suggested approach
Unit test with mocked `IManufactureDifficultyRepository` and `ICatalogRepository`:
- Case: GetByIdAsync returns null → response.Success == false
- Case: entry found → DeleteAsync called, then RefreshManufactureDifficultySettingsData called with the entry's productCode, response.Success == true
- Case: DeleteAsync throws → response.Success == false, exception not rethrown
~1 h effort.

---
_Filed by weekly coverage-gap routine on 2026-08-17. Based on CI run #31804633307 (6f781d410eb84616c8decb088d6d18cd1de01fb8)._
