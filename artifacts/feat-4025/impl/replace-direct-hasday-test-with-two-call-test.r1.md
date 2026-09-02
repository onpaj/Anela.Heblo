# Implementation: replace-direct-hasday-test-with-two-call-test

## What was implemented
Replaced the test `HasDayAlreadyBeenProcessedAsync_ShouldReturnCorrectValue` (which called the
internal `HasDayAlreadyBeenProcessedAsync` method directly on the concrete service instance) with
`ProcessDailyConsumptionAsync_CalledTwiceForSameDate_SecondCallReturnsWasRunFalse`, which verifies
the same idempotency behavior indirectly through the public `ProcessDailyConsumptionAsync` API:
call it once (a genuine run), simulate the mock repository now reporting the date as processed,
call it a second time, and assert the second call returns `WasRun == false` with
`MaterialsProcessed == 0`. This unblocks the separate, later task of removing
`HasDayAlreadyBeenProcessedAsync` from `IConsumptionCalculationService` and making it private.

No changes were made to `IConsumptionCalculationService.cs` or `ConsumptionCalculationService.cs`
(explicitly out of scope for this task) — the new test still exercises the current interface
surface (only via the public `ProcessDailyConsumptionAsync` method, never the internal one).

The `MockPackingMaterialRepository.SetHasDailyProcessingBeenRun(DateOnly, bool)` helper already
existed (used by several sibling tests, e.g. `ProcessDailyConsumptionAsync_ReturnsWasRunFalse_WhenAlreadyProcessed`)
and needed no changes.

## Files created/modified
- `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/ConsumptionCalculationServiceTests.cs` — replaced the old direct-call test (previously at lines ~234-249) with the new two-call idempotency test, in place, at the same location. No other test in the file was touched.

## Tests
- `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/ConsumptionCalculationServiceTests.cs` — covers `ConsumptionCalculationService.ProcessDailyConsumptionAsync` (PerDay/PerOrder/PerProduct fact-row generation, idempotency/duplicate-run handling, zero-consumption daily runs, exception propagation). The new test adds coverage for calling `ProcessDailyConsumptionAsync` twice for the same date and asserting the second call is a no-op.
- Ran: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ConsumptionCalculationServiceTests"` from the repo root.
  - Result: **Passed! - Failed: 0, Passed: 12, Skipped: 0, Total: 12** (matches the file's 12 `[Fact]` methods after the replacement).
  - Confirmed no test named `HasDayAlreadyBeenProcessedAsync_ShouldReturnCorrectValue` remains (grep against the file and the test output both come back empty).

## How to verify
1. `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ConsumptionCalculationServiceTests"`
2. Confirm output shows `Passed: 12, Failed: 0` and no reference to `HasDayAlreadyBeenProcessedAsync_ShouldReturnCorrectValue`.
3. `git show --stat HEAD` on this branch should show exactly one file changed: `ConsumptionCalculationServiceTests.cs`.

## Notes
The sandbox's `dotnet build`/`dotnet test` for this solution has a reproducible hang: `Anela.Heblo.API.csproj`
runs a `GenerateAccessMatrix` MSBuild target (`BeforeTargets="Build"`, Debug-only) that shells out to
`dotnet run --project ../../tools/Anela.Heblo.AccessMatrixGen` via `<Exec>`. That nested `dotnet run`
shares MSBuild's persistent build-server nodes with the outer `dotnet test` invocation, which deadlocked
in this environment right after the generator finished writing its output files (confirmed via zero CPU-tick
progress across all involved processes over multiple samples — not just "slow"). Adding
`-m:1 /nodeReuse:false` (single build worker, no persistent node reuse) to the `dotnet test` invocation
reliably avoided the deadlock and let the build/test run to completion. This is a pre-existing
environment/build-config issue unrelated to the test change itself; flagging it here in case other
tasks in this pipeline hit the same stall running `dotnet build`/`dotnet test` on this solution.

## PR Summary
Swapped the packing-materials consumption test that called the internal `HasDayAlreadyBeenProcessedAsync`
method directly for one that verifies the same day-already-processed idempotency behavior through two
successive calls to the public `ProcessDailyConsumptionAsync` method, clearing the way for a later task
to make `HasDayAlreadyBeenProcessedAsync` private.

### Changes
- `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/ConsumptionCalculationServiceTests.cs` — replaced `HasDayAlreadyBeenProcessedAsync_ShouldReturnCorrectValue` with `ProcessDailyConsumptionAsync_CalledTwiceForSameDate_SecondCallReturnsWasRunFalse`.

## Status
DONE
