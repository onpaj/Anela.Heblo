# Implementation: add-query-count-regression-test

## What was implemented
Added a failing regression test to `RecurringJobSeederTests` that proves `RecurringJobSeeder.SeedDefaultConfigurationsAsync` issues one DB round-trip per registered job today, using the same "counting repository wrapper" pattern already established in this codebase (`PackingMaterialsListQueryCountTests`).

## Files created/modified
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobSeederTests.cs` — added a private nested `CountingRepositoryWrapper` class implementing `IRecurringJobConfigurationRepository` that counts calls to `GetAllAsync` and `GetByJobNameAsync`, and added the test `SeedDefaultConfigurationsAsync_IssuesSingleBatchReadInsteadOfPerJobLookups` which seeds one pre-existing configuration plus several jobs with no existing row, then asserts exactly one `GetAllAsync` call and zero `GetByJobNameAsync` calls.

## Tests
- `RecurringJobSeederTests.SeedDefaultConfigurationsAsync_IssuesSingleBatchReadInsteadOfPerJobLookups` — new test, intentionally fails against the current implementation (proves the N+1 pattern exists). It will pass once Task 2 (`batch-load-existing-configurations`) replaces the per-item fetch with a single `GetAllAsync` call.

## How to verify
```
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~RecurringJobSeederTests.SeedDefaultConfigurationsAsync_IssuesSingleBatchReadInsteadOfPerJobLookups"
```
Confirmed FAIL as expected: `Assert.Equal() Failure: Values differ — Expected: 1, Actual: 0` (GetAllAsyncCallCount), i.e. the current implementation never calls `GetAllAsync` and instead calls `GetByJobNameAsync` once per job.

## Notes
No deviations from the task context — implemented exactly the `CountingRepositoryWrapper` class and test method specified, in the locations specified (wrapper after `MockRecurringJob`, test after `SeedDefaultConfigurationsAsync_WhenConfigurationExists_ResyncsTimeZoneIdFromMetadata`).

## PR Summary
Added a failing regression test proving `RecurringJobSeeder` issues one DB read per registered job (N+1) instead of a single batch read. This is task 1 of 2 for issue #4317; task 2 will fix the seeder to use `GetAllAsync` once, which will make this test pass.

### Changes
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobSeederTests.cs` — added `CountingRepositoryWrapper` and the new regression test

## Status
DONE
