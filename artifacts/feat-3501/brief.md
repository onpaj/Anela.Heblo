## Module / File
`backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/UpdatePurchaseOrder/UpdatePurchaseOrderRequestValidator.cs`

## Coverage
Line coverage: 0% (filter threshold: 60%)

## What's not tested
**`BeAReasonableDate` — dual-bound date check:**
The custom validator rejects delivery dates more than 2 years in the future OR more than 10 years in the past. No test exercises either boundary. The method contains three conditional branches (null passthrough, future max, past min) all of which are untested. A refactor that accidentally changes `AddYears(-10)` to `AddYears(-1)` would silently reject historical purchase order edits.

**100 line-items cap on `Lines`:**
`Must(lines => lines.Count <= 100)` is never tested. A purchase order with exactly 101 lines would be rejected in production, but no test confirms this limit exists.

**`UpdatePurchaseOrderLineRequestValidator` — quantity and unit price bounds:**
`Quantity GreaterThan(0)` and `LessThanOrEqualTo(999999.99m)`, and `UnitPrice GreaterThanOrEqualTo(0)` and `LessThanOrEqualTo(999999.99m)` are all untested boundary rules for the line items.

## Why it matters
Validation is the only guard before the purchase order reaches the database. If the date range validator incorrectly accepts or rejects values, legitimate orders could be blocked or invalid dates stored. The line count cap is a data integrity constraint.

## Suggested approach
- FluentValidation tests: a date exactly 2 years + 1 day in the future should fail; exactly 2 years should pass. Same pattern for the 10-year past bound.
- A request with 101 lines should fail validation; 100 lines should pass.
- Line items with Quantity = 0 and Quantity = 999999.99 boundary checks. ~0.5 day effort.

---
_Filed by weekly coverage-gap routine on 2026-07-06. Based on CI run #28716987459 (2ad2a2593e1834798a3def9ac2551b46c2e595cb)._
