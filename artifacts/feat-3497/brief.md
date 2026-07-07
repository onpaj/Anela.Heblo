# [coverage-gap] Catalog/InventorySummaryTileBase: age-bucket thresholds and never-inventoried count untested

## Module / File
`backend/src/Anela.Heblo.Application/Features/Catalog/DashboardTiles/InventorySummaryTileBase.cs`

## Coverage
Line coverage: 0% (filter threshold: 60%)

## What's not tested
The `LoadDataAsync` method computes four inventory-age buckets using two hardcoded thresholds (`ThresholdCritical = 180` days, `ThresholdWarning = 365` days):

- **Recent**: `LastStockTaking` < 180 days ago
- **Medium**: 180–365 days ago
- **Old**: > 365 days ago
- **Never**: `!LastStockTaking.HasValue`

No test verifies that items land in the correct bucket, that boundary values (exactly 180 or 365 days) are assigned correctly, or that items with a null `LastStockTaking` are counted in `never` rather than silently dropped from all buckets.

## Why it matters
These buckets drive the warehouse dashboard display. If the threshold comparison direction flips, or a null check is wrong, items migrate between buckets silently. The 180/365-day boundaries are business-defined constants that could be refactored without an obvious test failure.

## Suggested approach
- Unit-test `LoadDataAsync` with a mock `ICatalogRepository` returning items whose `LastStockTaking` is set to exactly the boundary values (180 days, 365 days, 179 days, 366 days) and one with `null`. Assert each item lands in the expected bucket.
- Verify that `total` equals the sum of all four bucket counts. ~0.5 day effort.

---
_Filed by weekly coverage-gap routine on 2026-07-06. Based on CI run #28716987459 (2ad2a2593e1834798a3def9ac2551b46c2e595cb)._
