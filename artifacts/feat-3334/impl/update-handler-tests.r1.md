# Implementation: update-handler-tests

## What was implemented
Updated `GetProductMarginSummaryHandlerTests` to use `FakeTimeProvider` frozen at `2026-01-15 12:00:00 UTC`:
- Added `FrozenNow = new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero)` and `FrozenDate = new DateTime(2026, 1, 15)` constants
- Created `FakeTimeProvider(FrozenNow)` → `TimeWindowParser` in constructor
- Passed `TimeWindowParser` to `GetProductMarginSummaryHandler` constructor
- Removed all `DateTime.Today` references; replaced with `FrozenDate` / `FrozenDate.Year`
- Updated sales history dates to fall within the frozen range (Jan 2026)
- Fixed `Handle_WithMockedDependencies_InvokesCalculatorAndBreakdownGenerator` to pass `_timeWindowParser` to the local handler
- Added `ParseTimeWindow_UnknownValue_ThrowsArgumentException` test covering FR-4

## Files created/modified
- `backend/test/Anela.Heblo.Tests/Features/Analytics/GetProductMarginSummaryHandlerTests.cs` — replaced `DateTime.Today` with frozen `FakeTimeProvider`, added `TimeWindowParser` injection, added `ArgumentException` test

## Tests
All 8 tests pass: `dotnet test --filter "FullyQualifiedName~GetProductMarginSummaryHandlerTests"` → 8 passed, 0 failed

## How to verify
```bash
cd backend/test/Anela.Heblo.Tests
dotnet test --filter "FullyQualifiedName~GetProductMarginSummaryHandlerTests"
```
Expected: 8 passed, 0 failed

## PR Summary
Updates `GetProductMarginSummaryHandlerTests` to use `FakeTimeProvider` with a frozen date (2026-01-15), eliminating all `DateTime.Today` calls and making tests fully deterministic. Adds a test for the new `ArgumentException` path when an unknown time window string is passed.

## Status
DONE
