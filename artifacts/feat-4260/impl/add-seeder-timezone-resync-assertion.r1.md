# Implementation: add-seeder-timezone-resync-assertion

## What was implemented

Added a new xUnit test, `SeedDefaultConfigurationsAsync_WhenConfigurationExists_ResyncsTimeZoneIdFromMetadata`, to `RecurringJobSeederTests`. It asserts that when a `RecurringJobConfiguration` row already exists with a stale `TimeZoneId`, `RecurringJobSeeder.SeedDefaultConfigurationsAsync` re-syncs `TimeZoneId` from the job's `IRecurringJob.Metadata.TimeZoneId` on every seed run (not just on first creation). This closes the gap identified in arch-review amendment A-3: the guarantee that the runtime CRON-edit path and the startup discovery path use the same time zone rests on this re-sync behavior, and nothing else in the suite exercised it for an *existing* row.

The test uses `invoice-classification` (whose mock metadata carries `"America/New_York"`) seeded into the in-memory DB with a stale `"Europe/Prague"` value, then asserts the value is `"America/New_York"` after seeding.

## Files created/modified

- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobSeederTests.cs` — added the new test method, inserted between the existing `SeedDefaultConfigurationsAsync_WhenConfigurationExists_SetsLastModifiedByToSystem` test and `Dispose()`. No new `using` directives were needed.

## Tests

- `RecurringJobSeederTests.SeedDefaultConfigurationsAsync_WhenConfigurationExists_ResyncsTimeZoneIdFromMetadata` — new test asserting the re-sync behavior described above.

## How to verify

```bash
cd backend
dotnet build
dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~RecurringJobSeederTests"
```

Expected: build succeeds with 0 errors, and the test run reports `Passed! - Failed: 0, Passed: 6, Skipped: 0, Total: 6`.

## Notes

Build and test were run against the solution file at the repo root (`Anela.Heblo.sln`), not `backend/`, since that's where the `.sln` lives in this checkout. No deviations from the task spec; the test was inserted verbatim as specified in the task context file.

## Status
DONE

## PR Summary
Added a regression test to `RecurringJobSeederTests` that asserts `RecurringJobSeeder` re-syncs an existing row's `TimeZoneId` from job metadata on every seed run, closing a gap where only the "new row" seeding path was covered. This guards the invariant (per arch-review amendment A-3) that the runtime CRON-edit path and the startup discovery path always apply the same time zone.

### Changes
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobSeederTests.cs` — added `SeedDefaultConfigurationsAsync_WhenConfigurationExists_ResyncsTimeZoneIdFromMetadata`
