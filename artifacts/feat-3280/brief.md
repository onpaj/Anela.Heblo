## Module / File
backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/CreatePurchaseOrder/CreatePurchaseOrderRequestValidator.cs

## Coverage
Line coverage: 0% (filter threshold: 60%)

## What's not tested
The validator contains three custom private methods with branching logic that are entirely uncovered:
- `BeAValidDate`: returns false for null/empty or unparseable strings
- `NotBeTooFarInFuture`: rejects dates more than 30 days ahead of UTC now; also returns false if the string is null or unparseable
- `BeAfterOrEqualToOrderDate`: cross-field check that expected delivery date is on or after the order date; handles null/empty gracefully by returning true

Additionally, no test exercises the line-item sub-validator (`CreatePurchaseOrderLineRequestValidator`) — the quantity/unit-price bounds (0 < quantity ≤ 999999.99, unitPrice ≥ 0), the 100-line-items cap, or the MaterialId/Notes length limits.

## Why it matters
If any of these rules regress — e.g. the future-date window changes from 30 days, or the cross-field ordering check silently accepts invalid dates — bad purchase orders will reach the database without any rejection. The error messages would also drift without being caught.

## Suggested approach
Unit tests for the validator (no DB needed — FluentValidation is pure):
- Valid request with all fields populated → no errors
- OrderDate unparseable or empty → error on OrderDate
- OrderDate more than 30 days in future → error on OrderDate
- ExpectedDeliveryDate before OrderDate → error on ExpectedDeliveryDate
- ExpectedDeliveryDate = OrderDate → passes (boundary)
- More than 100 line items → error
- Line item with quantity ≤ 0 or > 999999.99 → error
Estimated effort: ~1 hour.

---
_Filed by weekly coverage-gap routine on 2026-06-22. Based on CI run #27941952679 (9463aa5983b2a6d201782725aeeaaba777d8c07d)._
