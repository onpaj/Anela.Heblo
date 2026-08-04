# Development: Inject TimeProvider into TimePeriodResolver

## Summary

Implemented exactly what `design-01.md` (approved without changes in `architecture-01.md`) specified: `TimePeriodResolver` now takes `TimeProvider` via constructor injection and derives `now` from `_timeProvider.GetUtcNow().DateTime` instead of the ambient `DateTime.UtcNow`. The one existing test file that constructed the class directly was updated in lockstep to inject a `FakeTimeProvider` frozen at `2026-02-01T00:00:00Z` and assert exact `DateRange` values (previously only loose `BeBefore(DateTime.UtcNow)` ordering checks) for all five non-custom `TimePeriod` cases, including a month/quarter/season boundary case (the 1st of the month).

## Files changed

- `backend/src/Anela.Heblo.Application/Common/TimePeriods/TimePeriodResolver.cs`
  - Added `private readonly TimeProvider _timeProvider` field and a constructor taking `TimeProvider`.
  - Replaced `var now = DateTime.UtcNow;` with `var now = _timeProvider.GetUtcNow().DateTime;`.
  - No other lines changed — the five private `ResolveForX(DateTime now)` helpers, `ITimePeriodResolver`, `DateRange`, and `TimePeriod` are untouched.

- `backend/test/Anela.Heblo.Tests/Common/TimePeriods/TimePeriodResolverTests.cs`
  - Construction changed from `new TimePeriodResolver()` to `new(new FakeTimeProvider(FrozenNowUtc))` with `FrozenNowUtc = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero)`.
  - `Resolve_PreviousQuarter_ReturnsOneRange`, `Resolve_FutureQuarter_ReturnsOneRange`, `Resolve_Y2Y_ReturnsOneRange`, `Resolve_PreviousSeason_ReturnsOneRange`, `Resolve_Q9M_ReturnsTwoRanges` now assert exact `From`/`To` values (computed by hand and cross-checked in `architecture-01.md`) instead of `BeBefore(DateTime.UtcNow)`.
  - The four `CustomPeriod` tests are unchanged (they never depended on `now`).

No DI registration changes were needed: `ApplicationModule.cs:70` (`services.AddScoped<ITimePeriodResolver, TimePeriodResolver>();`) and the existing `services.AddSingleton(TimeProvider.System);` in `ServiceCollectionExtensions.cs` already compose correctly with the new constructor parameter.

## Verification performed

1. `grep -rn "new TimePeriodResolver(" backend/` — only the test file's target-typed `new(...)` construction remains; no other direct instantiation exists anywhere in `backend/`.
2. `dotnet build Anela.Heblo.sln` — succeeded, 0 errors (250 pre-existing nullable warnings in unrelated files, none introduced by this change).
3. `dotnet format Anela.Heblo.sln --include <the two changed files> --verify-no-changes` — exit code 0, no output (no formatting violations).
4. `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~TimePeriods|FullyQualifiedName~CalculateBatchPlanHandler|FullyQualifiedName~GetManufacturingStockAnalysisHandler" --no-build` — **Passed! Failed: 0, Passed: 20, Skipped: 0, Total: 20**. This covers all 9 `TimePeriodResolverTests` cases plus the two production consumer test suites, which mock `ITimePeriodResolver` directly and were confirmed unaffected.

## How to verify

```bash
cd backend
dotnet build
dotnet format --verify-no-changes
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~TimePeriods"
```
