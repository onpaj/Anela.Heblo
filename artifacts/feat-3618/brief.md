## Module / File
`backend/src/Anela.Heblo.Application/Features/Manufacture/Validators/CalculateBatchByIngredientRequestValidator.cs`

## Coverage
Line coverage: 0.0% (filter threshold: 60%)

## What's not tested
The validator has no tests. The key gap is the `DesiredIngredientAmount` bounds check:

- Must be `> 0` — a zero or negative amount should be rejected
- Must be `<= 999999.99` — the upper cap prevents batch calculations from running against unreasonably large ingredient quantities

If either bound is silently broken (e.g., by a refactor of the validator base class or an accidental rule removal), invalid amounts would flow into the batch calculation service, potentially causing silent incorrect results or overflow.

Also untested: `ProductCode` and `IngredientCode` required + max 50 chars each.

## Why it matters
Batch calculations feed directly into manufacture planning (batch sizes, material consumption). An uncaught zero or negative ingredient amount could produce a division-by-zero or negative batch size downstream.

## Suggested approach
Unit tests on `CalculateBatchByIngredientRequestValidator`:
- `DesiredIngredientAmount = 0` → validation error
- `DesiredIngredientAmount = -1` → validation error
- `DesiredIngredientAmount = 999999.99` → no error (boundary)
- `DesiredIngredientAmount = 1000000` → validation error (over cap)
- `DesiredIngredientAmount = 100` → no error (happy path)
- `ProductCode = ""` → required error
- `IngredientCode = ""` → required error

~1 hour effort.

---
_Filed by weekly coverage-gap routine on 2026-07-13. Based on CI run #28968007617 (06d109fe5edcb456730222410f64385606100b1b)._
