# Brief

## Module / File
`backend/src/Anela.Heblo.Application/Features/Purchase/DashboardTiles/PurchaseOrdersInTransitTile.cs`

## Coverage
Line coverage: 0.0% (filter threshold: 60%)

## What's not tested
`FormatAmountInThousands` has three branches: returns `"0"` for zero, an integer thousands string like `"5k"` when the thousands value has no fractional component, and a one-decimal string like `"5.3k"` otherwise. No tests verify the boundary between the integer and decimal branches (e.g. exactly 1000 vs. 1500 -> `"1k"` vs. `"1.5k"`), nor that zero returns `"0"` and not `"0k"`, nor that large values like 999999 round correctly.

## Why it matters
Incorrect formatting silently produces wrong amounts on the Purchases dashboard tile. The boundary between the integer and decimal path is an off-by-one risk; a wrong branch would display `"1.0k"` where `"1k"` is expected or vice versa.

## Suggested approach
Parameterised unit test covering: 0, 999, 1000, 1001, 1500, 9999, 10000. Assert the exact formatted string for each. ~30 min effort.

---
_Filed by weekly coverage-gap routine on 2026-09-14. Based on CI run #34699120372 (722ec6efc4c1f7e5db235606c763a2f2b4a9a374)._
