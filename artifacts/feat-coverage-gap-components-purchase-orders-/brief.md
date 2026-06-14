## Module / File
`frontend/src/components/purchase-orders/form/PurchaseOrderValidation.tsx`

## Coverage
Zero tests. `PurchaseOrderHelpers.test.tsx` covers the calculation helpers in the sibling file but does not import or test `validateForm` or `clearFieldError`.

## What's not tested
Five validation rules in `validateForm`:
1. **Missing order number** — required field, trimmed check
2. **Missing supplier** — null/undefined check
3. **Missing order date** — null/undefined check
4. **Expected delivery before order date** — date string comparison (`new Date(expectedDeliveryDate) < new Date(orderDate)`); the only cross-field rule
5. **Per-line validation** — each line is validated only when `selectedMaterial.productName` is truthy (partial rows are silently skipped); for valid-material rows: quantity must be > 0, unit price must be > 0

Also untested: `clearFieldError` — returns a new object with the field deleted, or returns the original object if the field wasn't present (referential equality branch).

## Why it matters
The delivery-date comparison is the most subtle: it uses string-constructed `Date` objects, which can behave unexpectedly with timezone edge cases. If broken, orders with impossible date ranges (delivery before order date) silently pass validation and reach the backend. The per-line conditional gate means invalid prices on rows without a material selection are silently ignored — a test documents this intent and prevents accidental removal.

## Suggested approach
Pure unit tests with `vitest` / Jest, no component rendering needed. ~6 test cases: each required field missing; delivery < order date; delivery > order date (valid); line with no material skipped; line with material + invalid quantity; `clearFieldError` both branches. ~1 hour.

---
_Filed by weekly coverage-gap routine on 2026-06-08. Based on CI run #27104028537 (6568feba33640ae063b2cb6af3c81da31b3720e1)._