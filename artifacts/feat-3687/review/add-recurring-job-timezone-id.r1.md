# Code Review: BackgroundJobs TimeZoneId persistence

## Summary
The implementation matches the task spec closely: `TimeZoneId` is added to `RecurringJobConfiguration` (constructor + `UpdateConfiguration`, both after `cronExpression`), threaded through `RecurringJobDto`, `RecurringJobNextRunCalculator.Calculate`, both handler call sites, the seeder, EF Core configuration, and a correctly-styled migration. Every call site of the changed signatures was updated, `dotnet build` is clean, `dotnet format --verify-no-changes` is clean, and all 95 BackgroundJobs tests plus the new required timezone-divergence test pass.

## Review Result: PASS

### task: add-recurring-job-timezone-id
**Status:** PASS

## Docs to Update
(none — task explicitly scoped DB migration application as an out-of-scope operational step already covered by `docs/development/setup.md`)

## Overall Notes

Verification performed:

- **`RecurringJobNextRunCalculator.Calculate`**: signature is now `(cronExpression, isEnabled, timeZoneId, utcNow, logger, jobName = null)`. `TimeZoneInfo.FindSystemTimeZoneById(timeZoneId)` replaces the old `RecurringJobMetadata.DefaultTimeZoneId` reference in both the resolution call and the `TimeZoneNotFoundException` log format argument. Confirmed via `grep -n "DefaultTimeZoneId"` that the string no longer appears anywhere in the file (the unused `using Anela.Heblo.Domain.Features.BackgroundJobs;` was correctly removed too), satisfying the acceptance criterion verbatim.
- **Call sites**: `grep -rn "RecurringJobNextRunCalculator.Calculate("` across `backend/` shows only the two production call sites (`GetRecurringJobHandler.cs`, `GetRecurringJobsListHandler.cs`), both updated to pass `dto.TimeZoneId`. `grep -rn "new RecurringJobConfiguration("` and `grep -rn "\.UpdateConfiguration("` show no remaining old-signature callers — every test file listed in the spec was updated with a `timeZoneId` argument in the correct position.
- **Migration**: `20260718074122_AddTimeZoneIdToRecurringJobConfigurations.cs` adds `TimeZoneId` as `character varying(100)`, `maxLength: 100`, `nullable: false`, `defaultValue: "Europe/Prague"` on `RecurringJobConfigurations` in schema `public` — matches the entity's `[MaxLength(100)]`/`[Required]` and the EF Core `HasMaxLength(100).IsRequired()` config exactly, and follows the `AddLotLabelDriftCorrection.cs` PascalCase style rather than the deprecated snake_case one. `ApplicationDbContextModelSnapshot.cs` was correctly regenerated with the matching `b.Property<string>("TimeZoneId")` entry.
- **`UpdateRecurringJobCronHandler.cs`** and **`BackgroundJobsMappingProfile.cs`**: confirmed via `git diff HEAD~1 HEAD` to be completely unmodified, as required.
- **Build**: `dotnet build Anela.Heblo.sln` → 0 errors (251 pre-existing warnings, unrelated to this change).
- **Format**: `dotnet format Anela.Heblo.sln --verify-no-changes` → no output, i.e. clean.
- **BackgroundJobs tests**: `dotnet test ... --filter "FullyQualifiedName~BackgroundJobs" --no-build` → 95/95 passed, including the new required test proving `Calculate` respects a non-default timezone (`GetRecurringJobHandlerTests.Handle_WhenJobHasNonDefaultTimeZone_UsesJobTimeZoneForNextRunAt`, with hand-verified DST arithmetic: fixed `utcNow` 2026-03-30 12:00 UTC → 08:00 EDT in New York, next `13:00` cron occurrence same day → 17:00 UTC; independently confirmed this differs from the Prague-timezone result of 2026-03-31 11:00 UTC, since Prague was already at 14:00 CEST at that instant), plus new `RecurringJobConfigurationTests` constructor/`UpdateConfiguration` timezone validation tests, and a `RecurringJobSeederTests` assertion that seeded `TimeZoneId` differs per job metadata (`"America/New_York"` for `invoice-classification`).
- **Full suite / Docker-failure claim spot check**: ran the entire `Anela.Heblo.Tests` suite (no filter): 5900 passed, 76 failed, 4 skipped — matching the impl summary's numbers exactly. Extracted all 76 failing test names and confirmed every one is a `*IntegrationTests`/`*SqlShapeTests` under `Bank`, `Catalog`, `Invoices`, `Leaflet`, `MeetingTasks`, `Photobank`, `Purchase`, `Persistence.GridLayouts`, `Persistence.Smartsupp`, `KnowledgeBase`, or `Article` namespace, and each failure's stack trace is the identical `System.ArgumentException: Docker is either not running or misconfigured` from `PostgreSqlBuilder.Build()` / Testcontainers. None reference `BackgroundJobs`. The claim is verified, not just plausible.
- **Round-trip persistence**: `RecurringJobConfigurationRepositoryTests.cs` includes both a construct-then-load assertion (`TimeZoneId == "America/New_York"`) and an `UpdateConfiguration`-then-reload assertion, both against the EF Core-backed repository, confirming the new column mapping works end-to-end as the spec required.

No issues found. The implementation is behavior-preserving for existing data (migration default `'Europe/Prague'`) and the fix is demonstrated to actually change behavior for non-default timezones, exactly as the task intended.
