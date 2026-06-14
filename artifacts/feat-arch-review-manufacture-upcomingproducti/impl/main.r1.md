Both tasks complete. Here is the implementation summary:

---

# Implementation: TimeProvider Consistency in UpcomingProductionTile

## What was implemented

Eliminated hard-coded `DateTime.Today` and `DateTime.UtcNow` calls from the `UpcomingProductionTile` abstract base class by injecting `TimeProvider` through the constructor. Both subclasses now forward their existing `TimeProvider` argument to the base. All three wall-clock call sites are replaced with `_timeProvider.GetUtcNow()`-derived values. Four xUnit tests covering the drill-down and `lastUpdated` behavior under a `FakeTimeProvider` were added.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/Manufacture/DashboardTiles/UpcomingProductionTile.cs` — Added `private readonly TimeProvider _timeProvider` field, new 2-arg constructor, replaced `DateTime.UtcNow` with `_timeProvider.GetUtcNow().UtcDateTime` and both `DateTime.Today` calls with a `today` local variable computed once from `_timeProvider.GetUtcNow().Date`
- `backend/src/Anela.Heblo.Application/Features/Manufacture/DashboardTiles/TodayProductionTile.cs` — Changed `base(repository)` → `base(repository, timeProvider)`
- `backend/src/Anela.Heblo.Application/Features/Manufacture/DashboardTiles/NextDayProductionTile.cs` — Changed `base(repository)` → `base(repository, timeProvider)`
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/DashboardTiles/UpcomingProductionTileTests.cs` — New file with 4 xUnit tests using `FakeTimeProvider`

## Tests

`UpcomingProductionTileTests.cs` — 4 tests, all passing:

1. `TodayProductionTile_GenerateDrillDownFilters_ReturnsWeeklyView` — Mon 2026-06-15, today branch returns `weekly`
2. `NextDayProductionTile_OnWeekday_GenerateDrillDownFilters_ReturnsWeeklyView` — Mon 2026-06-15, today+1 (Tue) branch returns `weekly`
3. `NextDayProductionTile_OnFriday_GenerateDrillDownFilters_ReturnsGridView` — Fri 2026-06-19, `GetNextWorkingDay` skips to Mon June 22, falls through to `grid`
4. `LoadDataAsync_LastUpdated_ComesFromTimeProvider` — asserts `lastUpdated` equals frozen time and has `DateTimeKind.Utc`

All 18 DashboardTiles tests pass (4 new + 14 pre-existing).

## How to verify

```bash
cd backend && dotnet build
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Features.Manufacture.DashboardTiles"
```

## Notes

- Commits: `ab5acd8b` (production code), `716d7a23` (tests)
- `lastUpdated` uses `.UtcDateTime` (not `.DateTime`) per arch-review Decision 3 — semantically unambiguous `DateTimeKind.Utc`
- No DI changes required — `TimeProvider.System` already framework-registered; `ManufactureModule.cs` is untouched
- Spec FR-4 misread corrected per arch-review Amendment 1: `NextDayProductionTile` returns `weekly` on weekday (today+1), `grid` on Friday (skips to Monday)
- `FakeTimeProvider` pattern matches `ProductionActivityAnalyzerTests` convention (not `Mock<TimeProvider>`)

## PR Summary

Inject `TimeProvider` into `UpcomingProductionTile` base class, eliminating the last wall-clock `DateTime.Today`/`DateTime.UtcNow` calls in the Manufacture dashboard tile layer.

The base class previously compared `ReferenceDate` (sourced from the injected `TimeProvider` in subclasses) against `DateTime.Today` (wall-clock) in `GenerateDrillDownFilters()`, making the returned drill-down view type non-deterministic in time-shifted tests. `lastUpdated` also used `DateTime.UtcNow` directly. Both call sites now use `_timeProvider.GetUtcNow()`, and both subclasses forward their existing `TimeProvider` to `base(...)`. No DI changes are needed; `TimeProvider.System` is already framework-registered.

### Changes
- `backend/src/.../DashboardTiles/UpcomingProductionTile.cs` — added `_timeProvider` field, 2-arg constructor, replaced clock calls at line 50 and lines 65/69
- `backend/src/.../DashboardTiles/TodayProductionTile.cs` — forwarded `timeProvider` to `base(repository, timeProvider)`
- `backend/src/.../DashboardTiles/NextDayProductionTile.cs` — forwarded `timeProvider` to `base(repository, timeProvider)`
- `backend/test/.../DashboardTiles/UpcomingProductionTileTests.cs` — new; 4 tests covering drill-down weekday/Friday branches and `lastUpdated` UTC kind under `FakeTimeProvider`

## Status

DONE