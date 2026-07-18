# Design: Propagate Developer-Owned Metadata Updates in RecurringJobSeeder

## Component Design

### `RecurringJobSeeder.SeedDefaultConfigurationsAsync` (modified)
`backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/RecurringJobSeeder.cs`

Responsibility: at application startup, ensure every discovered `IRecurringJob` has a corresponding `RecurringJobConfiguration` row, and keep that row's developer-owned fields (`DisplayName`, `Description`) in sync with the job's current `Metadata`, while never touching the admin-owned fields (`CronExpression`, `IsEnabled`).

Change is confined to the branch of the existing seeding loop where a row is found for a job (`existing != null`). No new methods, no new class members, no signature change to `SeedDefaultConfigurationsAsync` itself.

Loop body (per discovered job, `config` = in-memory `RecurringJobConfiguration` built from `job.Metadata`):

```csharp
foreach (var config in defaultConfigurations)
{
    var existing = await _repository.GetByJobNameAsync(config.JobName, cancellationToken);
    if (existing == null)
    {
        await _repository.AddAsync(config, cancellationToken);          // unchanged
    }
    else
    {
        existing.UpdateConfiguration(
            config.DisplayName,
            config.Description,
            existing.CronExpression,   // pass through the existing value: preserves admin override
            "System");
        await _repository.UpdateAsync(existing, cancellationToken);     // new: was previously a no-op branch
    }
}
```

- `IsEnabled` is never referenced in the update branch: `UpdateConfiguration` has no `isEnabled` parameter, so the entity's current value is left untouched by construction.
- `UpdateAsync` is called unconditionally for every pre-existing job on every run (no diff-and-skip), per NFR-1.
- The method's XML doc comment must be revised: it currently states the seeder "Only creates configurations for jobs that don't already exist," which is no longer accurate once the update branch is added.

### Reused, unmodified components
- **`RecurringJobConfiguration.UpdateConfiguration(string displayName, string description, string cronExpression, string modifiedBy)`** (`backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/RecurringJobConfiguration.cs:71-91`) — existing domain method, already validates non-blank args and stamps `LastModifiedAt`/`LastModifiedBy`. Called with the job's `DisplayName`/`Description` and the entity's own current `CronExpression` (not the code-defined one), plus `modifiedBy = "System"`.
- **`IRecurringJobConfigurationRepository.UpdateAsync(RecurringJobConfiguration, CancellationToken)`** (`backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/IRecurringJobConfigurationRepository.cs:8`) — existing repository method, implemented via EF Core `Update()` + `SaveChangesAsync()` in `RecurringJobConfigurationRepository`. Already used by admin-driven paths (`UpdateRecurringJobCronHandler`, `UpdateRecurringJobStatusHandler`); no changes to its implementation.
- **`IRecurringJobConfigurationRepository.GetByJobNameAsync`** and **`AddAsync`** — unchanged, used exactly as today for the insert path.

No new interfaces, no new repository methods, no new domain methods, no DI registration changes (`BackgroundJobsModule.cs` already wires up the repository).

## Data Schemas

No schema changes. `RecurringJobConfiguration` entity fields, unchanged shape, with ownership semantics now enforced by the seeder's update branch:

| Field | Ownership | Seeder behavior on existing row |
|---|---|---|
| `JobName` | Match key | Read-only (used to look up `existing`) |
| `DisplayName` | Developer-owned | Overwritten from `job.Metadata.DisplayName` via `UpdateConfiguration` |
| `Description` | Developer-owned | Overwritten from `job.Metadata.Description` via `UpdateConfiguration` |
| `CronExpression` | Admin-owned | Preserved: `existing.CronExpression` passed back into `UpdateConfiguration`, making it a no-op on this field |
| `IsEnabled` | Admin-owned | Preserved: not a parameter of `UpdateConfiguration`, left untouched |
| `LastModifiedAt` | Audit | Updated by `UpdateConfiguration` to current timestamp |
| `LastModifiedBy` | Audit | Updated by `UpdateConfiguration` to `"System"` |

No changes to request/response DTOs, controllers, or event payloads — this is an internal startup-seeding code path with no external API surface.
