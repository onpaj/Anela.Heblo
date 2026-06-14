Plan saved to `docs/superpowers/plans/2026-06-13-upcoming-production-tile-timeprovider.md`.

**Summary of the 9 tasks:**
1. Add `TimeProvider` field + ctor param to `UpcomingProductionTile`, replace `DateTime.UtcNow` (line 50) and `DateTime.Today` (lines 65, 69) with `_timeProvider.GetUtcNow()`-derived values.
2. Forward `timeProvider` to `base(...)` in `TodayProductionTile`.
3. Forward `timeProvider` to `base(...)` in `NextDayProductionTile`.
4. Verify subclass scope, build, format, commit production code.
5. New test file + `TodayProductionTile` weekly-drill-down test under `FakeTimeProvider` (Mon 2026-06-15).
6. `NextDayProductionTile` weekday test → `weekly` view (today+1 = Tue).
7. `NextDayProductionTile` Friday test → `grid` view (Mon skip via `GetNextWorkingDay`), correcting spec FR-4 misread per arch-review Amendment 1.
8. `lastUpdated` test asserts the value equals `FakeTimeProvider` time and has `DateTimeKind.Utc` (locks in `.UtcDateTime` choice from Decision 3).
9. Full filter run, format, commit tests.

All four arch-review spec amendments are incorporated. No DI changes required.