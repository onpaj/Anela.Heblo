# Implementation Task Plan: Persist and use per-job TimeZoneId in BackgroundJobs recurring job configuration

## Planning rationale

This is a single, tightly-coupled change: one new property/column (`TimeZoneId`) must flow through the domain entity constructor, `UpdateConfiguration`, the DTO, the calculator signature, both GET handlers, the seeder, the EF Core configuration, a new migration, and ~30+ existing test call sites — all in one .NET solution that must build end-to-end. Splitting this into multiple tasks would leave the build broken between them (e.g. changing the entity constructor without updating every call site immediately breaks compilation). There is no independently-verifiable sub-chunk. This plan therefore defines **one task**.

---

### task: add-recurring-job-timezone-id

## Goal

Add a persisted, per-job `TimeZoneId` to `RecurringJobConfiguration` and thread it end-to-end so `RecurringJobNextRunCalculator` computes `NextRunAt` using each job's own configured timezone (as already used by Hangfire via `HangfireJobRegistrationHelper`) instead of the hardcoded `RecurringJobMetadata.DefaultTimeZoneId` ("Europe/Prague"). This closes a latent bug: today all jobs happen to use the default timezone so results are coincidentally correct, but any job configured with a non-default `TimeZoneId` would show a wrong "next run" time in the API/UI while Hangfire actually runs it correctly.

## Context

Background (from spec): `RecurringJobMetadata` already models `TimeZoneId` per job (default `"Europe/Prague"` via `RecurringJobMetadata.DefaultTimeZoneId`), and `HangfireJobRegistrationHelper.RegisterOrUpdate` already passes it to Hangfire correctly. The gap is entirely in the persistence/display path: `RecurringJobConfiguration` (entity), `RecurringJobDto`, `RecurringJobSeeder`, and `RecurringJobNextRunCalculator` all ignore per-job timezone today.

This is a behavior-preserving change for all *current* data (every existing job uses the default timezone) — it only changes behavior once a non-default `TimeZoneId` is actually configured (e.g. `FlexiAnalyticsSyncJob`, whose timezone is config-driven via `FlexiAnalyticsSyncOptions.TimeZone` and could already differ from the default in some environment).

Architecture review notes (authoritative — corrects the spec in two places):
1. **`UpdateRecurringJobCronHandler` does NOT call `UpdateConfiguration`.** It calls a separate, narrower method `job.UpdateCronExpression(request.CronExpression, modifiedBy)` (verified at `UpdateRecurringJobCronHandler.cs:67`), which never touches `TimeZoneId` and is **not modified** by this change. `UpdateConfiguration` currently has **zero production callers** — only two unit tests call it directly (`RecurringJobConfigurationRepositoryTests.cs`, `RecurringJobConfigurationTests.cs`). Its contract is still extended for consistency, but no handler needs to change because of it.
2. **Constructor/`UpdateConfiguration` parameter position:** insert `timeZoneId` immediately after `cronExpression`, before `isEnabled` (constructor) / before `modifiedBy` (`UpdateConfiguration`) — grouping the two schedule-related fields together, matching how `RecurringJobMetadata` treats them as a pair.

Migration mechanism (from arch review, verified against `docs/development/setup.md` § "Database Migrations Runbook"): Production auto-migrates at app startup (`MigrateDatabaseAsync` in `Program.cs`). Development/Test/Staging do **not** auto-apply migrations — `dotnet ef database update` must be run manually against those environments before/along with deploying this code (same hazard as prior migrations `AddDataQualityTables` → `StandardizeTableNamingToPascalCase`). This is an operational step outside this task's scope (per CLAUDE.md, DB migrations are manual, not automated in deployment) — just generate the migration correctly.

**Naming convention warning:** the live table is `"RecurringJobConfigurations"` (PascalCase, schema `public`), with PascalCase columns. The *original* migration `20260105125530_AddRecurringJobConfigurations.cs` used snake_case — that was a one-off inconsistency, later corrected by `20260424142720_StandardizeTableNamingToPascalCase.cs`. **Do not copy the snake_case style.** Copy the style of `20260715150507_AddLotLabelDriftCorrection.cs` (below), which is the current PascalCase convention.

## Current code (verified by reading the files directly — use as the exact basis for diffs)

### `backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/RecurringJobConfiguration.cs`
```csharp
public class RecurringJobConfiguration : Entity<string>
{
    [Required]
    [MaxLength(100)]
    public string JobName { get; private set; }
    // ... DisplayName [MaxLength(200)], Description [MaxLength(500)] ...

    [Required]
    [MaxLength(50)]
    public string CronExpression { get; private set; }

    public bool IsEnabled { get; private set; }
    public DateTime LastModifiedAt { get; private set; }

    [Required]
    [MaxLength(100)]
    public string LastModifiedBy { get; private set; }

    // Private constructor for EF Core
    private RecurringJobConfiguration()
    {
        JobName = string.Empty;
        DisplayName = string.Empty;
        Description = string.Empty;
        CronExpression = string.Empty;
        LastModifiedBy = string.Empty;
    }

    public RecurringJobConfiguration(
        string jobName,
        string displayName,
        string description,
        string cronExpression,
        bool isEnabled,
        string lastModifiedBy)
    {
        if (string.IsNullOrWhiteSpace(jobName))
            throw new ValidationException("JobName is required");
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ValidationException("DisplayName is required");
        if (string.IsNullOrWhiteSpace(description))
            throw new ValidationException("Description is required");
        if (string.IsNullOrWhiteSpace(cronExpression))
            throw new ValidationException("CronExpression is required");
        if (string.IsNullOrWhiteSpace(lastModifiedBy))
            throw new ValidationException("LastModifiedBy is required");

        JobName = jobName;
        Id = jobName; // JobName is the primary key
        DisplayName = displayName;
        Description = description;
        CronExpression = cronExpression;
        IsEnabled = isEnabled;
        LastModifiedAt = DateTime.UtcNow;
        LastModifiedBy = lastModifiedBy;
    }

    public void UpdateConfiguration(
        string displayName,
        string description,
        string cronExpression,
        string modifiedBy)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ValidationException("DisplayName is required");
        if (string.IsNullOrWhiteSpace(description))
            throw new ValidationException("Description is required");
        if (string.IsNullOrWhiteSpace(cronExpression))
            throw new ValidationException("CronExpression is required");
        if (string.IsNullOrWhiteSpace(modifiedBy))
            throw new ValidationException("ModifiedBy is required");

        DisplayName = displayName;
        Description = description;
        CronExpression = cronExpression;
        LastModifiedAt = DateTime.UtcNow;
        LastModifiedBy = modifiedBy;
    }

    public void Enable(string modifiedBy) { ... }
    public void Disable(string modifiedBy) { ... }

    public void UpdateCronExpression(string cronExpression, string modifiedBy)
    {
        // unchanged, unmodified by this task
    }
}
```

### `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RecurringJobDto.cs`
```csharp
public class RecurringJobDto
{
    public string JobName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string CronExpression { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public DateTime LastModifiedAt { get; set; }
    public string LastModifiedBy { get; set; } = string.Empty;
    public DateTime? NextRunAt { get; set; }
}
```

### `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/RecurringJobNextRunCalculator.cs`
```csharp
public static class RecurringJobNextRunCalculator
{
    public static DateTime? Calculate(string cronExpression, bool isEnabled, DateTime utcNow, ILogger logger, string? jobName = null)
    {
        if (!isEnabled) return null;

        TimeZoneInfo tz;
        try
        {
            tz = TimeZoneInfo.FindSystemTimeZoneById(RecurringJobMetadata.DefaultTimeZoneId);
        }
        catch (TimeZoneNotFoundException ex)
        {
            logger.LogWarning(ex, "Timezone '{TimeZoneId}' not found on host, NextRunAt will be null for job '{JobName}'",
                RecurringJobMetadata.DefaultTimeZoneId, jobName);
            return null;
        }

        try
        {
            var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(utcNow, tz);
            var nextLocal = CrontabSchedule.Parse(cronExpression).GetNextOccurrence(nowLocal);
            var nextUtc = TimeZoneInfo.ConvertTimeToUtc(
                DateTime.SpecifyKind(nextLocal, DateTimeKind.Unspecified), tz);
            return DateTime.SpecifyKind(nextUtc, DateTimeKind.Utc);
        }
        catch (CrontabException ex)
        {
            logger.LogWarning(ex, "Invalid CRON expression '{CronExpression}' for job '{JobName}', NextRunAt will be null",
                cronExpression, jobName);
            return null;
        }
    }
}
```
`RecurringJobMetadata.DefaultTimeZoneId = "Europe/Prague"` (const) and `RecurringJobMetadata.TimeZoneId` (instance property, defaults to `DefaultTimeZoneId`) live in `backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/RecurringJobMetadata.cs` — **not modified** by this task.

### `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/RecurringJobSeeder.cs`
```csharp
public async Task SeedDefaultConfigurationsAsync(IEnumerable<IRecurringJob> jobs, CancellationToken cancellationToken = default)
{
    var defaultConfigurations = jobs.Select(job => new RecurringJobConfiguration(
        job.Metadata.JobName,
        job.Metadata.DisplayName,
        job.Metadata.Description,
        job.Metadata.CronExpression,
        job.Metadata.DefaultIsEnabled,
        "System"
    )).ToArray();
    // ... existing add-if-not-exists loop, unchanged
}
```

### `backend/src/Anela.Heblo.Persistence/BackgroundJobs/RecurringJobConfigurationConfiguration.cs`
Configures `Id` (100), `JobName` (100), `DisplayName` (200), `Description` (500), `CronExpression` (50, note: **not** 100 — the entity's own `[MaxLength(50)]` on `CronExpression` differs from `TimeZoneId`'s planned 100), `IsEnabled`, `LastModifiedAt`, `LastModifiedBy` (100), plus two indexes (`JobName` unique, `IsEnabled`). No `TimeZoneId` entry today.

### `GetRecurringJobHandler.cs` (line 47-48) and `GetRecurringJobsListHandler.cs` (line 41-42)
Both currently call:
```csharp
RecurringJobNextRunCalculator.Calculate(dto.CronExpression, dto.IsEnabled, utcNow, _logger, dto.JobName)
```
(the list handler does this inside `foreach (var dto in jobDtos)`).

### `UpdateRecurringJobCronHandler.cs`
Calls `job.UpdateCronExpression(request.CronExpression, modifiedBy)` at line 67 — **confirmed this is NOT `UpdateConfiguration`**. Do not modify this file.

### Migration style to copy — `backend/src/Anela.Heblo.Persistence/Migrations/20260715150507_AddLotLabelDriftCorrection.cs`
```csharp
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLotLabelDriftCorrection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DriftEveryNLabels",
                schema: "public",
                table: "LotLabelCalibrations",
                type: "integer",
                nullable: false,
                defaultValue: 3);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DriftEveryNLabels",
                schema: "public",
                table: "LotLabelCalibrations");
        }
    }
}
```
This task's migration mirrors this shape but adds a `string` column with `maxLength: 100` and a string default instead of an `int` column.

### Existing test call sites that will break on the constructor/`UpdateConfiguration`/`Calculate` signature changes (verified via grep, counts = number of matching lines, not necessarily distinct call sites — some are on multi-line invocations)
All under `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/`:
- `RecurringJobConfigurationRepositoryTests.cs` — constructs `RecurringJobConfiguration` directly (incl. one `UpdateConfiguration(...)` call around line 130 per arch review) — 7 matches
- `RecurringJobConfigurationTests.cs` — direct entity unit tests, including one `UpdateConfiguration(...)` call around line 137 per arch review — 10 matches
- `RecurringJobDiscoveryServiceTests.cs` — a test double's `GetAllAsync` builds a `RecurringJobConfiguration` (around line 205: `jobName: "test-async-job"`, ...) — 1 match
- `RecurringJobSeederTests.cs` — 1 match
- `RecurringJobStatusCheckerTests.cs` — 2 matches
- `UpdateRecurringJobCronHandlerTests.cs` — 1 match (constructs a `RecurringJobConfiguration` as test fixture data; does not call `UpdateConfiguration` since the handler under test uses `UpdateCronExpression`)
- `UpdateRecurringJobStatusHandlerTests.cs` — 6 matches
- `GetRecurringJobHandlerTests.cs` — 2 matches (also likely asserts on `RecurringJobNextRunCalculator.Calculate` behavior / `dto.NextRunAt` — check for direct `Calculate(...)` calls too, not just the DTO/entity constructor)
- `GetRecurringJobsListHandlerTests.cs` — 12 matches

Also search these same test files (and any `RecurringJobNextRunCalculatorTests.cs` if it exists — check for it, it was not enumerated above but may exist) for direct calls to `RecurringJobNextRunCalculator.Calculate(...)`, which also need the new `timeZoneId` argument inserted.

## Files to create/modify

1. `backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/RecurringJobConfiguration.cs`
2. `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RecurringJobDto.cs`
3. `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/RecurringJobNextRunCalculator.cs`
4. `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/UseCases/GetRecurringJob/GetRecurringJobHandler.cs`
5. `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/UseCases/GetRecurringJobsList/GetRecurringJobsListHandler.cs`
6. `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/RecurringJobSeeder.cs`
7. `backend/src/Anela.Heblo.Persistence/BackgroundJobs/RecurringJobConfigurationConfiguration.cs`
8. New file: `backend/src/Anela.Heblo.Persistence/Migrations/<timestamp>_AddTimeZoneIdToRecurringJobConfigurations.cs` (+ matching `.Designer.cs`, generated by tooling)
9. `backend/src/Anela.Heblo.Persistence/Migrations/ApplicationDbContextModelSnapshot.cs` (regenerated automatically — do not hand-edit)
10. All test files listed above under `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/`: `RecurringJobConfigurationRepositoryTests.cs`, `RecurringJobConfigurationTests.cs`, `RecurringJobDiscoveryServiceTests.cs`, `RecurringJobSeederTests.cs`, `RecurringJobStatusCheckerTests.cs`, `UpdateRecurringJobCronHandlerTests.cs`, `UpdateRecurringJobStatusHandlerTests.cs`, `GetRecurringJobHandlerTests.cs`, `GetRecurringJobsListHandlerTests.cs`, plus any `RecurringJobNextRunCalculatorTests.cs` if present.

**Explicitly NOT modified:** `BackgroundJobsMappingProfile.cs` (AutoMapper same-name matching handles it automatically), `UpdateRecurringJobCronHandler.cs` (`UpdateCronExpression` is untouched), `HangfireJobRegistrationHelper.cs` / `HangfireRecurringJobScheduler.cs` / `RecurringJobMetadata.cs` (already correct, out of scope).

## Implementation steps

1. **Domain entity** (`RecurringJobConfiguration.cs`):
   - Add `[Required] [MaxLength(100)] public string TimeZoneId { get; private set; }` (place it after `CronExpression`, before `IsEnabled`, mirroring field grouping).
   - In the private (EF Core) constructor, add `TimeZoneId = string.Empty;`.
   - In the public constructor, insert `string timeZoneId` as a parameter immediately after `cronExpression`, before `isEnabled`. Add validation `if (string.IsNullOrWhiteSpace(timeZoneId)) throw new ValidationException("TimeZoneId is required");` alongside the other required-string checks, and assign `TimeZoneId = timeZoneId;` alongside the other assignments.
   - In `UpdateConfiguration(...)`, insert `string timeZoneId` immediately after `cronExpression`, before `modifiedBy`. Add the same validation and assignment pattern used for `cronExpression`.
   - Leave `Enable`, `Disable`, `UpdateCronExpression` untouched.

2. **DTO** (`RecurringJobDto.cs`): add `public string TimeZoneId { get; set; } = string.Empty;` placed immediately after `CronExpression`. Keep as a class (never a record, per project rule).

3. **Mapping**: no change needed to `BackgroundJobsMappingProfile.cs` — verify after step 1-2 that `CreateMap<RecurringJobConfiguration, RecurringJobDto>()` still compiles and picks up `TimeZoneId` (same-name auto-mapping).

4. **Calculator** (`RecurringJobNextRunCalculator.cs`):
   - Change signature to `public static DateTime? Calculate(string cronExpression, bool isEnabled, string timeZoneId, DateTime utcNow, ILogger logger, string? jobName = null)`.
   - Replace `TimeZoneInfo.FindSystemTimeZoneById(RecurringJobMetadata.DefaultTimeZoneId)` with `TimeZoneInfo.FindSystemTimeZoneById(timeZoneId)`.
   - Update the `TimeZoneNotFoundException` catch block's log call to pass `timeZoneId` instead of `RecurringJobMetadata.DefaultTimeZoneId` as the format argument (message text can stay the same, e.g. `"Timezone '{TimeZoneId}' not found on host, NextRunAt will be null for job '{JobName}'", timeZoneId, jobName`).
   - Do not change the null-return-on-failure contract otherwise. Remove the now-unused `using` for `RecurringJobMetadata` if it becomes unreferenced in this file (check — it's in the same namespace `Anela.Heblo.Domain.Features.BackgroundJobs`, likely no explicit `using` needed to remove).

5. **Callers**:
   - `GetRecurringJobHandler.cs` line ~47-48: change to `RecurringJobNextRunCalculator.Calculate(dto.CronExpression, dto.IsEnabled, dto.TimeZoneId, utcNow, _logger, dto.JobName)`.
   - `GetRecurringJobsListHandler.cs` line ~41-42 (inside the `foreach`): same change.

6. **Seeder** (`RecurringJobSeeder.cs`): in the `RecurringJobConfiguration` object construction inside `SeedDefaultConfigurationsAsync`, insert `job.Metadata.TimeZoneId` as the new argument immediately after `job.Metadata.CronExpression`, before `job.Metadata.DefaultIsEnabled`.

7. **EF Core configuration** (`RecurringJobConfigurationConfiguration.cs`): add, in the same block as the other `[MaxLength]`-backed string properties (e.g. right after the `CronExpression` property config):
   ```csharp
   builder.Property(e => e.TimeZoneId)
       .HasMaxLength(100)
       .IsRequired();
   ```

8. **EF Core migration**: after steps 1-7 compile, generate the migration from the repo root:
   ```bash
   cd backend && dotnet ef migrations add AddTimeZoneIdToRecurringJobConfigurations \
     --project src/Anela.Heblo.Persistence --startup-project src/Anela.Heblo.API
   ```
   Verify the generated `Up`/`Down` match this shape (adjust only if the tool output differs from expectations — do not hand-edit beyond fixing an incorrect default):
   ```csharp
   protected override void Up(MigrationBuilder migrationBuilder)
   {
       migrationBuilder.AddColumn<string>(
           name: "TimeZoneId",
           schema: "public",
           table: "RecurringJobConfigurations",
           type: "character varying(100)",
           maxLength: 100,
           nullable: false,
           defaultValue: "Europe/Prague");
   }

   protected override void Down(MigrationBuilder migrationBuilder)
   {
       migrationBuilder.DropColumn(
           name: "TimeZoneId",
           schema: "public",
           table: "RecurringJobConfigurations");
   }
   ```
   Confirm `ApplicationDbContextModelSnapshot.cs` was regenerated with a `b.Property<string>("TimeZoneId")...` entry in the `RecurringJobConfiguration` section. Do not hand-edit the snapshot.

9. **Fix every broken call site** the compiler reports after steps 1 and 4 (constructor, `UpdateConfiguration`, and `Calculate` signature changes) across production and test code:
   - Every `new RecurringJobConfiguration(...)` call (production: `RecurringJobSeeder.cs`, already done in step 6; test files listed above) — insert a `timeZoneId` argument (positionally after `cronExpression`). Use `"Europe/Prague"` (or `RecurringJobMetadata.DefaultTimeZoneId` if the test file already references that constant) for tests that don't care about timezone specifically; use a distinct value like `"America/New_York"` for tests that will assert timezone-aware behavior (see Tests section).
   - Every `UpdateConfiguration(...)` call (the two known ones in `RecurringJobConfigurationRepositoryTests.cs` and `RecurringJobConfigurationTests.cs`, plus any others found by the compiler) — insert a `timeZoneId` argument after `cronExpression`.
   - Every `RecurringJobNextRunCalculator.Calculate(...)` call in tests — insert a `timeZoneId` argument after `isEnabled`.
   - Do a final `dotnet build` sweep to catch any call site not identified by the greps above (the greps are a starting point, not guaranteed exhaustive — trust compiler errors as the source of truth for completeness).

## Tests to write/update

- **Update all existing broken call sites** identified in step 9 above so the full existing suite compiles and passes unchanged in behavior (existing assertions about `NextRunAt`, cron parsing, etc. must still pass since the default `"Europe/Prague"` argument reproduces current behavior).
- **`RecurringJobConfigurationTests.cs`**: add/update tests so:
  - Constructing with `timeZoneId = null` or whitespace throws `ValidationException` (mirroring the existing `cronExpression` null/whitespace test).
  - Constructing with a valid `timeZoneId` (e.g. `"America/New_York"`) sets `TimeZoneId` correctly.
  - `UpdateConfiguration(...)` with a new `timeZoneId` updates `TimeZoneId` on the entity (mirroring the existing `cronExpression` update assertion), and with null/whitespace throws `ValidationException`.
- **`RecurringJobNextRunCalculator` tests** (add to whichever test file currently covers `Calculate` — locate it via the existing call sites in `GetRecurringJobHandlerTests.cs`/`GetRecurringJobsListHandlerTests.cs`, or a dedicated `RecurringJobNextRunCalculatorTests.cs` if one exists; create one only if genuinely none exists and calculator logic isn't otherwise unit-tested):
  - **New required test**: assert that `Calculate` with a non-default `timeZoneId` (e.g. `"America/New_York"`) produces a different `NextRunAt` than `Calculate` with `"Europe/Prague"` for the same cron expression and `utcNow`, proving the calculator now actually uses the passed-in timezone rather than a hardcoded default. Use a cron expression/time where the two timezones' offsets cause a different next-occurrence date (e.g. a job scheduled near local midnight, so the UTC-crossing differs) — or, simpler and robust, assert the returned `NextRunAt` values differ by the expected UTC offset delta between the two zones at that instant.
  - Update the existing `TimeZoneNotFoundException` test (if present) to pass a bogus `timeZoneId` string directly as the parameter and assert the warning log now references that string, not `RecurringJobMetadata.DefaultTimeZoneId`.
- **`RecurringJobSeederTests.cs`**: update/add an assertion that seeded `RecurringJobConfiguration.TimeZoneId` equals the source `IRecurringJob.Metadata.TimeZoneId` (test with a job whose metadata has a non-default `TimeZoneId` to prove it's not just defaulting).
- **`GetRecurringJobHandlerTests.cs`** and **`GetRecurringJobsListHandlerTests.cs`**: update fixtures to include `TimeZoneId` on the underlying `RecurringJobConfiguration`/DTO, and add/update an assertion that `NextRunAt` in the response reflects the entity's own `TimeZoneId` (not the default) when a non-default value is used.
- **`RecurringJobConfigurationRepositoryTests.cs`**: update the existing `UpdateConfiguration` call/test to pass and assert `TimeZoneId` round-trips through persistence (if this test uses an in-memory/real DB context, confirm the new EF Core column mapping works).
- Run the full BackgroundJobs test namespace plus a full solution test run to confirm nothing else broke.

## Acceptance criteria

- `dotnet build` succeeds for the whole solution with no errors.
- `dotnet format` reports no changes needed (run `dotnet format` and ensure clean diff, or that it was run and applied) on all touched files.
- All touched/existing unit tests in `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/` pass, including the new/updated tests described above.
- Full existing test suite (`dotnet test`) passes — no regressions outside `BackgroundJobs`.
- `RecurringJobConfiguration` has a persisted `TimeZoneId` property (`[Required] [MaxLength(100)]`), settable via both the public constructor and `UpdateConfiguration`, with `ValidationException` on null/whitespace.
- `RecurringJobDto` exposes `TimeZoneId` (`string`, defaults to `string.Empty`), still a class.
- `GetRecurringJobHandler` and `GetRecurringJobsListHandler` pass `dto.TimeZoneId` into `RecurringJobNextRunCalculator.Calculate`.
- `RecurringJobNextRunCalculator.Calculate` resolves the timezone exclusively from its `timeZoneId` parameter; `RecurringJobMetadata.DefaultTimeZoneId` no longer appears anywhere in `RecurringJobNextRunCalculator.cs`.
- `RecurringJobSeeder` seeds new rows with `job.Metadata.TimeZoneId`.
- A new EF Core migration (`AddTimeZoneIdToRecurringJobConfigurations`, following the `AddLotLabelDriftCorrection.cs` PascalCase/`schema: "public"` style — not the old snake_case style) adds the `TimeZoneId` column as `NOT NULL DEFAULT 'Europe/Prague'`, `maxLength: 100`; `ApplicationDbContextModelSnapshot.cs` reflects it.
- `UpdateRecurringJobCronHandler.cs` is unmodified (verified: it calls `UpdateCronExpression`, not `UpdateConfiguration`).
- `BackgroundJobsMappingProfile.cs` is unmodified.
- At least one new test proves `RecurringJobNextRunCalculator.Calculate` produces a different `NextRunAt` for a non-default `timeZoneId` than for the default, demonstrating the fix actually works (not just that the code compiles).
- For current/default-timezone data, `NextRunAt` values are unchanged from pre-change behavior (behavior-preserving for existing jobs) — covered by the existing (updated) test assertions continuing to pass with `"Europe/Prague"` as the passed-in timezone.
