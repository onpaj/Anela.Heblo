## Module / File
`backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/RecalculateProductWeight/RecalculateProductWeightHandler.cs`

## Coverage
Line coverage: 17.1% (filter threshold: 60%)

## What's not tested
Three code paths are completely uncovered:

1. **`ProductCode` provided** → calls `RecalculateProductWeight(productCode)` on the service — i.e., single-product recalculation.
2. **`ProductCode` null/empty** → calls `RecalculateAllProductWeights()` — full catalog recalculation.
3. **Exception thrown** → catches `Exception`, returns `{ Success = false, ErrorCount = 1, ErrorMessages = ["Internal error: …"] }`.

Additionally, `Success = result.ErrorCount == 0` is computed from the service result but never asserted, so a change to that expression (e.g., a typo that always sets `Success = false`) would go undetected.

## Why it matters
The single-vs-all dispatch is the most dangerous gap: a regression could cause a request intended to recalculate one product to trigger a full catalog recalculation (expensive, slow, impactful) or vice versa. The exception path returning `ErrorMessages` is consumed by the frontend to display error feedback; if it stops being populated, users get a silent failure.

## Suggested approach
Unit tests with a mocked `IProductWeightRecalculationService`, covering:
- Non-empty `ProductCode` → assert `RecalculateProductWeight(code)` called, not `RecalculateAllProductWeights`
- Null/empty `ProductCode` → assert `RecalculateAllProductWeights` called
- Service throws → assert `Success = false`, `ErrorCount = 1`, `ErrorMessages` non-empty
- `result.ErrorCount == 1` → assert `Success = false` in response

~1–2 hours effort.

---
_Filed by weekly coverage-gap routine on 2026-07-13. Based on CI run #28968007617 (06d109fe5edcb456730222410f64385606100b1b)._
