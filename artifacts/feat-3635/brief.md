## Module
BackgroundJobs

## Finding
`IRecurringJobConfigurationRepository` (line 8 of `backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/IRecurringJobConfigurationRepository.cs`) includes `SeedDefaultConfigurationsAsync`:

```csharp
public interface IRecurringJobConfigurationRepository
{
    Task<IEnumerable<RecurringJobConfiguration>> GetAllAsync(...);
    Task<RecurringJobConfiguration> GetByJobNameAsync(...);
    Task UpdateAsync(...);
    Task SeedDefaultConfigurationsAsync(IEnumerable<RecurringJobConfiguration> jobs, ...);  // ← startup-only
}
```

`SeedDefaultConfigurationsAsync` is called exactly once — from the API startup extension in `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` (line 463) — and never as part of any business operation. A domain repository interface describes the domain's data access contract; startup seeding is an infrastructure initialisation step, not a query or command issued by business logic.

**Concrete cost:** every test double of `IRecurringJobConfigurationRepository` must implement `SeedDefaultConfigurationsAsync`, even when the test is wholly unrelated to seeding. This is visible across existing tests:
- `HangfireRecurringJobSchedulerTests.cs` line 168 — stub returns `Task.CompletedTask`
- `RecurringJobDiscoveryServiceTests.cs` lines 186 and 222 — two more stubs returning `Task.CompletedTask`

## Why it matters
Violates Single Responsibility Principle: one interface now has two unrelated concerns (domain data access and system initialisation). Any new test of a handler that injects `IRecurringJobConfigurationRepository` must implement this unrelated method. It also couples the domain repository interface to the concept of "discovered jobs at startup", which is an application/infrastructure detail.

## Suggested fix
Remove `SeedDefaultConfigurationsAsync` from `IRecurringJobConfigurationRepository`. Extract it to a dedicated startup service with a narrow interface:

```csharp
// New in Application/Features/BackgroundJobs/Services/
public interface IRecurringJobSeeder
{
    Task SeedAsync(IEnumerable<RecurringJobConfiguration> jobs, CancellationToken cancellationToken = default);
}

// New class (or inline in ServiceCollectionExtensions if trivial)
public class RecurringJobSeeder : IRecurringJobSeeder
{
    private readonly IRecurringJobConfigurationRepository _repository;
    // ... delegates to repository, same logic
}
```

The startup extension calls `IRecurringJobSeeder.SeedAsync(...)` instead. `IRecurringJobConfigurationRepository` stays focused on domain CRUD.

---
_Filed by daily arch-review routine on 2026-07-14._
