## Module / File
`backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/ProcessDailyConsumption/ProcessDailyConsumptionHandler.cs`

## Coverage
Zero tests. Not referenced in any test file.

## What's not tested
Three paths:
1. **`result.WasRun == false`** — the service reports this date was already processed; handler returns `Success=false` with an idempotency message; no materials count is reported
2. **`result.WasRun == true`, `MaterialsProcessed == 0`** — processing ran but found no invoices; returns `Success=true` with a "no invoices found" message
3. **`result.WasRun == true`, `MaterialsProcessed > 0`** — normal success path; returns `Success=true` with a materials-updated message
4. **Exception** — returns `Success=false` with a generic error message (exception detail is suppressed from the response)

## Why it matters
The `WasRun == false` guard is the idempotency gate preventing double-processing of daily consumption. If it stops working (e.g. the service interface changes), the same day's consumption gets processed multiple times, producing inflated packing material deductions. No test currently asserts this guard fires correctly.

## Suggested approach
Unit-test with a mocked `IConsumptionCalculationService`. Four tests, one per path. Assert `Success`, `MaterialsProcessed`, and `Message` on each. ~1 hour.

---
_Filed by weekly coverage-gap routine on 2026-06-08. Based on CI run #27104028537 (6568feba33640ae063b2cb6af3c81da31b3720e1)._