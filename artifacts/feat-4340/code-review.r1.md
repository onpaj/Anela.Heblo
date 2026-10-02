## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- None

### Notes
- Verified the diff is additive/test-only: `ConversionsSyncService.cs`, `Ga4ChunkUpsert.cs`, `Ga4ValueParser.cs`, and `Ga4EntitySyncServiceBase.cs` are untouched, matching spec NFR-2.
- Cross-checked `Ga4TestHarness.ConversionsSync`/`ConversionsRow` against `ConversionsSyncService`'s `Dimensions`/`Metrics` arrays and `Ga4DbContext`'s composite key (`Date`, `ChannelGroup`) — the positional mapping and upsert key used by the new tests match production code exactly.
- Walked each test (`drops_rows_whose_date_is_the_other_bucket_but_keeps_the_rest`, `maps_dimensions_and_metrics_by_position_without_transposing_them`, `inserts_a_new_row_on_a_fresh_sync_window`, `updates_the_existing_row_in_place_on_a_resync_instead_of_duplicating`, `keeps_both_rows_when_channel_group_differs_on_the_same_date`) against `Ga4EntitySyncServiceBase.SyncAsync`'s watermark/window logic (`StartDateFor`, `Yesterday`, `TrailingReprocessDays`) and `Ga4ChunkUpsert.ReplaceRangeAsync`'s insert/update/delete semantics — the fixtures line up with the real windowing and upsert behavior, including the two-run resync case where both runs target the same single day.
- Could not get a locally green `dotnet test` run for this test project: `Anela.Heblo.Adapters.GoogleAnalytics.Tests` → `Anela.Heblo.Adapters.GoogleAnalytics` → `Anela.Heblo.Application`, and `Anela.Heblo.Application` currently fails to build on `main` at the merge-base (`RecurringJobSeeder.cs:51`, CS1503 passing a `List<RecurringJobConfiguration>` where a single `RecurringJobConfiguration` is expected). This is a pre-existing break unrelated to this diff, already filed separately as issue #4346, and this PR's diff does not touch that file. Once #4346 is fixed, this PR's tests should build and run cleanly based on the source review above.
