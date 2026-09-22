# Design: Remove redundant `JobName` column from `RecurringJobConfiguration`

## Component Design

### `RecurringJobConfiguration` (domain entity)
`backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/RecurringJobConfiguration.cs`

- `Id` (inherited from `Entity<string>`) remains the sole persisted identifier, assigned once in the public constructor from the `jobName` parameter.
- `JobName` becomes a computed, read-only property with no backing field and no independent assignment:
  ```csharp
  public string JobName => Id;
  ```
- The constructor no longer assigns `JobName` directly (only `Id = jobName;` remains); the parameterless EF constructor no longer needs to initialize `JobName` either, since it has no backing field.
- All other members (`DisplayName`, `Description`, `CronExpression`, `TimeZoneId`, `IsEnabled`, `LastModifiedAt`, `LastModifiedBy`, `UpdateConfiguration`, `Enable`, `Disable`, `UpdateCronExpression`, `ValidateCronFormat`) are unchanged.
- Responsibility: `JobName` exists purely as a semantically-named read accessor for callers (`RecurringJobSeeder`, AutoMapper) that conceptually think in terms of "job name" rather than "entity id" — it carries no independent state.

### `RecurringJobConfigurationConfiguration` (EF Core entity configuration)
`backend/src/Anela.Heblo.Persistence/BackgroundJobs/RecurringJobConfigurationConfiguration.cs`

- Remove the `builder.Property(e => e.JobName)...` block (lines mapping a separate `JobName` column).
- Remove the `builder.HasIndex(e => e.JobName).IsUnique().HasDatabaseName("IX_RecurringJobConfigurations_JobName")` block.
- `builder.HasKey(e => e.Id)` and `builder.Property(e => e.Id)...` (max length 100, required) are unchanged and remain the sole source of uniqueness for the job identifier.
- All other property/index configuration (`DisplayName`, `Description`, `CronExpression`, `TimeZoneId`, `IsEnabled`, `LastModifiedAt`, `LastModifiedBy`, the `IX_RecurringJobConfigurations_IsEnabled` index) is unchanged.
- Since `JobName` is now a computed property with no setter, EF Core's convention-based model builder will not attempt to map it automatically — no `[NotMapped]` attribute or `builder.Ignore(e => e.JobName)` call is required, but its absence should be confirmed by inspecting the EF Core model diff `dotnet ef migrations add` produces (it must not attempt to add a shadow property for `JobName`).

### `RecurringJobConfigurationRepository` (repository)
`backend/src/Anela.Heblo.Persistence/BackgroundJobs/RecurringJobConfigurationRepository.cs`

- `GetByJobNameAsync(string jobName, ...)`: query predicate changes from `c.JobName == jobName` to `c.Id == jobName`. Method name, signature, and parameter name (`jobName`) are unchanged — this is the argument, not a column reference, and stays semantically "look up by job name."
- `GetAllAsync(...)`: `.OrderBy(c => c.JobName)` changes to `.OrderBy(c => c.Id)`. Resulting order is unchanged since the two values were always identical.
- `AddAsync` and `UpdateAsync` are unaffected (they persist the whole tracked entity graph; `JobName` no longer participates in the persisted graph at all).

### Unaffected components (no code change, listed for completeness)
- `IRecurringJobConfigurationRepository` (interface) — signatures unchanged.
- `RecurringJobSeeder.SeedDefaultConfigurationsAsync` — reads `config.JobName` in-memory off an already-constructed entity; continues to work unchanged against the computed property.
- `BackgroundJobsMappingProfile` (`CreateMap<RecurringJobConfiguration, RecurringJobDto>()`) — AutoMapper's convention mapping reads `JobName` in-memory off a materialized entity, continues to work unchanged.
- `RecurringJobDto` — shape unchanged; no OpenAPI/TypeScript client regeneration needed.

## Data Schemas

### `RecurringJobConfigurations` table (schema `public`)

**Before:**
| Column | Type | Nullable | Constraint |
|---|---|---|---|
| `Id` | `character varying(100)` | not null | PK |
| `JobName` | `character varying(100)` | not null | Unique index `IX_RecurringJobConfigurations_JobName` |
| `DisplayName` | `character varying(200)` | not null | |
| `Description` | `character varying(500)` | not null | |
| `CronExpression` | `character varying(50)` | not null | |
| `TimeZoneId` | `character varying(100)` | not null | |
| `IsEnabled` | `boolean` | not null | Index `IX_RecurringJobConfigurations_IsEnabled` |
| `LastModifiedAt` | `timestamp without time zone` | not null | |
| `LastModifiedBy` | `character varying(100)` | not null | |

**After:**
| Column | Type | Nullable | Constraint |
|---|---|---|---|
| `Id` | `character varying(100)` | not null | PK |
| ~~`JobName`~~ | *(removed)* | | ~~Unique index `IX_RecurringJobConfigurations_JobName`~~ *(removed)* |
| `DisplayName` | `character varying(200)` | not null | |
| `Description` | `character varying(500)` | not null | |
| `CronExpression` | `character varying(50)` | not null | |
| `TimeZoneId` | `character varying(100)` | not null | |
| `IsEnabled` | `boolean` | not null | Index `IX_RecurringJobConfigurations_IsEnabled` |
| `LastModifiedAt` | `timestamp without time zone` | not null | |
| `LastModifiedBy` | `character varying(100)` | not null | |

### Migration
New EF Core migration, following the naming and style of the existing `20260718074122_AddTimeZoneIdToRecurringJobConfigurations` migration in `backend/src/Anela.Heblo.Persistence/Migrations/`:

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.DropIndex(
        name: "IX_RecurringJobConfigurations_JobName",
        schema: "public",
        table: "RecurringJobConfigurations");

    migrationBuilder.DropColumn(
        name: "JobName",
        schema: "public",
        table: "RecurringJobConfigurations");
}

protected override void Down(MigrationBuilder migrationBuilder)
{
    migrationBuilder.AddColumn<string>(
        name: "JobName",
        schema: "public",
        table: "RecurringJobConfigurations",
        type: "character varying(100)",
        maxLength: 100,
        nullable: false,
        defaultValue: "");

    migrationBuilder.CreateIndex(
        name: "IX_RecurringJobConfigurations_JobName",
        schema: "public",
        table: "RecurringJobConfigurations",
        column: "JobName",
        unique: true);
}
```

Note: the exact generated shape (order of `DropIndex`/`DropColumn`, and the `Down()` counterpart) should come from `dotnet ef migrations add DropRedundantJobNameColumn --project backend/src/Anela.Heblo.Persistence --startup-project backend/src/Anela.Heblo.API`, with the developer verifying the generated file matches this intent (per arch-review.r1.md Decision 3) rather than hand-writing it from scratch. `Down()`'s `defaultValue: ""` is a placeholder to satisfy the `nullable: false` constraint on rollback — since `Id` still holds the real value, a rollback that needs the old `JobName` values populated would additionally need a data-backfill step (`UPDATE "RecurringJobConfigurations" SET "JobName" = "Id"`), which is out of scope for this migration's auto-generated `Down()` and is noted here only for operational awareness if a rollback is ever exercised.

### API / request-response shapes
No API shape changes. `RecurringJobDto` (returned to the frontend) is unchanged:
```csharp
public class RecurringJobDto
{
    public string JobName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string CronExpression { get; set; } = string.Empty;
    public string TimeZoneId { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public DateTime LastModifiedAt { get; set; }
    public string LastModifiedBy { get; set; } = string.Empty;
    public DateTime? NextRunAt { get; set; }
}
```
`JobName` on this DTO is populated the same way as before (AutoMapper convention mapping from the entity's `JobName` property), just now sourced from a computed property instead of a persisted column.
