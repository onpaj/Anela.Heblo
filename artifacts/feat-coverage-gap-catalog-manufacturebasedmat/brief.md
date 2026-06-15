## Module / File
`backend/src/Anela.Heblo.Application/Features/Catalog/CostProviders/ManufactureBasedMaterialCostProvider.cs`

## Coverage
Line coverage: 25% (filter threshold: 60%)

## What's not tested
`CalculateMaterialCosts` routes by product type and `CalculateFromManufactureHistory` implements a temporal carry-forward algorithm. Untested paths:
- **Product-type routing**: Set/Product/SemiProduct use manufacture history; all other types fall back to purchase price with VAT — the branch guard is unchecked
- **Carry-forward (no gap months)**: when a month has no manufacture entry, the last-known price is carried forward; verifying the sequence is never asserted
- **Future-manufacture backfill**: months before the first manufacture record use the earliest future price — the `futureManufacture != default` branch is uncovered
- **No-history fallback**: products with empty `ManufactureHistory` fall back to purchase-price path — untested
- **Zero/null purchase price guard**: `CalculateFromPurchasePriceWithVat` returns empty list when price is null or ≤ 0 — never exercised
- **Weighted average calculation**: `Sum(PricePerPiece * Amount) / Sum(Amount)` across grouped month records — any overflow or divide-by-zero is uncaught

## Why it matters
This is the M0 cost source feeding product margin calculations. The carry-forward logic silently propagates stale prices across months. A wrong branch (e.g., type check widened or carry-forward skipped) would corrupt historical cost data for all manufactured products without raising an exception.

## Suggested approach
Unit tests with mocked `ICatalogRepository`:
1. Manufactured product with gaps → verify carry-forward fills missing months with last-known price
2. Manufactured product with future manufacture only → verify backfill from first future record
3. Non-manufactured product (type = Material) → verify purchase-price path is used
4. Manufactured product with no history → verify falls back to purchase-price path
5. Purchase price null → verify empty list returned
Effort: ~2–3 hours

---
_Filed by weekly coverage-gap routine on 2026-06-14. Based on CI run #27416879267 (3a6b7f99ee715caf82fc0efa17de5a5ede7b46fb)._