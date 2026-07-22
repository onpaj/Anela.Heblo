# Implementation: add-recurring-job-timezone-id

## What was implemented

Added a persisted, per-job `TimeZoneId` to `RecurringJobConfiguration` and threaded it end-to-end so `RecurringJobNextRunCalculator` computes `NextRunAt` using each job's own configured timezone, instead of the hardcoded `RecurringJobMetadata.DefaultTimeZoneId` ("Europe/Prague"). This closes the latent display bug described in the issue: Hangfire already scheduled jobs correctly per-timezone via `HangfireJobRegistrationHelper`, but the API/UI "next run" calculation ignored it.

## Files created/modified

- `backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/RecurringJobConfiguration.cs` — added `TimeZoneId` property, threaded through the constructor and `UpdateConfiguration` (positioned right after `cronExpression`), with the same required/non-empty validation as `CronExpression`.
- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RecurringJobDto.cs` — added `TimeZoneId` field.
- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/RecurringJobNextRunCalculator.cs` — `Calculate(...)` now takes a `timeZoneId` parameter and uses it instead of the constant default; removed the now-unused `Anela.Heblo.Domain.Features.BackgroundJobs` using.
- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/RecurringJobSeeder.cs` — passes `job.Metadata.TimeZoneId` into the entity constructor.
- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/UseCases/GetRecurringJob/GetRecurringJobHandler.cs` and `.../GetRecurringJobsList/GetRecurringJobsListHandler.cs` — pass `dto.TimeZoneId` into the calculator call.
- `backend/src/Anela.Heblo.Persistence/BackgroundJobs/RecurringJobConfigurationConfiguration.cs` — EF Core column config: `nvarchar(100)`, required.
- `backend/src/Anela.Heblo.Persistence/Migrations/20260718074122_AddTimeZoneIdToRecurringJobConfigurations.cs` (+ `.Designer.cs`, + updated `ApplicationDbContextModelSnapshot.cs`) — generated via `dotnet ef migrations add`; adds `TimeZoneId` column (`character varying(100)`, `NOT NULL DEFAULT 'Europe/Prague'`) to `RecurringJobConfigurations`, matching the PascalCase convention established by `20260424142720_StandardizeTableNamingToPascalCase.cs`.
- Test files updated for the new constructor/method signatures: `GetRecurringJobHandlerTests.cs`, `GetRecurringJobsListHandlerTests.cs`, `RecurringJobConfigurationRepositoryTests.cs`, `RecurringJobConfigurationTests.cs`, `RecurringJobDiscoveryServiceTests.cs`, `RecurringJobSeederTests.cs`, `RecurringJobStatusCheckerTests.cs`, `UpdateRecurringJobCronHandlerTests.cs`, `UpdateRecurringJobStatusHandlerTests.cs`.

`UpdateRecurringJobCronHandler` was confirmed (per the architect review) to call the separate, narrower `UpdateCronExpression(...)` method, not `UpdateConfiguration(...)` — it was left untouched apart from a one-line test-fixture update for the new constructor signature.

## Tests

- Existing ~95 `BackgroundJobs` unit tests updated for the new signatures and passing.
- `RecurringJobConfigurationTests.cs` — added new cases asserting `TimeZoneId` round-trips through the constructor and `UpdateConfiguration`, and that a null/empty `TimeZoneId` throws `ValidationException`.
- `RecurringJobNextRunCalculator` coverage (within the existing handler tests) now includes a case with a non-default `TimeZoneId` (e.g. `"America/New_York"`) asserting `NextRunAt` differs from the value computed under the default Prague timezone — proving the calculator now actually respects the passed-in timezone.

## How to verify

```
cd backend
dotnet build Anela.Heblo.sln
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~BackgroundJobs" --no-build
dotnet format Anela.Heblo.sln --verify-no-changes
```

## Notes

- Ran the full `Anela.Heblo.Tests` suite: 5900 passed, 76 failed, 4 skipped. All 76 failures are pre-existing `Testcontainers`/Docker-dependent integration tests (Leaflet, KnowledgeBase, Bank, Catalog, Invoices, MeetingTasks, Photobank, Purchase, Article repository/SQL-shape tests) that fail with `"Docker is either not running or misconfigured"` in this sandbox, which has no Docker daemon — none reference `BackgroundJobs` and none are affected by this change.
- This change is behavior-preserving for all current data: every existing `RecurringJobConfiguration` row will get `TimeZoneId = 'Europe/Prague'` via the migration default, identical to today's hardcoded behavior. Behavior only changes once a job is configured with a non-default timezone.
- Per CLAUDE.md, DB migrations are manual in this repo (not auto-applied outside Production) — the new migration must be applied with `dotnet ef database update` before/alongside deploying this code to Dev/Test/Staging.

## PR Summary

Fixes a latent display bug in the BackgroundJobs admin UI: `RecurringJobConfiguration` didn't persist a per-job timezone, so `RecurringJobNextRunCalculator` always assumed the default `Europe/Prague` timezone regardless of what a job's metadata actually specified — even though Hangfire itself already scheduled jobs correctly per-timezone. This PR adds a `TimeZoneId` column/property to the persisted entity and DTO and threads it through to the calculator and its two callers, so the "next run" display matches what Hangfire actually does. All existing jobs keep their current (correct) behavior via a `'Europe/Prague'` migration default; only a job configured with a non-default timezone would see a behavior change, and it would now be the *correct* one.

### Changes
- `RecurringJobConfiguration.cs` — new `TimeZoneId` property, constructor param, `UpdateConfiguration` param
- `RecurringJobDto.cs` — new `TimeZoneId` field
- `RecurringJobNextRunCalculator.cs` — `Calculate(...)` takes and uses `timeZoneId`
- `RecurringJobSeeder.cs`, `GetRecurringJobHandler.cs`, `GetRecurringJobsListHandler.cs` — pass the timezone through
- `RecurringJobConfigurationConfiguration.cs` + new EF Core migration — persist the column
- Nine BackgroundJobs test files updated/extended for the new signatures and new timezone-respecting behavior

## Status
DONE
