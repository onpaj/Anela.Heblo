# Specification: Propagate Developer-Owned Metadata Updates in RecurringJobSeeder

## Summary
`RecurringJobSeeder.SeedDefaultConfigurationsAsync` currently inserts a `RecurringJobConfiguration` row only when no record exists for a job, silently skipping any job that already has a row. This means changes to code-owned fields (`DisplayName`, `Description`) made by developers in `RecurringJobMetadata` never reach the database, and the admin UI keeps showing stale values indefinitely. This spec covers changing the seeder to upsert: keep inserting new jobs as before, but for existing jobs, update `DisplayName` and `Description` from code while preserving the admin-editable `CronExpression` and `IsEnabled` fields untouched.

## Background
`RecurringJobConfiguration` rows are seeded once per job at startup from `IRecurringJob.Metadata`. Two of the fields on that entity are developer-owned and should always reflect the current code (`DisplayName`, `Description`); two are admin-owned and must survive across deployments because administrators may have changed them at runtime (`CronExpression`, `IsEnabled`). Today, the seeder's "insert if missing" logic means that once a row is created, it is never revisited — so developer-owned fields silently drift out of sync with the code whenever they're edited in `RecurringJobMetadata`, while admin-owned fields (which the seeder never had a chance to overwrite anyway) are not the problem. This is an internal architecture-review finding, not a user-facing bug report; there is no reported production incident, but the drift is real and will worsen as more jobs are added or renamed.

## Functional Requirements

### FR-1: Upsert developer-owned metadata on seeding
When `SeedDefaultConfigurationsAsync` runs and finds an existing `RecurringJobConfiguration` row for a job (matched by `JobName`), it must update that row's `DisplayName` and `Description` to match the current values from `job.Metadata`, using the existing `RecurringJobConfiguration.UpdateConfiguration(displayName, description, cronExpression, modifiedBy)` domain method. The `cronExpression` argument passed to `UpdateConfiguration` must be the **existing** row's current `CronExpression` (not the value from `job.Metadata`), so that the admin's runtime override is preserved. The `modifiedBy` argument must be `"System"`, consistent with the value already used for inserts.

**Acceptance criteria:**
- Given a job whose `RecurringJobConfiguration` row already exists in the database, when `SeedDefaultConfigurationsAsync` runs and `job.Metadata.DisplayName` or `job.Metadata.Description` differs from the stored values, the stored `DisplayName`/`Description` are updated to match `job.Metadata` after seeding completes.
- Given a job whose `RecurringJobConfiguration` row already exists and an administrator has set a custom `CronExpression` and/or `IsEnabled` value that differs from `job.Metadata.CronExpression` / `job.Metadata.DefaultIsEnabled`, when `SeedDefaultConfigurationsAsync` runs, the stored `CronExpression` and `IsEnabled` values are unchanged after seeding completes.
- Given a job whose `RecurringJobConfiguration` row does not yet exist, when `SeedDefaultConfigurationsAsync` runs, a new row is inserted with all fields taken from `job.Metadata`, exactly as today (no behavior change for the insert path).
- The updated row's `LastModifiedAt` and `LastModifiedBy` reflect the seeder's update (set by `UpdateConfiguration`), i.e. `LastModifiedBy == "System"`.
- `IRecurringJobConfigurationRepository.UpdateAsync` is called exactly once per pre-existing job on each seeding run, regardless of whether the metadata actually changed (idempotent no-op update is acceptable; see NFR-1).

### FR-2: No change to IsEnabled handling
`IsEnabled` is not passed to `UpdateConfiguration` (that method does not accept it) and is not touched by the update path at all. The existing row's `IsEnabled` value is left exactly as-is when updating an existing configuration.

**Acceptance criteria:**
- After seeding a pre-existing job whose `IsEnabled` differs from `job.Metadata.DefaultIsEnabled`, the stored `IsEnabled` value still differs from `job.Metadata.DefaultIsEnabled` in the same direction (i.e., unchanged).

## Non-Functional Requirements

### NFR-1: Performance
Seeding runs once at application startup against a small, fixed set of recurring jobs (expected: low tens of rows). Calling `UpdateAsync` unconditionally for every pre-existing job (rather than diffing first and skipping unchanged rows) is acceptable and preferred for simplicity; no batching or conditional-skip optimization is required. This must not introduce a measurable startup delay (sub-second for the full seeding pass under normal job counts).

### NFR-2: Security
No new authentication, authorization, or data-sensitivity concerns are introduced. The seeder already runs as part of trusted startup code with direct repository access; `modifiedBy` continues to be hardcoded to `"System"` for both insert and update paths, distinguishing seeder-driven changes from admin-driven changes (which presumably use a different `modifiedBy` value in the admin UI's own update path).

## Data Model
No schema or entity changes. Existing entity: `RecurringJobConfiguration` (backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/RecurringJobConfiguration.cs), with the pre-existing `UpdateConfiguration(displayName, description, cronExpression, modifiedBy)` method reused as-is. Fields involved:
- Developer-owned (always overwritten by seeder): `DisplayName`, `Description`
- Admin-owned (preserved by seeder, never overwritten): `CronExpression`, `IsEnabled`
- Audit fields updated by `UpdateConfiguration`: `LastModifiedAt`, `LastModifiedBy`

## API / Interface Design
No public API or UI changes. This is an internal change to `RecurringJobSeeder.SeedDefaultConfigurationsAsync` in `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/RecurringJobSeeder.cs`:

```csharp
foreach (var config in defaultConfigurations)
{
    var existing = await _repository.GetByJobNameAsync(config.JobName, cancellationToken);
    if (existing == null)
    {
        await _repository.AddAsync(config, cancellationToken);
    }
    else
    {
        existing.UpdateConfiguration(
            config.DisplayName,
            config.Description,
            existing.CronExpression,   // preserve admin override
            "System");
        await _repository.UpdateAsync(existing, cancellationToken);
    }
}
```

No changes to `IRecurringJobConfigurationRepository`, `IRecurringJob`, `RecurringJobMetadata`, or any controller/DTO.

## Dependencies
- `RecurringJobConfiguration.UpdateConfiguration` (already exists, no domain changes needed).
- `IRecurringJobConfigurationRepository.UpdateAsync` (already exists; used elsewhere for admin-driven updates, so no new repository method is needed).
- No external services or new libraries.

## Out of Scope
- Any change to how `CronExpression` or `IsEnabled` are seeded or reconciled (they remain admin-owned and are never overwritten by the seeder).
- Any change to the admin UI, controllers, or endpoints that expose `RecurringJobConfiguration`.
- Adding change-detection/diffing to skip no-op `UpdateAsync` calls (see NFR-1 — unconditional update on every run is accepted).
- Handling jobs that are removed from code but still have a database row (orphaned configurations) — not mentioned in the brief and not addressed here.
- Any validation changes to `UpdateConfiguration` itself (its existing null/whitespace checks are assumed sufficient, since `job.Metadata` values are code-defined and expected to always be valid).
- Unit/integration test additions beyond what's implied by the acceptance criteria above (test implementation is a development-team concern, not specified further here).

## Open Questions
None.

## Status: COMPLETE
