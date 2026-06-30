# Implementation: write-validator-tests

## What was implemented

Created `backend/test/Anela.Heblo.Tests/Features/Purchase/CreatePurchaseOrderRequestValidatorTests.cs` with two test classes totalling **46 tests**, all passing.

## Test classes

### CreatePurchaseOrderRequestValidatorTests (29 tests)
Covers `CreatePurchaseOrderRequestValidator`:
- `SupplierId`: valid (1, 100, long.MaxValue) and invalid (0, -1, long.MinValue) with error message check
- `OrderDate`: empty → error, invalid string → error with message, past date → pass, today → pass, +30 days → pass (boundary), +31 days → error with message
- `ExpectedDeliveryDate`: null → pass, empty → pass, invalid string → error, same as order date → pass, after order date → pass, before order date → error with message
- `Notes`: null → pass, 1000 chars → pass, 1001 chars → error with message
- `OrderNumber`: null → pass, 50 chars → pass, 51 chars → error with message
- `Lines`: null → pass, 0 items → pass, 100 items → pass (boundary), 101 items → error with message
- Full valid request → no errors

### CreatePurchaseOrderLineRequestValidatorTests (17 tests)
Covers `CreatePurchaseOrderLineRequestValidator`:
- `MaterialId`: empty → error, valid → pass, 50 chars → pass (boundary), 51 chars → error
- `Quantity`: 0 → error, negative → error, 0.01 → pass, 999999.99 → pass (boundary), 1000000 → error
- `UnitPrice`: -0.01 → error, 0 → pass, 999999.99 → pass (boundary), 1000000 → error
- `Notes`: null → pass, 500 chars → pass (boundary), 501 chars → error
- Full valid line → no errors

## Test results
```
Total tests: 46
     Passed: 46
```

## Date handling
Used `DateTime.UtcNow`-based helpers (`TodayStr`, `FutureStr(days)`, `PastStr(days)`) to avoid flakiness on date-sensitive rules.
