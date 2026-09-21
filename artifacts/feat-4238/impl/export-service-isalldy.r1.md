# Implementation: export-service-isalldy

## What was implemented
`OutlookCalendarSyncService.BuildEventBody` now reads `action.IsAllDay` directly
instead of re-deriving it from `StartDate`/`EndDate` via the old `IsDateOnly`
guess. The guess method is deleted — it has no remaining callers. This closes
the round-trip data-corruption bug in #4238: a genuinely timed 24-hour action
whose dates happen to look like an all-day event (both at midnight) no longer
gets exported to Outlook as `isAllDay: true`.

The three existing date-only/timed export tests were updated to set `isAllDay`
explicitly (matching the task context), and spec FR-5's import→export
round-trip regression test was added as two new facts: a timed
midnight-to-midnight event that must stay timed through the round trip, and a
genuine all-day event that must stay all-day (companion, confirms no
overcorrection).

One additional fix beyond the task context: the pre-existing parameterized
test `CreateEventAsync_ThenImportingWhatWasSent_ReproducesTheOriginalDates`
built its `MarketingAction` without passing `isAllDay`, so it silently
inherited the new `BuildAction` helper's `isAllDay: false` default. For the
two all-day `InlineData` rows this defeated the round trip (the single-day
case exported with `isAllDay: false`, so Graph's echoed end equalled the
start, and re-import treated that as "no end date" per `ParseEndDate`'s
`evt.EndUtc == evt.StartUtc` branch — asserting `reimported.EndDate` against
the original end then failed). Added an explicit `isAllDay` parameter to the
theory and its two all-day `InlineData` rows, matching the existing per-row
comments (`// single-day all-day`, `// multi-day all-day`) that already
declared each row's intent.

## Files created/modified
- `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/OutlookCalendarSyncService.cs` — `BuildEventBody` reads `action.IsAllDay`; `IsDateOnly` deleted.
- `backend/test/Anela.Heblo.Tests/Marketing/OutlookCalendarSyncServiceTests.cs` — `BuildAction` helper gained an `isAllDay` parameter; three existing tests now pass it explicitly; three new regression tests added; the round-trip theory test now takes and forwards `isAllDay` per `InlineData` row.

## Tests
- `OutlookCalendarSyncServiceTests` (26 tests, all passing):
  - `CreateEventAsync_ForDateOnlyAction_SendsGraphsExclusiveEnd`, `CreateEventAsync_ForMultiDayDateOnlyAction_SendsGraphsExclusiveEnd`, `CreateEventAsync_ForTimedAction_SendsItsEndUnchanged` — updated to pass `isAllDay` explicitly.
  - `CreateEventAsync_ForATimedMidnightToMidnightAction_DoesNotExportAsAllDay` — new; the direct regression test for the issue.
  - `ImportedTimedMidnightToMidnightEvent_ThenReExported_StaysTimed` — new; the exact failure scenario end-to-end (import → re-export).
  - `ImportedGenuineAllDayEvent_ThenReExported_StaysAllDay` — new; companion, confirms genuine all-day events still round-trip correctly.
  - `CreateEventAsync_ThenImportingWhatWasSent_ReproducesTheOriginalDates` — pre-existing theory test, fixed to seed `isAllDay` per row so it continues to prove the export/import halves agree.

## How to verify
```
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~OutlookCalendarSyncServiceTests" -c Release -p:UseSharedCompilation=false
```
Expected: `Passed! - Failed: 0, Passed: 26, Skipped: 0, Total: 26`.

Note: `Anela.Heblo.Application` and the Tests project do not compile standalone
at this point in the plan — `CreateMarketingActionHandler.cs`,
`UpdateMarketingActionHandler.cs`, and two repository test fixtures still call
the `MarketingAction` constructor/`UpdateDetails` without the now-required
`isAllDay` argument. These are explicitly out of this task's file scope and
are `manual-handlers-isalldy`'s responsibility (its task context already
prescribes the exact fix for all 4 call sites). Verified test results above
by temporarily patching those 4 files in memory with exactly the code
`manual-handlers-isalldy` already specifies, running the filtered test suite,
confirming 26/26 pass, then reverting all 4 files via `cp` from pre-edit
backups and confirming byte-for-byte identity via `diff` before committing —
same pattern already used and accepted for `persistence-migration-isalldy`
and `import-mapper-isalldy`. No trace of the workaround is in the committed
diff (`git status --short` shows only the two files listed above, plus
`artifacts/feat-4238/state.json`).

## Notes
- Used `-c Release -p:UseSharedCompilation=false` for the verification build,
  following the pattern already established by the previous task
  (`import-mapper-isalldy`) to avoid the shared VBCSCompiler build-server
  deadlock observed on this shared machine.
- The `isAllDay` fix to the pre-existing round-trip theory test was not in
  the task context's Step 2 list, but is required for this task's own Step 5
  acceptance criterion ("Run the tests to verify they pass ... Expected:
  PASS") — without it, the pre-existing test regressed as a direct
  consequence of this task's `BuildEventBody` change.

## PR Summary
Fixed the round-trip data corruption in #4238: `OutlookCalendarSyncService.BuildEventBody` now reads the persisted `MarketingAction.IsAllDay` flag directly instead of guessing all-day-ness from whether both `StartDate` and `EndDate` sit at midnight. A genuinely timed 24-hour action (e.g. imported from a timed midnight-to-midnight Outlook event) no longer gets silently promoted to a multi-day all-day event on the shared calendar after one edit round-trip.

The obsolete `IsDateOnly` guess method was deleted. Three existing tests were updated to pass `IsAllDay` explicitly (matching the flag they were already implicitly relying on), and three new regression tests cover the issue's exact failure scenario end-to-end plus the two directions of the all-day/timed round trip.

### Changes
- `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/OutlookCalendarSyncService.cs` — read `action.IsAllDay` on export; deleted `IsDateOnly`
- `backend/test/Anela.Heblo.Tests/Marketing/OutlookCalendarSyncServiceTests.cs` — added `isAllDay` to the `BuildAction` test helper and three new regression tests; fixed the round-trip theory test's `isAllDay` seeding

## Status
DONE
