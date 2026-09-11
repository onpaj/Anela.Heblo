## Module
BackgroundJobs

## Finding
`RecurringJobSeeder.SeedDefaultConfigurationsAsync` constructs `RecurringJobConfiguration` instances and calls `UpdateConfiguration` using `DateTime.UtcNow` directly on two lines:

- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/RecurringJobSeeder.cs:34` — `DateTime.UtcNow` passed as `lastModifiedAt` when creating a new config
- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/RecurringJobSeeder.cs:51` — `DateTime.UtcNow` passed as `modifiedAt` when updating an existing config

Every MediatR handler in this module injects `TimeProvider` and calls `_timeProvider.GetUtcNow().UtcDateTime` to get the current time. `RecurringJobSeeder` is the one service that bypasses this convention.

## Why it matters
`TimeProvider` is the project-wide abstraction that makes time-dependent code testable (frozen clock in tests). `RecurringJobSeederTests` cannot verify the exact audit timestamp written to the DB without mocking static time, so test assertions either skip the timestamp or use a loose approximation. It is also inconsistent with the rest of the module, creating two code paths for "get current time" in the same feature.

## Suggested fix
Inject `TimeProvider` into `RecurringJobSeeder` and replace both `DateTime.UtcNow` usages:

```csharp
public RecurringJobSeeder(
    IRecurringJobConfigurationRepository repository,
    TimeProvider timeProvider)
{
    _repository = repository;
    _timeProvider = timeProvider;
}

// Replace DateTime.UtcNow with:
var now = _timeProvider.GetUtcNow().UtcDateTime;
```

Two-line change; no logic change required.

---
_Filed by daily arch-review routine on 2026-09-03._
