All tasks complete. Here is the output summary:

---

# Implementation: Inject TimeProvider into Manufacture Module Handlers

## What was implemented

Replaced all `DateTime.Now` calls in `GetManufactureOutputHandler` (3 reads) and `CalculateBatchPlanHandler` (1 read) with `_timeProvider.GetUtcNow().DateTime`. Both handlers now receive `TimeProvider` as the final constructor parameter and snapshot the clock once per invocation. Test fixtures were updated to use `Mock<TimeProvider>` via Moq, and time-shift tests were added to verify deterministic behavior.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureOutput/GetManufactureOutputHandler.cs` — added `_timeProvider` field + constructor param, replaced 3 `DateTime.Now` reads with single `now` snapshot
- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/CalculateBatchPlan/CalculateBatchPlanHandler.cs` — added `_timeProvider` field + constructor param, replaced 1 `DateTime.Now` read in `ResolveSalesRanges`
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/GetManufactureOutputHandlerTests.cs` — added `Mock<TimeProvider>` fixture, fixed `CreateTestManufactureHistory()` to use `FixedClock.DateTime`, added 2 time-shift tests
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/CalculateBatchPlanHandlerTests.cs` — added `Mock<TimeProvider>` fixture (removed errant `FixedTimeProvider` class), added 1 fallback-path time-shift test

## Tests

- `GetManufactureOutputHandlerTests` — 5 tests total (3 existing preserved + 2 new)
  - `Handle_UsesInjectedClock_ForDateRangeUpperBound` — verifies `GetHistoryAsync` receives `FixedClock.DateTime` as upper bound
  - `Handle_GapFillingLoop_TerminatesAtMonthOfInjectedClock` — verifies gap-filling produces exactly the 3 months `[2026-01, 2026-02, 2026-03]`
- `CalculateBatchPlanHandlerTests` — 5 tests total (4 existing preserved + 1 new)
  - `Handle_TimePeriodNullAndToDateNull_UsesInjectedClockAsEndDate` — verifies `DateRange.To == FixedClock.DateTime` and `From == FixedClock.DateTime.AddDays(-30)`

## How to verify

```bash
# Grep audit (should return empty)
grep -nE 'DateTime\.(Now|UtcNow|Today)' \
  backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureOutput/GetManufactureOutputHandler.cs \
  backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/CalculateBatchPlan/CalculateBatchPlanHandler.cs

# Build
dotnet build backend/Anela.Heblo.sln

# All 10 tests
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GetManufactureOutputHandlerTests|FullyQualifiedName~CalculateBatchPlanHandlerTests"
```

## Notes

- `CreateTestManufactureHistory()` in `GetManufactureOutputHandlerTests` was updated to use `FixedClock.DateTime` as the reference date (not `DateTime.Now`) because the handler now computes date ranges from the fixed clock; records anchored to the real wall clock would fall outside the gap-filling window and break the existing `Handle_ValidRequestWithHistory_ReturnsSuccessfulResponseWithData` assertion. This is correct and expected.
- 31 pre-existing Docker/integration test failures in other test classes are unrelated to this change and were already failing before this work.
- `DateTimeKind` audit: `IManufactureHistoryClient.GetHistoryAsync` and `DateRange` both treat `DateTime` as raw values with no `Kind`-dependent logic — confirmed safe to pass `DateTimeKind.Unspecified` from `DateTimeOffset.DateTime`.
- DI: `TimeProvider.System` was already registered as a singleton at `ServiceCollectionExtensions.cs:127` — no registration change required.

## PR Summary

Two handlers in the Manufacture module — `GetManufactureOutputHandler` and `CalculateBatchPlanHandler` — were the last outliers still calling `DateTime.Now` instead of using the module's `TimeProvider` abstraction. This change injects `TimeProvider` into both handlers, replaces all `DateTime.Now` call sites with a single `_timeProvider.GetUtcNow().DateTime` snapshot per invocation, and adds deterministic time-shift tests using `Mock<TimeProvider>`.

The concrete failure mode fixed: at 23:45 CET, `DateTime.Now` returns `2026-06-07 23:45` while UTC data is written as `2026-06-07 21:45`, producing off-by-one date ranges for late-evening queries. After this change both handlers are anchored to UTC regardless of server OS timezone.

`DateTimeKind` audit performed: `IManufactureHistoryClient.GetHistoryAsync` (Flexi adapter) and `DateRange` are both `Kind`-agnostic — no downstream consumer depends on `DateTimeKind.Local`, so the shift from `Local` to `Unspecified` is safe.

### Changes
- `GetManufactureOutputHandler.cs` — added `TimeProvider` ctor param + field, single `now` snapshot replaces 3 `DateTime.Now` reads
- `CalculateBatchPlanHandler.cs` — added `TimeProvider` ctor param + field, replaces 1 `DateTime.Now` read in `ResolveSalesRanges` fallback
- `GetManufactureOutputHandlerTests.cs` — `Mock<TimeProvider>` fixture, fixed helper dates to align with fixed clock, 2 new time-shift tests
- `CalculateBatchPlanHandlerTests.cs` — `Mock<TimeProvider>` fixture (removed errant custom `FixedTimeProvider` class), 1 new fallback-path time-shift test

## Status
DONE