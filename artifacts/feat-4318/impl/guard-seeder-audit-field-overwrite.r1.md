# Implementation: guard-seeder-audit-field-overwrite

## What was implemented

`RecurringJobSeeder.SeedDefaultConfigurationsAsync` no longer calls `UpdateConfiguration`/
`UpdateAsync` for an existing job row when none of the developer-owned, code-sourced fields
(`DisplayName`, `Description`, `TimeZoneId`) differ from what is stored. This preserves
`LastModifiedAt`/`LastModifiedBy` (an admin's real CRON/enable-disable audit trail) across
every no-op seed pass (e.g. every app restart). When at least one of those three fields does
differ, behavior is unchanged: the row is updated and audit fields are stamped to `"System"` /
the current seed time. `CronExpression` and `IsEnabled` remain excluded from the comparison and
remain preserved exactly as stored in all cases — no regression there.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/RecurringJobSeeder.cs` —
  replaced the unconditional `else { UpdateConfiguration(...) }` branch with
  `else if (HasSeededFieldsChanged(existing, config)) { ... }`, and added the private static
  `HasSeededFieldsChanged` helper that compares `DisplayName`, `Description`, and `TimeZoneId`
  only. The `existing == null` (`AddAsync`) branch is untouched.
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobSeederTests.cs` —
  - Renamed/corrected `SeedDefaultConfigurationsAsync_WhenConfigurationExists_SetsLastModifiedByToSystem`
    to `SeedDefaultConfigurationsAsync_WhenConfigurationExists_AndSeededFieldsUnchanged_PreservesLastModifiedBy`,
    which now asserts `LastModifiedBy` stays `"Admin"` (the old assertion encoded the bug).
  - Added new test `SeedDefaultConfigurationsAsync_WhenNothingChanged_PreservesLastModifiedAtAndBy`,
    which asserts both `LastModifiedBy` and `LastModifiedAt` survive a no-op seed pass.

## Tests

- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobSeederTests.cs` — 7 tests
  total (5 pre-existing untouched, 1 renamed/corrected, 1 new), all covering
  `RecurringJobSeeder.SeedDefaultConfigurationsAsync` behavior for: creation, no-duplication,
  DisplayName/Description resync, CronExpression/IsEnabled preservation, TimeZoneId resync, and
  the new "nothing changed -> audit fields preserved" guard (both `LastModifiedBy` alone and
  `LastModifiedBy` + `LastModifiedAt` together).

## How to verify

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~RecurringJobSeederTests"
# Passed! - Failed: 0, Passed: 7, Skipped: 0, Total: 7

dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Features.BackgroundJobs"
# Passed! - Failed: 0, Passed: 131, Skipped: 0, Total: 131

dotnet build Anela.Heblo.sln
# 0 Error(s)
```

Before the fix, both the renamed and the new test failed against the unfixed seeder
(`Assert.Equal("Admin", ...)` got `"System"` instead), confirming the tests actually exercise
the bug.

## Notes

- The task-context's `dotnet build`/`dotnet test` commands referenced `backend/Anela.Heblo.sln`,
  but the solution file actually lives at the repo root (`Anela.Heblo.sln`, one level up from
  `backend/`). Built/ran from the correct path; this is a pre-existing path discrepancy in the
  task-context doc, not something this task's scope covers changing.
- `dotnet format Anela.Heblo.sln` additionally reformatted two unrelated, pre-existing files
  (`GetMarketingPerformanceComparisonHandlerTests.cs`,
  `GetMarketingPerformanceMonthsHandlerTests.cs` under `Features/MarketingPerformance`) — these
  were reverted with `git checkout --` before committing, per the "surgical changes" rule; they
  are unrelated to this task and were not touched.
- No new EF1002 formatting warnings were introduced; the `dotnet format` "Unable to fix EF1002"
  notices are pre-existing raw-SQL interpolation warnings elsewhere in the codebase, unrelated to
  this change.

## PR Summary
Fixed `RecurringJobSeeder.SeedDefaultConfigurationsAsync` so it stops unconditionally overwriting
`LastModifiedAt`/`LastModifiedBy` on every existing recurring-job configuration row on every app
restart. Previously, every seed pass called `UpdateConfiguration` regardless of whether anything
actually changed, silently erasing an admin's real audit trail (e.g. who last edited a CRON
expression or enabled/disabled a job) even when the seeder itself made no changes. Now the seeder
only writes when at least one developer-owned field (`DisplayName`, `Description`, `TimeZoneId`)
actually differs from the stored row; `CronExpression` and `IsEnabled` remain admin-owned and were
already excluded from triggering a write.

### Changes
- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/RecurringJobSeeder.cs` —
  added `HasSeededFieldsChanged` guard before calling `UpdateConfiguration`/`UpdateAsync`
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobSeederTests.cs` — corrected
  a test that encoded the bug, and added a new test covering the "nothing changed" case

## Status
DONE
