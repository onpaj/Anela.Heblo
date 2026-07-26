## Module
BackgroundJobs

## Finding
`RecurringJobSeeder.SeedDefaultConfigurationsAsync` only inserts rows for jobs that have no existing database record. If a row already exists, the method skips it silently:

```csharp
// backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/RecurringJobSeeder.cs  lines 28–37
foreach (var config in defaultConfigurations)
{
    var existing = await _repository.GetByJobNameAsync(config.JobName, cancellationToken);
    if (existing == null)
    {
        await _repository.AddAsync(config, cancellationToken);
    }
    // Nothing happens when existing != null
}
```

This means that if a developer changes a job's `DisplayName` or `Description` in `RecurringJobMetadata`, the change is never propagated to the database, and the UI permanently shows the old values.

## Why it matters
`DisplayName` and `Description` are developer-owned fields — they describe the job's purpose and are not editable by administrators. Leaving them permanently frozen at the values from the first seeding run causes configuration drift between the codebase and the database. The displayed names in the admin panel become stale and potentially misleading.

This is distinct from `CronExpression` and `IsEnabled`, which are intentionally preserved in the database because administrators may override them at runtime.

## Suggested fix
Split the upsert logic: always apply code-owned fields, never overwrite runtime-admin fields.

```csharp
if (existing == null)
{
    await _repository.AddAsync(config, cancellationToken);
}
else
{
    // Update developer-owned display metadata only
    existing.UpdateConfiguration(
        config.DisplayName,
        config.Description,
        existing.CronExpression,   // preserve admin override
        "System");
    await _repository.UpdateAsync(existing, cancellationToken);
}
```

`RecurringJobConfiguration.UpdateConfiguration` (domain entity method) already accepts `displayName`, `description`, and `cronExpression`, so no domain changes are needed.

---
_Filed by daily arch-review routine on 2026-07-18._
