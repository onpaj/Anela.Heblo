# Specification: Persist and use per-job TimeZoneId in BackgroundJobs recurring job configuration

## Summary
`RecurringJobConfiguration` (the persisted entity behind the BackgroundJobs admin UI) does not store a `TimeZoneId`, so `RecurringJobNextRunCalculator` always computes "next run" using the hardcoded default timezone (`Europe/Prague`), even though Hangfire itself is registered with the job's actual configured timezone via `HangfireJobRegistrationHelper`. This fix threads `TimeZoneId` through the domain entity, DTO, mapping, calculator, and seeder so the displayed "next run" time is always computed in the same timezone Hangfire actually uses.

## Background
`RecurringJobMetadata` already models `TimeZoneId` as a first-class, per-job concept (defaulting to `RecurringJobMetadata.DefaultTimeZoneId = "Europe/Prague"`), and `HangfireJobRegistrationHelper.RegisterOrUpdate` correctly passes it through to Hangfire's `RecurringJobOptions.TimeZone`. However, the persistence model (`RecurringJobConfiguration` entity, `RecurringJobDto`, and `RecurringJobSeeder`) drops this field entirely, and `RecurringJobNextRunCalculator` unconditionally uses the constant default when computing `NextRunAt`.

This is currently a **latent** bug: all jobs today happen to use the default timezone, so the calculator's hardcoded assumption produces correct results by coincidence. The moment a job is registered with a non-default `TimeZoneId`, Hangfire will schedule and execute it correctly in that timezone, but the admin UI's "Next run" column will silently display a wrong time computed in `Europe/Prague` instead — with no error, warning, or visible inconsistency between the two data sources (Hangfire dashboard vs. the API/UI). This spec closes that gap by making `TimeZoneId` a persisted, first-class property of `RecurringJobConfiguration`, flowing end-to-end from job discovery through to the calculator.

## Functional Requirements

### FR-1: Persist and use the job's configured timezone for "next run" calculation
The system must persist each recurring job's `TimeZoneId` alongside its other configuration (cron expression, enabled state, etc.), and `RecurringJobNextRunCalculator` must use that persisted, per-job value — not a hardcoded constant — when computing `NextRunAt`.

Scope of change (grounded in the current codebase, all under `backend/src/`):

1. **Domain entity** — `Anela.Heblo.Domain/Features/BackgroundJobs/RecurringJobConfiguration.cs`
   - Add `public string TimeZoneId { get; private set; }`.
   - Initialize to `string.Empty` in the private (EF Core) constructor, consistent with the other string properties.
   - Add a required `timeZoneId` parameter to the public constructor; validate it the same way other required string parameters are validated (`ValidationException` when null/whitespace); assign to `TimeZoneId`.
   - Add a `timeZoneId` parameter to `UpdateConfiguration(...)`; validate and assign it the same way `cronExpression` is validated and assigned, so timezone changes go through the same update path as cron changes.

2. **DTO** — `Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RecurringJobDto.cs`
   - Add `public string TimeZoneId { get; set; } = string.Empty;`, following the existing pattern of non-nullable string properties defaulting to `string.Empty` in this DTO.
   - Per project rule, this DTO **must remain a class** (not a C# record).

3. **Mapping** — `Anela.Heblo.Application/Features/BackgroundJobs/BackgroundJobsMappingProfile.cs`
   - No change needed. The existing `CreateMap<RecurringJobConfiguration, RecurringJobDto>();` maps same-named properties automatically; adding `TimeZoneId` to both sides is sufficient.

4. **Calculator** — `Anela.Heblo.Application/Features/BackgroundJobs/RecurringJobNextRunCalculator.cs`
   - Change `Calculate(string cronExpression, bool isEnabled, DateTime utcNow, ILogger logger, string? jobName = null)` to `Calculate(string cronExpression, bool isEnabled, string timeZoneId, DateTime utcNow, ILogger logger, string? jobName = null)`.
   - Replace `TimeZoneInfo.FindSystemTimeZoneById(RecurringJobMetadata.DefaultTimeZoneId)` with `TimeZoneInfo.FindSystemTimeZoneById(timeZoneId)`.
   - Update the `TimeZoneNotFoundException` log message to report the passed-in `timeZoneId` (not the default constant) so the warning reflects what was actually looked up.
   - Keep the existing null-return-on-failure behavior (disabled job, timezone not found, invalid cron) unchanged — this fix does not change the calculator's error-handling contract, only its timezone source.
   - `RecurringJobMetadata.DefaultTimeZoneId` remains as the fallback default used when constructing new configurations (see FR-1.6); it is no longer referenced directly inside the calculator.

5. **Callers** — update both call sites to pass the DTO's timezone:
   - `Anela.Heblo.Application/Features/BackgroundJobs/UseCases/GetRecurringJob/GetRecurringJobHandler.cs` (line ~47-48): `RecurringJobNextRunCalculator.Calculate(dto.CronExpression, dto.IsEnabled, dto.TimeZoneId, utcNow, _logger, dto.JobName)`.
   - `Anela.Heblo.Application/Features/BackgroundJobs/UseCases/GetRecurringJobsList/GetRecurringJobsListHandler.cs` (line ~41-42): same change, inside the `foreach (var dto in jobDtos)` loop.

6. **Seeder** — `Anela.Heblo.Application/Features/BackgroundJobs/Services/RecurringJobSeeder.cs`
   - In `SeedDefaultConfigurationsAsync`, pass `job.Metadata.TimeZoneId` as the new constructor argument when building each `RecurringJobConfiguration` from discovered `IRecurringJob` metadata, so newly-seeded rows start with the job's actual configured timezone (falling back to `RecurringJobMetadata.DefaultTimeZoneId` = `"Europe/Prague"` for any job that doesn't override it, since `RecurringJobMetadata.TimeZoneId` already defaults there).

7. **EF Core configuration and migration**
   - `Anela.Heblo.Persistence/BackgroundJobs/RecurringJobConfigurationConfiguration.cs`: add a `builder.Property(e => e.TimeZoneId).HasMaxLength(100).IsRequired();` entry, consistent with the `[MaxLength(100)]` attribute to be added on the entity property (matching the `JobName`/`Id` column length convention already used for identifier-like strings in this entity).
   - New EF Core migration (naming convention observed from `20260105125530_AddRecurringJobConfigurations.cs`, e.g. `AddTimeZoneIdToRecurringJobConfigurations`): add a `TimeZoneId` column, `nvarchar(100)` (or provider equivalent) `NOT NULL DEFAULT 'Europe/Prague'`, to the `public.RecurringJobConfigurations` table. Using a database-level default ensures no data loss/backfill step is needed — all existing rows currently rely on the default timezone, so they receive `'Europe/Prague'` automatically. Update `ApplicationDbContextModelSnapshot.cs` accordingly (standard EF migration output).

**Acceptance criteria:**
- `RecurringJobConfiguration` has a persisted `TimeZoneId` property, set via both the public constructor and `UpdateConfiguration`.
- `RecurringJobDto` exposes `TimeZoneId`.
- `GetRecurringJobHandler` and `GetRecurringJobsListHandler` pass `dto.TimeZoneId` into `RecurringJobNextRunCalculator.Calculate`.
- `RecurringJobNextRunCalculator.Calculate` resolves the timezone from its `timeZoneId` parameter, not from `RecurringJobMetadata.DefaultTimeZoneId`.
- `RecurringJobSeeder` seeds new `RecurringJobConfiguration` rows with `job.Metadata.TimeZoneId`.
- A new EF Core migration adds the `TimeZoneId` column with a `'Europe/Prague'` default and applies cleanly to an existing database with no data loss.
- For a job configured with a non-default `TimeZoneId` (e.g. `"America/New_York"`), `GET` endpoints returning `RecurringJobDto.NextRunAt` compute that value using the job's own timezone, matching the timezone Hangfire uses via `HangfireJobRegistrationHelper.RegisterOrUpdate`.
- All existing jobs (which use the default timezone) continue to report the same `NextRunAt` as before this change — i.e., this is a behavior-preserving change for current data, and only changes behavior once a non-default timezone is actually configured.
- Existing unit/integration tests covering `RecurringJobConfiguration`, `RecurringJobDto`, `RecurringJobNextRunCalculator`, `RecurringJobSeeder`, `GetRecurringJobHandler`, and `GetRecurringJobsListHandler` are updated to construct/assert with the new `TimeZoneId` parameter/property, and continue to pass.

## Non-Functional Requirements

### NFR-1: Performance
No measurable performance impact expected. This adds one `string` column to an existing lookup table and one additional constructor/method parameter passed by reference; no new queries, joins, or I/O are introduced. `RecurringJobNextRunCalculator.Calculate` remains an in-memory, synchronous computation per job, unchanged in complexity.

### NFR-2: Security
No new security surface. `TimeZoneId` is server-controlled configuration data (sourced from `IRecurringJob` metadata at seed time, or admin-updated), not user input from an untrusted external source. It flows through the same validation and `ValidationException` pattern already used for `CronExpression` in `RecurringJobConfiguration`. No new authentication/authorization concerns — this reuses the existing endpoints and their existing access control.

### NFR-3: Data integrity
The new `TimeZoneId` column must be `NOT NULL` with a database-level default (`'Europe/Prague'`) so that:
- The migration is safe to apply against the existing `RecurringJobConfigurations` table without a manual backfill step.
- Any row inserted outside the seeder path (if one ever exists) cannot end up with a null/missing timezone, preserving the invariant that `RecurringJobNextRunCalculator` always receives a resolvable `timeZoneId`.

## Data Model
`RecurringJobConfiguration` (table `public.RecurringJobConfigurations`) gains one column:

| Column | Type | Nullable | Default | Notes |
|---|---|---|---|---|
| `TimeZoneId` | `nvarchar(100)` / provider equivalent | No | `'Europe/Prague'` | IANA/Windows timezone id resolvable via `TimeZoneInfo.FindSystemTimeZoneById`; mirrors `RecurringJobMetadata.TimeZoneId` and `RecurringJobMetadata.DefaultTimeZoneId`. |

No new tables, no relationship changes. `RecurringJobDto` gains a parallel `TimeZoneId` field with no independent validation beyond what `RecurringJobConfiguration` already enforces at write time.

## API / Interface Design
No new endpoints. Existing endpoints backed by `GetRecurringJobRequest`/`GetRecurringJobResponse` (single job) and `GetRecurringJobsListRequest`/`GetRecurringJobsListResponse` (list) now return a `TimeZoneId` field on each `RecurringJobDto` in their response payload, and their `NextRunAt` value is computed using that timezone instead of the previously hardcoded default. This is an additive, backward-compatible change to the response contract (new field only); no request contract changes. Since the OpenAPI TypeScript client is auto-generated on build, the frontend's generated types will pick up `timeZoneId` automatically — no manual frontend client changes are required by this spec (any UI surfacing of the new field, if desired, is out of scope — see below).

Any existing "update job" command/handler (e.g. `UpdateRecurringJobCronHandler`) that calls `RecurringJobConfiguration.UpdateConfiguration(...)` must be updated to pass a `timeZoneId` argument, since that method's signature changes per FR-1.1. Where the job's timezone is not being changed by that specific command, pass through the entity's current `TimeZoneId` unchanged.

## Dependencies
- `NCrontab.Advanced` (already used by `RecurringJobNextRunCalculator` for cron parsing) — no version or usage change.
- `TimeZoneInfo.FindSystemTimeZoneById` / host OS timezone database — unchanged; same resolution mechanism already used by `HangfireJobRegistrationHelper`, now shared consistently by the calculator.
- EF Core migration tooling (existing `Anela.Heblo.Persistence` migrations pipeline) — this fix requires a new migration, applied manually per this repo's convention (database migrations are not automated in deployment).
- No new external services or libraries.

## Out of Scope
- Adding UI in the admin frontend to display or edit `TimeZoneId` per job (this spec only makes the field available on the API contract; any frontend surfacing is a separate follow-up).
- Adding validation that a `TimeZoneId` string is a resolvable timezone at the point of API/command input (beyond the existing null/whitespace check pattern already used for other required strings on the entity). `HangfireJobRegistrationHelper` and `RecurringJobNextRunCalculator` already handle unresolvable timezones defensively (`TimeZoneNotFoundException` / warning + null `NextRunAt`); this spec does not add new upfront validation.
- Changing `HangfireJobRegistrationHelper` or the job registration/discovery flow — it already correctly handles per-job timezones and is not modified by this fix.
- Backfilling or auditing historical `NextRunAt` values that were previously computed incorrectly for non-default-timezone jobs — not applicable today since no job currently uses a non-default timezone.
- Any change to `RecurringJobMetadata` itself — it already exposes `TimeZoneId` correctly.

## Open Questions
None. The brief and current codebase are precise enough to implement this without further clarification; all decisions above (migration default value, seeder fallback, `UpdateConfiguration` signature change, no frontend UI change) are reasonable, low-risk assumptions consistent with the existing code patterns and explicitly noted inline above.

## Status: COMPLETE
