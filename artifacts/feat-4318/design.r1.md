# Design: BackgroundJobs — RecurringJobSeeder must not overwrite audit fields when nothing changed

## Component Design

### `RecurringJobSeeder` (`backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/RecurringJobSeeder.cs`)

Responsibility unchanged: on startup, sync developer-owned fields (`DisplayName`, `Description`,
`TimeZoneId`) from discovered `IRecurringJob.Metadata` into `RecurringJobConfiguration` rows,
creating rows for newly discovered jobs and preserving admin-owned fields (`CronExpression`,
`IsEnabled`) for existing rows.

**New responsibility added:** before updating an existing row, the seeder must determine whether any
developer-owned field actually differs from what's stored, and skip the update entirely (no domain
mutator call, no repository write) when nothing differs — so admin-owned audit metadata
(`LastModifiedAt`, `LastModifiedBy`) is left untouched on a no-op seed pass.

**New private contract** (internal to the class, not exposed):

```csharp
private static bool HasSeededFieldsChanged(
    RecurringJobConfiguration existing,
    RecurringJobConfiguration config)
```

- Inputs: the currently-stored row (`existing`) and the freshly-built candidate constructed from
  code metadata (`config`), both already in scope inside the existing `foreach` loop.
- Output: `true` if `DisplayName`, `Description`, or `TimeZoneId` differ (ordinal `string`
  inequality) between the two; `false` if all three are identical.
- Pure function, no I/O, no side effects — trivially unit-testable in isolation if desired, though
  the plan may choose to cover it only through the seeder's existing integration-style tests (see
  below).

**Control flow change**, inside `SeedDefaultConfigurationsAsync`'s existing `foreach` loop, only in
the `existing != null` branch:

```csharp
else
{
    if (HasSeededFieldsChanged(existing, config))
    {
        existing.UpdateConfiguration(
            config.DisplayName,
            config.Description,
            existing.CronExpression,   // preserve admin override
            config.TimeZoneId,
            "System",
            now);
        await _repository.UpdateAsync(existing, cancellationToken);
    }
}
```

Everything else in the method — job discovery, `defaultConfigurations` construction, the `existing
== null` → `AddAsync` branch, the `now` computation — is unchanged.

### `RecurringJobConfiguration` (Domain entity)

No change. `UpdateConfiguration(...)` keeps its current signature and behavior (it is correct: when
called, it should stamp audit fields — the fix is entirely about *whether the caller calls it*, not
what it does when called).

## Data Schemas

No schema change. No migration. `RecurringJobConfiguration` table/columns are unchanged:
`JobName` (PK), `DisplayName`, `Description`, `CronExpression`, `TimeZoneId`, `IsEnabled`,
`LastModifiedAt`, `LastModifiedBy`.

No API request/response shape changes — this fix is entirely inside a startup service with no
controller, MediatR request, or DTO in its call path.
