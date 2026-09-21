# Code Review: export-service-isalldy

## Summary
The implementation matches the task context line-for-line: `BuildEventBody` now reads `action.IsAllDay` directly, `IsDateOnly` is deleted, all three existing tests were updated to pass `isAllDay` explicitly, and the three new regression tests from Step 2 (spec FR-5) are present verbatim. The developer also caught and fixed a real regression in a pre-existing test that the task context didn't call out, and verified all 26 tests pass via the same documented out-of-scope-file verification workaround used by the two prior tasks in this plan.

## Review Result: PASS

### task: export-service-isalldy
**Status:** PASS

Verified against the task context file and spec FR-3/FR-5:
- `BuildEventBody` change matches Step 4 exactly: `var isAllDay = action.IsAllDay;` replacing the `IsDateOnly(action)` guess call.
- `IsDateOnly` method deleted in full, as instructed — diff confirms no remaining references.
- `BuildGraphEnd` is untouched, as the task context specified.
- Test file: `BuildAction` helper gained the `isAllDay` parameter with default `false`, in the exact position specified. The three existing tests (`CreateEventAsync_ForDateOnlyAction_SendsGraphsExclusiveEnd`, `CreateEventAsync_ForMultiDayDateOnlyAction_SendsGraphsExclusiveEnd`, `CreateEventAsync_ForTimedAction_SendsItsEndUnchanged`) now pass `isAllDay` explicitly, matching Step 2's required values.
- The three new tests from Step 2 (`CreateEventAsync_ForATimedMidnightToMidnightAction_DoesNotExportAsAllDay`, `ImportedTimedMidnightToMidnightEvent_ThenReExported_StaysTimed`, `ImportedGenuineAllDayEvent_ThenReExported_StaysAllDay`) are present and match the task context's code verbatim, including the exact-issue-scenario regression test and its genuine-all-day companion.
- Additional fix beyond the task context's explicit list: `CreateEventAsync_ThenImportingWhatWasSent_ReproducesTheOriginalDates` (a pre-existing theory test) is a direct casualty of removing the `IsDateOnly` guess — without an explicit `isAllDay` per `InlineData` row, the new `BuildAction` default (`false`) breaks the single-day and multi-day all-day round-trip cases. The developer's fix (adding an `isAllDay` theory parameter, set per the pre-existing row comments) is correct, minimal, and squarely within this task's Step 5 acceptance bar ("Run the tests to verify they pass"). This is the right call — leaving a regressed pre-existing test broken to stay narrowly inside the task context's line-numbered diff would be a worse outcome than the one extra parameter this required.
- Test run confirmed: `dotnet test ... --filter "FullyQualifiedName~OutlookCalendarSyncServiceTests" -c Release -p:UseSharedCompilation=false` → `Passed! - Failed: 0, Passed: 26, Skipped: 0, Total: 26`.
- Diff scope: `git status --short` shows only `artifacts/feat-4238/state.json`, `OutlookCalendarSyncService.cs`, and `OutlookCalendarSyncServiceTests.cs` — no files outside this task's declared scope are committed.

**On the verification workaround (same pattern already accepted for `persistence-migration-isalldy` and `import-mapper-isalldy`):** `Anela.Heblo.Application` and the Tests project still do not compile standalone at this point in the plan — `CreateMarketingActionHandler.cs`, `UpdateMarketingActionHandler.cs`, and the two repository test fixtures are `manual-handlers-isalldy`'s explicit responsibility. The developer's impl artifact documents patching those 4 files in memory with exactly the code `manual-handlers-isalldy`'s task context already prescribes, running the filtered suite, confirming 26/26 pass, then reverting via `cp` from pre-edit backups with a byte-for-byte `diff` confirmation before committing. `git status --short` corroborates no trace of the workaround remains. Consistent, sound, no scope creep.

No functional requirement is unmet, no architecture guideline is contradicted, and the required tests are present and pass.

## Docs to Update
(None — this task only changes internal export behavior; no public-facing behavior, CLI, or agent contract changed.)

## Overall Notes
Clean, surgical implementation. The one deviation from the task context's exact file list (the round-trip theory test fix) is well-justified, narrowly scoped, and necessary for this task's own stated acceptance criterion rather than scope creep. Ready for the next unit in the plan (`manual-handlers-isalldy`, per `state.json`).
