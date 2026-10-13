# Code Review: add-conversions-sync-service-tests

## Summary
The implementation adds `ConversionsSyncServiceTests.cs` exactly as specified in the task
context — five tests driving the real `ConversionsSyncService` end to end via the
`Ga4TestHarness.ConversionsSync`/`ConversionsRow` helpers added by the prior task. The full
`Anela.Heblo.Adapters.GoogleAnalytics.Tests` project (53 tests, including the 5 new ones) was
independently re-run and passes with 0 failures.

## Review Result: PASS

### task: add-conversions-sync-service-tests
**Status:** PASS

Verification notes:
- Test file matches the task context's Steps 1, 3, 5, 7, 9 verbatim; the class is correctly
  closed and all five `[Fact]` methods are present.
- FR-2 ((other)-bucket filtering, `RowsFetched` vs `RowsUpserted` divergence) →
  `drops_rows_whose_date_is_the_other_bucket_but_keeps_the_rest` — asserts `RowsFetched == 2`,
  `RowsUpserted == 1`, and exactly one surviving row via a second, independent `Ga4DbContext`.
- FR-3 (column-order-safe mapping) →
  `maps_dimensions_and_metrics_by_position_without_transposing_them` — uses distinguishable
  values (`Transactions=7`, `PurchaseRevenue=123.45m`, `ChannelGroup="Paid Search"`) that would
  fail on a transposition rather than pass by coincidence.
- FR-4 (insert / update-in-place / composite key) →
  `inserts_a_new_row_on_a_fresh_sync_window`, `updates_the_existing_row_in_place_on_a_resync_instead_of_duplicating`
  (two `SyncAsync()` calls over the same window, second call's revised values overwrite the
  first, still exactly one row), and `keeps_both_rows_when_channel_group_differs_on_the_same_date`
  (same `Date`, different `ChannelGroup`, both rows kept — proves the key is composite).
- FR-5 (`ChunkOutcome` tuple) → covered via the `RowsFetched`/`RowsUpserted` assertions in the
  FR-2 and composite-key tests, matching the spec's own note that FR-5 needs no separate test.
- NFR-1 (isolation/determinism): every test uses a fresh `NewDatabaseName()`, asserts through a
  second `Ga4DbContext` (never the context the service under test wrote through), and uses the
  fixed `Now`/`FixedTimeProvider` — no wall-clock or ordering dependency.
- NFR-2 (no production code changes): `git status --short` after the developer's local
  build-verification patch was reverted shows only the new test file and the checkpoint's
  `state.json` changed — `ConversionsSyncService.cs`, `Ga4ChunkUpsert.cs`, `Ga4ValueParser.cs`,
  and `Ga4EntitySyncServiceBase.cs` are untouched.
- Build/test verification: independently re-ran
  `dotnet test test/Anela.Heblo.Adapters.GoogleAnalytics.Tests/Anela.Heblo.Adapters.GoogleAnalytics.Tests.csproj`
  (full project, no filter) and confirmed `Passed! - Failed: 0, Passed: 53, Skipped: 0, Total: 53`.
  As with the prior task, this required the same temporary local workaround for the pre-existing,
  unrelated `RecurringJobSeeder.cs` compile error (confirmed present on `origin/main` too, not
  introduced by this branch) — patch, build/test, revert. The developer correctly did not fix
  that out-of-scope bug in the committed diff.
- `dotnet format --include` was run against the new test file with 0 files needing changes.

## Docs to Update
(none — this is internal test infrastructure, no public behavior changed)

## Overall Notes
Same overall note as the prior task's review: `RecurringJobSeeder.cs` line 51 (`existing` passed
where `existingConfig` was intended) blocks a full backend build for anyone on this branch or on
`main`. It remains correctly out of scope for this coverage-only issue but continues to warrant a
human-filed fix, since it blocks CI-equivalent local builds for the whole backend.
