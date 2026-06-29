## Module / File
`backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisHandler.cs`

## Coverage
Line coverage: 30.8% (filter threshold: 60%)
2 existing test files: `GetPurchaseStockAnalysisHandlerTests` and `GetPurchaseStockAnalysisHandlerDiacriticsTests` (covers Czech diacritic normalisation in search).

## What's not tested

**Invalid date range guard**:
- When `request.FromDate > request.ToDate`, the handler returns `InvalidDateRange` with both dates in `Params`. No test covers this branch — a future refactor that accidentally inverts the check would allow impossible date windows to proceed to the catalog service.

**`ShouldIncludeItem` filter predicate**:
- All items from the snapshot are first analysed, then filtered via `ShouldIncludeItem` (request filters for severity, supplier, type, etc.) before paging. The `CalculateSummary` call, however, uses the **pre-filter** `allAnalysisItems` list — so summary totals intentionally reflect the full population, not the displayed subset. No test verifies this split-bucket design: a test with a request that filters out some items should still produce summary totals that include those items.

**Export / no-pagination path**:
- When `request.IsExport = true`, paging is skipped and all matched items are returned. No test exercises the export branch.

**`CalculateSummary` content**:
- The summary aggregates across all items regardless of the display filter. Its specific aggregation logic (counts by severity, total value, etc.) is not asserted in any test — a sign flip or off-by-one in the summary calculator would be undetectable.

## Why it matters
The dual-bucket design (filter for display, full set for summary) is a subtle intentional invariant. If it accidentally collapses to one bucket, dashboard totals would shift every time a user applies a filter — a confusing and hard-to-diagnose regression. The date guard is a cheap defensive check whose absence would surface as a 500 or a misleading empty result.

## Suggested approach
- Add a test with `FromDate = today, ToDate = yesterday` — assert `InvalidDateRange` error is returned.
- Add a test where snapshots include items that `ShouldIncludeItem` filters out — assert the response `Items` list is smaller than the summary represents.
- Add a test with `IsExport = true` and a large snapshot set — assert no paging is applied (all items returned).
- Add assertions on specific `Summary` fields (e.g. count of items in each severity bucket). ~1 day effort.

---
_Filed by weekly coverage-gap routine on 2026-06-29. Based on CI run #28295125598 (23c3b5d571c976074ee31869c96e29487098040c)._
