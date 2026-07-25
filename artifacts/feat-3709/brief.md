## Module / File
`backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetMaterialForPurchase/GetMaterialsForPurchaseHandler.cs`

## Coverage
Line coverage: 11.8% (4/34 lines — filter threshold: 60%)

## What's not tested
**Search term filter** — the case-insensitive `Contains` check on `ProductCode OR ProductName` is never exercised. If the condition were accidentally changed to AND, searches that match only name or only code would return empty results.

**No-history price fallback** — `item.PurchaseHistory.LastOrDefault()?.PricePerPiece` silently returns `null` when a material has no purchase history. No test verifies this produces a `null` `LastPurchasePrice` in the DTO rather than an exception.

**Limit applied after filter** — the `Take(request.Limit)` runs after the search filter. No test confirms that a restrictive search term returning fewer than `Limit` results doesn't get truncated, or that an empty search term still respects the limit.

## Why it matters
The search is the primary interaction path for purchase order creation. A regression in the OR logic or case normalization would silently return wrong results, and the no-history fallback masking an exception would be hard to diagnose in production.

## Suggested approach
Unit tests with a mocked `ICatalogRepository` (≈ low effort):
1. SearchTerm matching ProductCode only, ProductName only, and both.
2. Material with empty `PurchaseHistory` — verify `LastPurchasePrice` is null.
3. SearchTerm that narrows to fewer items than `Limit` — verify all matches are returned.

---
_Filed by weekly coverage-gap routine on 2026-07-20. Based on CI run #29525794843 (bba537b141de1dba71a2c6853c4ff3f7e96153b2)._
