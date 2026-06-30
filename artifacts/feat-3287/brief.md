## Module / File
frontend/src/contexts/PurchasePlanningListContext.tsx

## Coverage
Line coverage: 54.2% (filter threshold: 60%)

## What's not tested
`addItem` has two silent no-op guards that are not covered by tests:
1. **Duplicate guard**: if a material with the same `productCode` already exists in the list, `addItem` returns the list unchanged — the caller receives no feedback. If this guard breaks, the same material could be added multiple times.
2. **Max-capacity guard**: if the list already has 20 items (`PURCHASE_PLANNING_LIST_MAX_ITEMS`), `addItem` silently no-ops. There is no notification to the user that the item was not added.

## Why it matters
The purchase planning list is used by buyers to collect materials for a purchase order. Silent no-ops are acceptable UX only if the guards are correct — if the duplicate check uses the wrong field or the cap is off-by-one, buyers could end up with duplicate entries or lose items they believe were added. The context is used across the purchase planning flow, so a broken guard affects the entire workflow.

## Suggested approach
Unit tests rendering the provider with React Testing Library:
- Add the same material twice → list still has one item
- Add 20 distinct materials, then a 21st → list stays at 20; the 21st is not present
- Add a material to an empty list → list has one item with correct fields
- `removeItem` on existing code → item removed; on non-existent code → list unchanged
- `clearList` → list becomes empty
Estimated effort: ~1 hour.

---
_Filed by weekly coverage-gap routine on 2026-06-22. Based on CI run #27941952679 (9463aa5983b2a6d201782725aeeaaba777d8c07d)._
