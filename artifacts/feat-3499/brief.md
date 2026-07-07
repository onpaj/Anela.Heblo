## Module / File
`backend/src/Anela.Heblo.Application/Features/Catalog/CostProviders/DirectManufactureCostProvider.cs`

## Coverage
Line coverage: 0% (filter threshold: 60%)

## What's not tested
**`RefreshAsync` — concurrency guard:**
`RefreshAsync` uses a `SemaphoreSlim` with `WaitAsync(0)` to skip a refresh if one is already in progress. The skip path (log + return) is never tested. If the guard were accidentally removed, concurrent refreshes could compute costs against a stale or partially-written cache without any observed failure.

**`GetCostsAsync` — unhydrated cache fallback:**
When `cacheData.IsHydrated` is `false`, `GetCostsAsync` logs a warning and returns an empty dictionary instead of computing costs. No test verifies this path. A caller that receives an unexpectedly empty cost dictionary and treats it as "no data available" rather than "cache not ready yet" would silently produce wrong business outputs.

**`FilterByProductCodes`:**
The static filter that restricts results to a provided product-code list is also untested, including the null/empty passthrough.

## Why it matters
Direct manufacture costs flow into financial dashboards. A regressed concurrency guard could allow a refresh to run twice against the same incomplete catalog state. The unhydrated-cache path returns an empty result that callers must handle correctly — no test confirms they do.

## Suggested approach
- Test `RefreshAsync` called twice concurrently; assert the second call returns without invoking the repository.
- Test `GetCostsAsync` when the cache returns `IsHydrated = false`; assert the return value is an empty dictionary and a warning is logged.
- Test `FilterByProductCodes` with a null list (returns all), an empty list (returns all), and a specific list (returns only matching codes). ~1 day effort.

---
_Filed by weekly coverage-gap routine on 2026-07-06. Based on CI run #28716987459 (2ad2a2593e1834798a3def9ac2551b46c2e595cb)._
