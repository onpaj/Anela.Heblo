# Implementation: remove-gc-collect

## What was implemented

Deleted the `GC.Collect();` call on line 36 of `CatalogAnalyticsSourceAdapter.cs`. This was a one-line deletion with no structural changes — the batch loop, `yield return`, and `cancellationToken.ThrowIfCancellationRequested()` are byte-for-byte identical to the pre-change state.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogAnalyticsSourceAdapter.cs` — removed `GC.Collect();` from the product batch loop

## Tests

No new tests written. The 9 existing tests in `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogAnalyticsSourceAdapterTests.cs` all pass without modification (verified: 9 passed, 0 failed, 0 skipped).

## How to verify

1. Confirm `GC.Collect` does not appear in `CatalogAnalyticsSourceAdapter.cs` — grep returns no matches.
2. `dotnet build` on the Application project and test project succeed with 0 errors.
3. `dotnet test --filter "FullyQualifiedName~CatalogAnalyticsSourceAdapter"` → 9/9 pass.

## Notes

Pre-existing build errors in `Anela.Heblo.Xcc.csproj` exist in the main checkout and are unrelated to this change. The Application and Tests projects build cleanly.

## PR Summary

Removes an explicit `GC.Collect()` call that was firing after every 100-product batch in `CatalogAnalyticsSourceAdapter.StreamProductsWithSalesAsync`. The call was unnecessary and harmful: .NET's GC already schedules collections based on allocation pressure and heap thresholds, so forcing a full-heap blocking collection on a fixed product-count boundary only added stop-the-world pauses with no benefit. Removing it restores correct GC behavior and improves throughput for analytics queries over large product catalogs.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogAnalyticsSourceAdapter.cs` — deleted `GC.Collect();` from the batch loop (line 36)

## Status
DONE
