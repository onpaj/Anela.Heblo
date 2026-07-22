# Design: Persist and use per-job TimeZoneId in BackgroundJobs recurring job configuration

## Component Design

No new components are introduced. This closes a gap in an existing data path by threading a `TimeZoneId` value through five existing components in the `BackgroundJobs` vertical slice, plus a new EF Core migration.

### `RecurringJobConfiguration` (Domain entity)
`Anela.Heblo.Domain/Features/BackgroundJobs/RecurringJobConfiguration.cs`

- Responsibility: the persisted aggregate representing one recurring job's admin-configurable state (cron expression, enabled flag, timezone, audit fields).
- Interface change:
  - New property: `public string TimeZoneId { get; private set; }` — initialized to `string.Empty` in the private (EF Core) constructor, matching the pattern of `JobName`, `CronExpression`, etc.
  - Public constructor gains a required `timeZoneId` parameter, positioned immediately after `cronExpression` and before `isEnabled`:
    ```
    public RecurringJobConfiguration(string jobName, string displayName, string description,
        string cronExpression, string timeZoneId, bool isEnabled, string lastModifiedBy)
    ```
    Validated with the same `ValidationException` pattern used for other required strings (throw when null/whitespace), then assigned to `TimeZoneId`.
  - `UpdateConfiguration(...)` gains a `timeZoneId` parameter in the same relative position (after `cronExpression`, before `modifiedBy`), validated and assigned identically to how `cronExpression` is handled today. Note: `UpdateConfiguration` currently has no production caller (only two unit tests) — its contract is kept complete and consistent with the constructor, but no handler changes as a result.
  - `[MaxLength(100)]` attribute on `TimeZoneId`, matching the identifier-length convention already used for `JobName`/`LastModifiedBy` on this entity.
  - `UpdateCronExpression(cronExpression, modifiedBy)` is a separate, narrower method (used by `UpdateRecurringJobCronHandler`) and is **not** touched — it never set `TimeZoneId` and doesn't need to.

### `RecurringJobDto` (Application contract)
`Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RecurringJobDto.cs`

- Responsibility: the API-facing shape returned by the "get job" / "get jobs list" use cases.
- Interface change: new field `public string TimeZoneId { get; set; } = string.Empty;`, placed next to `CronExpression` to mirror the entity's field grouping. Remains a plain class (project rule: DTOs are never records).
- No change to `BackgroundJobsMappingProfile.cs` — the existing `CreateMap<RecurringJobConfiguration, RecurringJobDto>()` maps same-named properties automatically once `TimeZoneId` exists on both sides.

### `RecurringJobNextRunCalculator` (Application service)
`Anela.Heblo.Application/Features/BackgroundJobs/RecurringJobNextRunCalculator.cs`

- Responsibility: pure, static, in-memory computation of a job's next scheduled run time from its cron expression and timezone.
- Interface change — new signature:
  ```
  public static DateTime? Calculate(string cronExpression, bool isEnabled, string timeZoneId,
      DateTime utcNow, ILogger logger, string? jobName = null)
  ```
  - Resolves the timezone via `TimeZoneInfo.FindSystemTimeZoneById(timeZoneId)` instead of `TimeZoneInfo.FindSystemTimeZoneById(RecurringJobMetadata.DefaultTimeZoneId)`.
  - On `TimeZoneNotFoundException`, the warning log now reports the passed-in `timeZoneId` (not the default constant).
  - Error-handling contract unchanged: still returns `null` for a disabled job, unresolvable timezone, or invalid cron expression.
  - `RecurringJobMetadata.DefaultTimeZoneId` is no longer referenced inside this file; it remains the fallback used upstream when a job doesn't override its metadata timezone.

### Callers of the calculator
- `GetRecurringJobHandler.cs` (`UseCases/GetRecurringJob/`): `RecurringJobNextRunCalculator.Calculate(dto.CronExpression, dto.IsEnabled, dto.TimeZoneId, utcNow, _logger, dto.JobName)`.
- `GetRecurringJobsListHandler.cs` (`UseCases/GetRecurringJobsList/`): same change, applied inside the existing `foreach (var dto in jobDtos)` loop.

Both are single-argument insertions at an existing call site; no other logic in these handlers changes.

### `RecurringJobSeeder` (Application service)
`Anela.Heblo.Application/Features/BackgroundJobs/Services/RecurringJobSeeder.cs`

- Responsibility: seeds `RecurringJobConfiguration` rows from discovered `IRecurringJob` metadata (`SeedDefaultConfigurationsAsync`).
- Change: passes `job.Metadata.TimeZoneId` as the new constructor argument when building each `RecurringJobConfiguration`. No explicit fallback logic needed here — `RecurringJobMetadata.TimeZoneId` already defaults to `RecurringJobMetadata.DefaultTimeZoneId` (`"Europe/Prague"`) for any job that doesn't override it.

### EF Core configuration
`Anela.Heblo.Persistence/BackgroundJobs/RecurringJobConfigurationConfiguration.cs`

- Adds:
  ```
  builder.Property(e => e.TimeZoneId)
      .HasMaxLength(100)
      .IsRequired();
  ```
  Placed alongside the other `[MaxLength]`-backed string property configurations on this entity.

### Data flow (end-to-end)

```
IRecurringJob.Metadata.TimeZoneId
    → RecurringJobSeeder.SeedDefaultConfigurationsAsync
    → RecurringJobConfiguration.TimeZoneId (persisted, EF Core)
    → AutoMapper (CreateMap<RecurringJobConfiguration, RecurringJobDto>)
    → RecurringJobDto.TimeZoneId
    → RecurringJobNextRunCalculator.Calculate(..., timeZoneId, ...)
    → RecurringJobDto.NextRunAt
```

This runs parallel to, and now agrees with, the already-correct registration path:
```
IRecurringJob.Metadata.TimeZoneId → HangfireJobRegistrationHelper.RegisterOrUpdate → RecurringJobOptions.TimeZone
```
`RecurringJobMetadata` (code/config-sourced) remains the single authoritative source; the persisted `RecurringJobConfiguration.TimeZoneId` is a display-layer cache of it at seed time — the same pattern already used for `CronExpression`, `DisplayName`, and `Description`.

### Out of scope for this change
- `HangfireJobRegistrationHelper` / job registration and discovery flow — already correct, not modified.
- `UpdateRecurringJobCronHandler` — calls `RecurringJobConfiguration.UpdateCronExpression(cronExpression, modifiedBy)`, a distinct method not touched by this change.
- Any UI surfacing of `TimeZoneId` (admin frontend) — API-contract-only change; the generated TypeScript client will pick up the new field automatically on next build, but no frontend usage is added.
- New validation that a `TimeZoneId` string is a resolvable timezone at write time — unresolvable timezones remain handled defensively downstream (`TimeZoneNotFoundException` → warning + `null NextRunAt`), unchanged.

## Data Schemas

### Database schema change

Table `public.RecurringJobConfigurations` gains one column:

| Column | Type | Nullable | Default | Notes |
|---|---|---|---|---|
| `TimeZoneId` | `character varying(100)` (Postgres) / `nvarchar(100)` provider-equivalent | No | `'Europe/Prague'` | IANA/Windows timezone id resolvable via `TimeZoneInfo.FindSystemTimeZoneById`; mirrors `RecurringJobMetadata.TimeZoneId` / `RecurringJobMetadata.DefaultTimeZoneId`. |

No new tables, no relationship or index changes.

**Migration** — new file under `Anela.Heblo.Persistence/Migrations/`, named `<timestamp>_AddTimeZoneIdToRecurringJobConfigurations.cs`, generated via:
```bash
dotnet ef migrations add AddTimeZoneIdToRecurringJobConfigurations \
  --project backend/src/Anela.Heblo.Persistence --startup-project backend/src/Anela.Heblo.API
```
Content pattern (mirrors `AddLotLabelDriftCorrection.cs` — a single `NOT NULL` column with a database-level default so no manual backfill step is required):
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
`ApplicationDbContextModelSnapshot.cs` is regenerated automatically by `dotnet ef migrations add` — do not hand-edit; verify a `b.Property<string>("TimeZoneId")...` block appears in the `RecurringJobConfiguration` entity section.

Naming convention note: follow the PascalCase table/column style used from `20260424142720_StandardizeTableNamingToPascalCase.cs` onward (e.g. `20260715150507_AddLotLabelDriftCorrection.cs`) — **not** the snake_case style of the original `20260105125530_AddRecurringJobConfigurations.cs`, which was a one-off inconsistency later corrected.

Deployment note: per `docs/development/setup.md` § "Database Migrations Runbook", Production auto-migrates at app startup (`MigrateDatabaseAsync`); Development/Test/Staging do **not** auto-apply migrations — `dotnet ef database update` must be run against those environments before deploying dependent code, to avoid a missing-column error at query time.

### API response shape (additive, backward-compatible)

No new endpoints, no request contract changes. Existing responses gain one field on each `RecurringJobDto`:

**`GetRecurringJobResponse` (single job)** and **`GetRecurringJobsListResponse` (list)** — each contained `RecurringJobDto` now includes:
```json
{
  "jobName": "string",
  "displayName": "string",
  "description": "string",
  "cronExpression": "string",
  "timeZoneId": "string",
  "isEnabled": true,
  "nextRunAt": "2026-07-19T08:00:00Z",
  "...": "existing fields unchanged"
}
```
- `timeZoneId`: new field, non-nullable string (e.g. `"Europe/Prague"`, `"America/New_York"`).
- `nextRunAt`: unchanged shape, but now computed by `RecurringJobNextRunCalculator.Calculate` using `timeZoneId` instead of the previously hardcoded `RecurringJobMetadata.DefaultTimeZoneId`, so it matches the timezone Hangfire actually schedules the job in.

The OpenAPI TypeScript client is auto-generated on build and will pick up `timeZoneId` automatically; no manual frontend client changes are required.

### Domain constructor / method signatures (contract summary)

```csharp
// RecurringJobConfiguration — public constructor
public RecurringJobConfiguration(
    string jobName,
    string displayName,
    string description,
    string cronExpression,
    string timeZoneId,   // new, required, non-null/whitespace
    bool isEnabled,
    string lastModifiedBy)

// RecurringJobConfiguration.UpdateConfiguration
public void UpdateConfiguration(
    string displayName,
    string description,
    string cronExpression,
    string timeZoneId,   // new, required, non-null/whitespace
    string modifiedBy)

// RecurringJobNextRunCalculator.Calculate
public static DateTime? Calculate(
    string cronExpression,
    bool isEnabled,
    string timeZoneId,   // new; replaces internal use of RecurringJobMetadata.DefaultTimeZoneId
    DateTime utcNow,
    ILogger logger,
    string? jobName = null)
```

These are positional-parameter breaking changes at the C# level; every existing call site (production and the ~30 test call sites enumerated in the architecture review) must be updated to compile, but no external/API-facing contract is broken — the response payload change is purely additive.
