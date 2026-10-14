# Implementation: add-ga4-harness-conversions-helpers

## What was implemented

Added two static test-harness helpers to `Ga4TestHarness`, mirroring the existing `TrafficSync`/`TrafficRow` pattern, so future tests can construct a `ConversionsSyncService` under test and build canned `Ga4Row` rows for it:

- `ConversionsSync(dbContext, client, options, now)` — builds a `ConversionsSyncService` wired to `Ga4SyncWatermarkRepository`, the in-memory `Ga4DbContext`, the given `Ga4SyncOptions`, and a `FixedTimeProvider`, logging via `NullLogger<ConversionsSyncService>.Instance`.
- `ConversionsRow(date, channelGroup, transactions, purchaseRevenue)` — builds a `Ga4Row` whose dimension/metric order matches `ConversionsSyncService`'s `Dimensions = ["date", "sessionDefaultChannelGroup"]` and `Metrics = ["transactions", "purchaseRevenue"]` exactly, documented with an XML doc comment explaining the index mapping.

Both were inserted immediately after the existing `TrafficRow` method, per the task context.

## Files created/modified

- `backend/test/Anela.Heblo.Adapters.GoogleAnalytics.Tests/Ga4TestHarness.cs` — added `ConversionsSync` and `ConversionsRow` static helpers.

## Tests

None added by this task — it only adds test-harness infrastructure. The follow-on task (`add-conversions-sync-service-tests`) is what actually exercises `ConversionsSyncService` using these helpers.

## How to verify

```
cd backend
dotnet build test/Anela.Heblo.Adapters.GoogleAnalytics.Tests/Anela.Heblo.Adapters.GoogleAnalytics.Tests.csproj
```

Expected: `Build succeeded.` with no errors.

## Notes

The solution-wide build for this test project transitively pulls in `Anela.Heblo.Application` (via the `Anela.Heblo.Adapters.GoogleAnalytics` project reference), which currently has a **pre-existing, unrelated compile error** on this branch (and on `origin/main`, confirmed via `git merge-base --is-ancestor`):

```
backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/RecurringJobSeeder.cs(51,45): error CS1503:
Argument 1: cannot convert from 'System.Collections.Generic.List<RecurringJobConfiguration>' to 'RecurringJobConfiguration'
```

This is unrelated to this task's scope (`RecurringJobSeeder` vs. GA4 conversions sync) and was **not** touched here, per the "surgical changes" rule. To confirm the new harness code itself compiles cleanly, I temporarily patched that one unrelated line locally (`existing` → `existingConfig` at line 51), ran the build (`0 Errors`), and then reverted the temporary patch before committing — `git diff --stat` confirms only `Ga4TestHarness.cs` (and the checkpoint's `state.json`) are changed in this commit. Flagging this for a human: `RecurringJobSeeder.cs` line 51 needs a real fix (use `existingConfig` instead of `existing`) — it currently blocks a full solution/backend build for anyone.

## PR Summary
Added `ConversionsSync` and `ConversionsRow` static helpers to `Ga4TestHarness`, following the existing `TrafficSync`/`TrafficRow` pattern, so the next task can write actual `ConversionsSyncService` tests against them.

### Changes
- `backend/test/Anela.Heblo.Adapters.GoogleAnalytics.Tests/Ga4TestHarness.cs` — added `ConversionsSync` and `ConversionsRow` helpers after `TrafficRow`

## Status
DONE_WITH_CONCERNS
