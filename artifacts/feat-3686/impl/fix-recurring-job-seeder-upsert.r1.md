# Implementation: fix-recurring-job-seeder-upsert

## What was implemented
`RecurringJobSeeder.SeedDefaultConfigurationsAsync` now upserts instead of insert-only-if-missing. When a `RecurringJobConfiguration` row already exists for a discovered job, the seeder calls the existing domain method `existing.UpdateConfiguration(config.DisplayName, config.Description, existing.CronExpression, "System")` — passing the *existing* row's `CronExpression` back in so the admin's runtime override is preserved — and then persists the change via the existing `_repository.UpdateAsync(existing, cancellationToken)`. The insert branch (`existing == null`) is untouched. The XML doc comment above the method was updated to describe the new upsert behavior. No new abstractions, interfaces, or repository/domain methods were introduced.

Three new unit tests were added to the existing test class to cover the three acceptance criteria from the spec/task-plan:
1. Stale `DisplayName`/`Description` on an existing row get corrected to match `job.Metadata`.
2. An admin-customized `CronExpression` and `IsEnabled` survive seeding unchanged.
3. `LastModifiedBy` becomes `"System"` after the seeder updates a row (even if it was previously `"Admin"`).

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/RecurringJobSeeder.cs` — added the `else` branch in the seeding loop calling `UpdateConfiguration` + `UpdateAsync`; updated the method's XML doc comment.
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobSeederTests.cs` — added `SeedDefaultConfigurationsAsync_WhenConfigurationExists_UpdatesDisplayNameAndDescription`, `SeedDefaultConfigurationsAsync_WhenConfigurationExists_PreservesCronExpressionAndIsEnabled`, and `SeedDefaultConfigurationsAsync_WhenConfigurationExists_SetsLastModifiedByToSystem`.

## Tests
Ran the filtered test command from the task plan:
```
dotnet test Anela.Heblo.sln --filter "FullyQualifiedName~RecurringJobSeederTests"
```
Result: `Passed! - Failed: 0, Passed: 5, Skipped: 0, Total: 5` — all 5 tests in `RecurringJobSeederTests` pass, including the 2 pre-existing tests (`..._WhenEmpty_CreatesAllDefaultConfigurations`, `..._WhenConfigurationsExist_DoesNotDuplicate`) and the 3 new ones.

## How to verify
1. `cd backend && dotnet build Anela.Heblo.sln` — should build with no errors.
2. `dotnet test Anela.Heblo.sln --filter "FullyQualifiedName~RecurringJobSeederTests"` — should report 5/5 passed.
3. Inspect `RecurringJobSeeder.cs` — confirm the `else` branch calls `UpdateConfiguration` with `existing.CronExpression` (not `config.CronExpression`) so admin cron overrides are preserved, and that `IsEnabled` is never touched.

## Notes
No deviations from the task plan. The plan's exact code for both the test additions and the fixed seeder file matched the actual current source (verified field values against the existing `CreateMockJobs()` fixture before applying). `dotnet format --verify-no-changes` and the full solution test suite run are being executed as part of the pipeline's finishing/validation step, per the orchestrator flow, rather than repeated here.

## Status
DONE
