# Implementation: import-mapper-isalldy

## What was implemented

Updated `OutlookEventImportMapper` so it sets and compares `MarketingAction.IsAllDay` using
Graph's own `isAllDay` flag on the imported event, instead of leaving the property unset
(previous behaviour, since the flag didn't exist before `domain-isalldy-property`). This is
the exact fix for the issue's failure scenario: a genuinely timed 24-hour event whose dates
happen to look like an all-day event (midnight to midnight) must not be recorded as all-day,
because Graph says `isAllDay: false`.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/Marketing/Services/OutlookEventImportMapper.cs`
  — `BuildAction` now passes `isAllDay: evt.IsAllDay` to the `MarketingAction` constructor;
  `HasChanges` now includes `existing.IsAllDay != evt.IsAllDay` in its comparison; `ApplyChanges`
  now passes `isAllDay: evt.IsAllDay` to `UpdateDetails`. `ParseEndDate` is unchanged (it already
  consumed `evt.IsAllDay` correctly for the exclusive→inclusive conversion).
- `backend/test/Anela.Heblo.Tests/Features/Marketing/OutlookEventImportMapperTests.cs` —
  added `BuildAction_SetsIsAllDayFromGraphsFlag_ForAllDayEvent`,
  `BuildAction_SetsIsAllDayFromGraphsFlag_ForTimedMidnightToMidnightEvent` (the exact failure
  scenario from the issue), and `HasChanges_ForToggledIsAllDayWithUnchangedDates_ReportsAChange`.
  Updated the existing `HasChanges_ForAlreadyImportedAllDayEventWithExclusiveEnd_ReportsAChange`
  test's `MarketingAction` seed to pass `isAllDay: false` (it represents a row written by the
  old mapper, which never set `IsAllDay`).

## Tests

`OutlookEventImportMapperTests.cs` — all 7 tests (4 pre-existing + 3 new) cover: all-day
import sets `IsAllDay = true`; a timed midnight-to-midnight import sets `IsAllDay = false` and
keeps `EndDate` untouched (not exclusive-to-inclusive-converted); `HasChanges` reports a change
when only `IsAllDay` toggles with dates unchanged; the pre-existing all-day/multi-day/timed
`BuildAction` date-shape tests are unaffected.

## How to verify

`cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~OutlookEventImportMapperTests"`
→ `Passed! - Failed: 0, Passed: 7, Skipped: 0, Total: 7`.

Verification note: at this point in the plan, `Anela.Heblo.Application` and the Tests project
do not compile standalone — `CreateMarketingActionHandler.cs`, `UpdateMarketingActionHandler.cs`,
`MarketingActionRepositoryGetPagedTests.cs`, and `MarketingActionRepositoryGetSyncedInWindowTests.cs`
still call the `MarketingAction` constructor/`UpdateDetails` without the now-required `isAllDay`
argument. These 4 call sites are explicitly out of this task's declared file scope and are the
responsibility of the dependent task `manual-handlers-isalldy` (same pattern documented in
`persistence-migration-isalldy`'s own review). To actually run the tests above and confirm they
pass, I temporarily patched those 4 files in place (matching exactly the code `manual-handlers-isalldy`'s
task context already prescribes for them), ran the filtered test suite, confirmed
`Passed! - Failed: 0, Passed: 7`, then reverted all 4 files via `cp` from pre-edit backups and
confirmed via `diff` that each is byte-for-byte identical to its pre-patch state before committing.
No trace of the temporary patch is in this commit's diff (`git status --short` shows only
`state.json`, `OutlookEventImportMapper.cs`, and `OutlookEventImportMapperTests.cs`).

Environment note: the shared-machine VBCSCompiler build server repeatedly deadlocked under
concurrent load from other worktrees' pipeline runs during this verification (zero CPU progress
for minutes while `futex_do_wait`). Worked around with `-p:UseSharedCompilation=false -c Release`
(Release skips the Debug-only `GenerateAccessMatrix` `dotnet run` Exec target that triggered the
Domain-rebuild cascade coinciding with the deadlocks) — this is a build-tooling workaround only,
not a code change.

## Notes

No deviations from the task context. `ParseEndDate` was left untouched as instructed — it
already consumed `evt.IsAllDay` correctly.

## PR Summary
Fixes the import half of #4238: `OutlookEventImportMapper` now sets `MarketingAction.IsAllDay`
directly from Graph's own `isAllDay` flag on import, and includes it in the update-detection
comparison, instead of leaving the flag unset. This stops a genuinely timed 24-hour event from
losing its "timed" identity on import just because its dates happen to look like an all-day
event.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Marketing/Services/OutlookEventImportMapper.cs` — `BuildAction`, `HasChanges`, `ApplyChanges` now set/compare `IsAllDay` from `evt.IsAllDay`
- `backend/test/Anela.Heblo.Tests/Features/Marketing/OutlookEventImportMapperTests.cs` — 3 new tests, 1 existing test updated for the new required constructor parameter

## Status
DONE
