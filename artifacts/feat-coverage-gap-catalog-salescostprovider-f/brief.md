## Module / File
`backend/src/Anela.Heblo.Application/Features/Catalog/CostProviders/SalesCostProvider.cs`

## Coverage
Line coverage: 0% (filter threshold: 60%)

## What's not tested
The `ComputeAllCostsAsync` method distributes warehouse and marketing costs (cost centers SKLAD + MARKETING) across products proportionally by sold pieces. Key untested paths:
- **Zero sold-pieces guard**: when `totalSoldPieces == 0` the method falls back to empty costs for all products — if this guard is removed or its condition is wrong, every product silently gets 0 cost
- **Cost-per-piece calculation**: `totalCost / totalSoldPieces` feeds into monthly allocation; any rounding or type-casting issue is invisible
- **`FilterByProductCodes`**: filtering when `productCodes` is null (returns all) vs. a populated list (returns subset)
- **`GetDateRange` date math**: the costsTo end-of-month calculation uses `DateTime.DaysInMonth` — leap year and month-boundary edge cases are unchecked
- **Cache not-hydrated fallback**: `GetCostsAsync` returns an empty dictionary and logs a warning; caller-side impact is never asserted

## Why it matters
This provider calculates the M2 cost component used in product margin analysis. A silent regression in the allocation formula (e.g., totalSoldPieces check flipped, wrong date range) would produce incorrect cost data for every product without any error being raised.

## Suggested approach
Unit tests with mocked `ICatalogRepository` and `ILedgerService`:
1. Nominal: 3 products, known sales history, known warehouse+marketing costs → assert cost-per-piece allocation per product
2. Zero-sales guard: all sales = 0 → assert each product gets empty/zero monthly costs
3. Date range: verify `costsTo` ends on the last second of the month (check leap-year February)
4. FilterByProductCodes: pass a subset list, assert only those products are returned
Effort: ~1–2 hours

---
_Filed by weekly coverage-gap routine on 2026-06-14. Based on CI run #27416879267 (3a6b7f99ee715caf82fc0efa17de5a5ede7b46fb)._