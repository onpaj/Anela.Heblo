All 4 tasks complete. Let me write the final output summary.

---

# Implementation: Consolidate AnalyticsProduct Mapping — SalesHistory Projection

## What was implemented

Verified that PR #1805 already delivered all 5 FRs from the original spec (mapping helper exists on `CatalogAnalyticsSourceAdapter`, both call sites route through it, `SalesHistory` drift bug was already fixed). Executed the one justified follow-up identified by the arch review ("delta 1"): moved the duplicated `SalesHistory.Where(...).Select(...).ToList()` projection from both call sites into the `MapToAnalyticsProduct` helper, dropping the helper's `List<SalesDataPoint>` parameter.

## Files created/modified

- `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogAnalyticsSourceAdapterTests.cs` — added `GetProductAnalysisDataAsync_ExcludesSalesOutsidePeriodEvenWhenCallerPassesUnfilteredAggregate` regression test (commit `0844c42e`)
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogAnalyticsSourceAdapter.cs` — changed `MapToAnalyticsProduct` from 4-arg to 3-arg; moved `SalesHistory` projection inside; removed duplicate 6-line LINQ blocks from both call sites (commit `e145432e`)

## Tests

- `CatalogAnalyticsSourceAdapterTests.cs` — 9 tests pass (8 original + 1 new regression test)
- `ModuleBoundariesTests` — all pass; no boundary violations introduced
- Full solution: all tests pass

## How to verify

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/.worktrees/feat-arch-review-analytics-catalogaggregate-a
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~CatalogAnalyticsSourceAdapterTests|FullyQualifiedName~ModuleBoundariesTests" --no-build
```

Expected: all tests pass.

## Notes

Load-bearing decisions confirmed preserved: `M1_A` (not `M1`) for all three M1 fields, `MapProductType(product.Type)` (not raw `product.Type`), `marginData.Averages.M0.Amount` fallback for `MarginAmount`. The spec is now fully superseded by #1805 plus this consolidation.

## PR Summary

The original spec (extract `CatalogAggregate → AnalyticsProduct` mapping helper) was already delivered by PR #1805. This branch executes the one remaining consolidation the arch review identified: the duplicated `SalesHistory.Where(...).Select(...).ToList()` projection that still appeared at both call sites has been moved inside `MapToAnalyticsProduct`, completing the "one mapping site" intent.

A regression test was added first to pin the observable contract — `SalesHistory` is date-bounded — so the helper can never silently lose the filter again regardless of future call-site changes.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogAnalyticsSourceAdapter.cs` — changed `MapToAnalyticsProduct` from 4-arg (caller passes pre-filtered `List<SalesDataPoint>`) to 3-arg (helper owns the `Where/Select/ToList` projection); both call sites drop the duplicate 6-line LINQ block
- `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogAnalyticsSourceAdapterTests.cs` — added `GetProductAnalysisDataAsync_ExcludesSalesOutsidePeriodEvenWhenCallerPassesUnfilteredAggregate` to permanently pin the date-filter contract at the helper level

## Status

DONE