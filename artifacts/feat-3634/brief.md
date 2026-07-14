## Module
BackgroundJobs

## Finding
`RecurringJobConfigurationRepository.SeedDefaultConfigurationsAsync` (line 53–58 of `backend/src/Anela.Heblo.Persistence/BackgroundJobs/RecurringJobConfigurationRepository.cs`) executes one `GetByJobNameAsync` database call per job inside a `foreach` loop, then a final `SaveChangesAsync`:

```csharp
foreach (var config in defaultConfigurations)
{
    var existing = await GetByJobNameAsync(config.JobName, cancellationToken); // N separate queries
    if (existing == null)
    {
        await _context.RecurringJobConfigurations.AddAsync(config, cancellationToken);
    }
}
await _context.SaveChangesAsync(cancellationToken);
```

For N registered jobs this issues N + 1 DB round-trips at every application startup.

## Why it matters
N+1 is a well-known anti-pattern. While the current job count is small, the pattern grows proportionally with new jobs and is avoidable at zero cost. Startup time and connection pool pressure increase unnecessarily.

## Suggested fix
Load all existing `JobName` values in a single query and filter in-memory before inserting:

```csharp
var existingNames = await _context.RecurringJobConfigurations
    .Select(c => c.JobName)
    .ToHashSetAsync(cancellationToken);

foreach (var config in defaultConfigurations.Where(c => !existingNames.Contains(c.JobName)))
{
    await _context.RecurringJobConfigurations.AddAsync(config, cancellationToken);
}

await _context.SaveChangesAsync(cancellationToken);
```

---
_Filed by daily arch-review routine on 2026-07-14._
