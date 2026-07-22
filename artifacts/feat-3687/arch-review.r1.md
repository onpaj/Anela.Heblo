# Architecture Review: Persist and use per-job TimeZoneId in BackgroundJobs recurring job configuration

## Skip Design: true

Backend-only change: one entity property, one DTO field, one calculator parameter, one migration. No UI surfacing (explicitly out of scope in spec). No new endpoints, no new interaction pattern.

## Architectural Fit Assessment

The feature slots cleanly into the existing `BackgroundJobs` vertical slice — every file the spec touches already exists and already has a clear seam for this addition (`RecurringJobConfiguration`, `RecurringJobDto`, `RecurringJobNextRunCalculator`, the two GET handlers, the seeder). No new abstractions, no new module boundary, no cross-module coupling. This is a same-shape, same-pattern extension of fields that already exist one layer over (`RecurringJobMetadata.TimeZoneId`) — closing a gap rather than introducing a new concept.

Verified against the codebase:
- `RecurringJobConfiguration` (`backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/RecurringJobConfiguration.cs`) is a plain `class` (`Entity<string>`), not a record — consistent with project rules, no conflict.
- `RecurringJobDto` (`backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RecurringJobDto.cs`) is already a plain class with `= string.Empty` defaults — the spec's proposed `TimeZoneId` field matches the existing style exactly.
- `BackgroundJobsMappingProfile` uses `CreateMap<RecurringJobConfiguration, RecurringJobDto>()` with no explicit member mappings — AutoMapper's default same-name-property matching will pick up `TimeZoneId` automatically once it exists on both sides. Confirmed no change needed there, per spec.

One factual correction to the spec, found during exploration (see Specification Amendments): the spec's "API / Interface Design" section states `UpdateRecurringJobCronHandler` calls `RecurringJobConfiguration.UpdateConfiguration(...)`. It does not — it calls a separate, narrower method, `UpdateCronExpression(cronExpression, modifiedBy)`. This changes the scope of caller updates slightly (fewer than the spec implies) and is corrected below.

## Proposed Architecture

### Component Overview

No new components. Five existing files change, one migration is added:

```
Domain:       RecurringJobConfiguration.cs          (+ TimeZoneId property, ctor param, UpdateConfiguration param)
Application:  RecurringJobDto.cs                     (+ TimeZoneId field)
Application:  RecurringJobNextRunCalculator.cs        (Calculate(...) gains timeZoneId param)
Application:  GetRecurringJobHandler.cs                (pass dto.TimeZoneId)
Application:  GetRecurringJobsListHandler.cs            (pass dto.TimeZoneId)
Application:  RecurringJobSeeder.cs                     (pass job.Metadata.TimeZoneId)
Persistence:  RecurringJobConfigurationConfiguration.cs (+ column config)
Persistence:  Migrations/<ts>_AddTimeZoneIdToRecurringJobConfigurations.cs (new)
Persistence:  Migrations/ApplicationDbContextModelSnapshot.cs (regenerated)
```

`BackgroundJobsMappingProfile.cs` — no change (confirmed).

### Key Design Decisions

#### Decision 1: Constructor parameter position for `timeZoneId`

**Options considered:**
- Append at the end of the constructor parameter list (minimizes textual diff per call site, but separates it from the cron-related fields it's conceptually paired with).
- Insert immediately after `cronExpression` (groups the two schedule-related fields together; matches how `RecurringJobMetadata` documents them as a pair).

**Chosen approach:** Insert after `cronExpression`, before `isEnabled`:
```csharp
public RecurringJobConfiguration(
    string jobName,
    string displayName,
    string description,
    string cronExpression,
    string timeZoneId,
    bool isEnabled,
    string lastModifiedBy)
```
Same ordering for `UpdateConfiguration`:
```csharp
public void UpdateConfiguration(
    string displayName,
    string description,
    string cronExpression,
    string timeZoneId,
    string modifiedBy)
```

**Rationale:** `cronExpression` and `timeZoneId` are read together everywhere they're consumed (`RecurringJobNextRunCalculator.Calculate`, `HangfireJobRegistrationHelper.RegisterOrUpdate`) — keeping them adjacent in the constructor signature makes future call sites self-documenting. This is a positional-parameter change either way (all ~30 existing call sites — see Risks — must be touched regardless of where the parameter lands), so there's no "cheaper" option; pick the more readable one.

#### Decision 2: `UpdateRecurringJobCronHandler` / `UpdateCronExpression` — do NOT touch

**Options considered:**
- Follow the spec literally and modify `UpdateRecurringJobCronHandler` to pass a `timeZoneId` argument into `UpdateConfiguration` (as the spec's "API / Interface Design" section directs).
- Leave `UpdateRecurringJobCronHandler` and `RecurringJobConfiguration.UpdateCronExpression` untouched, since they don't call `UpdateConfiguration` at all.

**Chosen approach:** Leave both untouched. Verified in `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/UseCases/UpdateRecurringJobCron/UpdateRecurringJobCronHandler.cs:67` — it calls `job.UpdateCronExpression(request.CronExpression, modifiedBy)`, a separate two-parameter method (`RecurringJobConfiguration.cs:113`) that only ever touched `CronExpression`, `LastModifiedAt`, `LastModifiedBy`. It does not call the four/five-parameter `UpdateConfiguration`. `UpdateCronExpression`'s signature is out of scope for this fix — it doesn't set `TimeZoneId` today and doesn't need to.

**Rationale:** `UpdateConfiguration` currently has **no production caller at all** — grep confirms its only call sites are two unit tests (`RecurringJobConfigurationRepositoryTests.cs:130`, `RecurringJobConfigurationTests.cs:137`). It exists as a general-purpose update method on the aggregate but nothing in the current UseCases wires up to it. Adding `timeZoneId` to it is still correct per spec (keep the domain method's contract complete and consistent with the constructor), but no handler needs to change to accommodate it. See Specification Amendments.

## Implementation Guidance

### Directory / Module Structure

No structural changes — every touched file already lives in its correct location per `docs/architecture/filesystem.md` (Domain entity in `Anela.Heblo.Domain/Features/BackgroundJobs/`, DTO in `Anela.Heblo.Application/Features/BackgroundJobs/Contracts/`, handlers under `UseCases/{UseCase}/`, EF config under `Anela.Heblo.Persistence/BackgroundJobs/`, migration under `Anela.Heblo.Persistence/Migrations/`).

### Interfaces and Contracts

**`RecurringJobConfiguration.cs`** — add property, private-ctor init, public-ctor param + validation, `UpdateConfiguration` param + validation:
```csharp
[Required]
[MaxLength(100)]
public string TimeZoneId { get; private set; }

// private ctor
TimeZoneId = string.Empty;

// public ctor — validate like the other required strings:
if (string.IsNullOrWhiteSpace(timeZoneId))
    throw new ValidationException("TimeZoneId is required");
...
TimeZoneId = timeZoneId;

// UpdateConfiguration — same validation pattern as cronExpression:
if (string.IsNullOrWhiteSpace(timeZoneId))
    throw new ValidationException("TimeZoneId is required");
...
TimeZoneId = timeZoneId;
```
`[MaxLength(100)]` matches the convention already used for `JobName`/`LastModifiedBy` (identifier-length strings) on this entity — confirmed by reading the file, not assumed.

**`RecurringJobDto.cs`** — add, placed next to `CronExpression` to mirror the entity's field grouping:
```csharp
public string CronExpression { get; set; } = string.Empty;
public string TimeZoneId { get; set; } = string.Empty;
public bool IsEnabled { get; set; }
```

**`RecurringJobNextRunCalculator.cs`** — exact new signature:
```csharp
public static DateTime? Calculate(string cronExpression, bool isEnabled, string timeZoneId, DateTime utcNow, ILogger logger, string? jobName = null)
{
    ...
    tz = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
    ...
    catch (TimeZoneNotFoundException ex)
    {
        logger.LogWarning(ex, "Timezone '{TimeZoneId}' not found on host, NextRunAt will be null for job '{JobName}'",
            timeZoneId, jobName);
        return null;
    }
```
`RecurringJobMetadata.DefaultTimeZoneId` is no longer referenced inside this file at all after the change (verified it has exactly one other reference, in `RecurringJobConfigurationTests.cs`/seeder fallback context — not in the calculator).

**Callers** — both are one-line diffs, confirmed exact current call shape:
- `GetRecurringJobHandler.cs:47-48`: `RecurringJobNextRunCalculator.Calculate(dto.CronExpression, dto.IsEnabled, dto.TimeZoneId, utcNow, _logger, dto.JobName)`
- `GetRecurringJobsListHandler.cs:41-42`: same change, inside the existing `foreach (var dto in jobDtos)` loop.

**`RecurringJobSeeder.cs`** — add one line to the existing object-initializer-style constructor call at line 23-30:
```csharp
var defaultConfigurations = jobs.Select(job => new RecurringJobConfiguration(
    job.Metadata.JobName,
    job.Metadata.DisplayName,
    job.Metadata.Description,
    job.Metadata.CronExpression,
    job.Metadata.TimeZoneId,
    job.Metadata.DefaultIsEnabled,
    "System"
)).ToArray();
```
`RecurringJobMetadata.TimeZoneId` already defaults to `DefaultTimeZoneId = "Europe/Prague"` (`RecurringJobMetadata.cs:41`), so no explicit fallback logic is needed here — every `IRecurringJob` implementation either sets it explicitly (as `FlexiAnalyticsSyncJob` does, from `FlexiAnalyticsSyncOptions.TimeZone`) or inherits the default.

**`UpdateRecurringJobCronHandler.cs` / `UpdateCronExpression`** — no change (Decision 2).

### Data Flow

Unchanged shape, one more field riding along: `IRecurringJob.Metadata.TimeZoneId` → `RecurringJobSeeder` → `RecurringJobConfiguration.TimeZoneId` (persisted) → `AutoMapper` → `RecurringJobDto.TimeZoneId` → `RecurringJobNextRunCalculator.Calculate(..., timeZoneId, ...)` → `RecurringJobDto.NextRunAt`. This now runs parallel to, and in agreement with, `RecurringJobDiscoveryService` → `HangfireJobRegistrationHelper.RegisterOrUpdate` → `RecurringJobOptions.TimeZone`, which already uses `job.Metadata.TimeZoneId` directly (`HangfireRecurringJobScheduler.cs:47`). Both paths now read from the same ultimate source per job; the persisted `RecurringJobConfiguration.TimeZoneId` is a cache of it for display purposes, not a competing source of truth (`RecurringJobMetadata` — sourced from code/config — remains authoritative; the DB column mirrors it at seed time, same pattern already used for `CronExpression`/`DisplayName`/`Description`).

### Migration — exact approach for this repo

**Mechanism confirmed:** `docs/development/setup.md` § "Database Migrations Runbook" (not just the CLAUDE.md one-liner) documents the actual split:
- **Production**: migrations run automatically at app startup via `MigrateDatabaseAsync` (`Program.cs:165`) — no manual step.
- **Development / Test / Staging**: migrations are **not** auto-applied. Deploying code that depends on a new migration before running `dotnet ef database update` against that environment produces `Npgsql.PostgresException: 42P01: relation "<table>" does not exist` (or, for a column addition, a missing-column error at query time). This exact hazard is already documented for `AddDataQualityTables` → `StandardizeTableNamingToPascalCase` and applies identically here.

**Table/column naming — verified, not assumed:** the *live* physical name is `"RecurringJobConfigurations"` (PascalCase, schema `public`), with PascalCase columns (`JobName`, `CronExpression`, etc.) — confirmed via `ApplicationDbContextModelSnapshot.cs:333-380`. The original `20260105125530_AddRecurringJobConfigurations.cs` migration created the table with **snake_case** names (`recurring_job_configurations`, `job_name`, ...) — this was a one-off inconsistency at the time, since **no snake-case naming convention is registered anywhere in `ApplicationDbContext`/`PersistenceModule`** (verified — no `UseSnakeCaseNamingConvention` or equivalent). It was corrected by a later migration, `20260424142720_StandardizeTableNamingToPascalCase.cs`, which explicitly renamed the table and every column/index back to PascalCase to match EF's default (no-convention) naming. **Do not copy the naming style of the original 2026-01-05 migration file** — copy the style of any migration after 2026-04-24 (e.g. `20260715150507_AddLotLabelDriftCorrection.cs`).

**Exact migration content** (mirrors `AddLotLabelDriftCorrection.cs`, the closest existing example — a single `NOT NULL` column with a C#-level default that both sets the DB default and backfills existing rows):
```bash
dotnet ef migrations add AddTimeZoneIdToRecurringJobConfigurations \
  --project backend/src/Anela.Heblo.Persistence --startup-project backend/src/Anela.Heblo.API
```
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
`ApplicationDbContextModelSnapshot.cs` is regenerated automatically by the `dotnet ef migrations add` command — do not hand-edit it; just verify the new `b.Property<string>("TimeZoneId")...` block appears in the `RecurringJobConfiguration` entity section (alongside `JobName` et al., ~line 357).

`RecurringJobConfigurationConfiguration.cs` — add, in the same block as the other `[MaxLength]`-backed string properties:
```csharp
builder.Property(e => e.TimeZoneId)
    .HasMaxLength(100)
    .IsRequired();
```

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Staging/Test 500s if app code deploys before the migration is applied (documented "ordering hazard" pattern, same as `AddDataQualityTables`/`StandardizeTableNamingToPascalCase`) | Medium | Apply `dotnet ef database update` to Staging/Test **before** merging/deploying the dependent code; verify via `/health/ready` per the runbook. Production is unaffected (auto-migrates at startup). |
| ~30 existing call sites of `new RecurringJobConfiguration(...)` across test files (`RecurringJobConfigurationRepositoryTests`, `RecurringJobConfigurationTests`, `GetRecurringJobsListHandlerTests`, `GetRecurringJobHandlerTests`, `RecurringJobStatusCheckerTests`, `RecurringJobDiscoveryServiceTests`, `RecurringJobSeederTests`, `UpdateRecurringJobStatusHandlerTests`) all break on the constructor signature change (positional args) | Low (mechanical, high count) | Add the constructor param as specified; the compiler will point at every failing call site — no risk of silently-wrong tests, just volume. Already called out in spec's acceptance criteria. |
| Spec's claim that `UpdateRecurringJobCronHandler` calls `UpdateConfiguration` is incorrect, could lead an implementer to add an unnecessary/incorrect change to that handler | Low | Corrected in this review (Decision 2) and in Specification Amendments below — implementer should skip that handler entirely. |
| `FlexiAnalyticsSyncJob` already reads `TimeZoneId` from `FlexiAnalyticsSyncOptions.TimeZone` (config-driven, defaults to `"Europe/Prague"` but is operator-overridable per environment) — this is not a purely hypothetical future scenario | Informational | No action needed beyond this fix; noted because it means the "latent" bug in the brief is one config change away from being live in any environment, which supports prioritizing this fix. |

## Specification Amendments

1. **§ "API / Interface Design", correct this sentence:** *"Any existing 'update job' command/handler (e.g. `UpdateRecurringJobCronHandler`) that calls `RecurringJobConfiguration.UpdateConfiguration(...)` must be updated to pass a `timeZoneId` argument, since that method's signature changes per FR-1.1."* — **This is factually incorrect.** `UpdateRecurringJobCronHandler` calls `RecurringJobConfiguration.UpdateCronExpression(cronExpression, modifiedBy)`, a different, narrower method that does not touch `TimeZoneId` and is not changing in this fix. `UpdateConfiguration` currently has zero production callers (only two unit tests call it directly). No handler-level change is required for this sentence's concern; remove it or replace with: "`UpdateConfiguration` gains a `timeZoneId` parameter per FR-1.1; it currently has no production caller, so no handler needs updating as a result."
2. **FR-1.1, constructor/`UpdateConfiguration` parameter position** — spec doesn't pin down where `timeZoneId` goes in the parameter list. This review fixes it: immediately after `cronExpression`, before `isEnabled` (ctor) / before `modifiedBy` (`UpdateConfiguration`). See Decision 1.

## Prerequisites

None. All target files exist today with the exact shapes described above; no other in-flight work in `BackgroundJobs` was found that would conflict (no other open changes to `RecurringJobConfiguration`, `RecurringJobDto`, or the calculator in this worktree).
